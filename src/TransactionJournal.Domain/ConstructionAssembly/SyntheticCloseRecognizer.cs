using TransactionJournal.Domain.Materialization;

namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Чистая функция распознавания синтетических закрытий ног конструкции по
/// шаблону правила ac-43: линейное исполнение образует связку со встречным
/// опционным исполнением, когда совпадает базовый актив, зазор не превышает
/// 60 минут (та же константа окна, что в сборке), страйк и экспирация
/// встречного опциона совпадают с открытой опционной ногой той же доски,
/// количества равны и не превышают открытую часть ноги, а направления
/// соответствуют правилу: закрытие длинного PUT — покупка фьючерса и продажа
/// CALL, длинного CALL — продажа фьючерса и продажа PUT. Каждое исполнение
/// входит не более чем в одну связку; при нескольких кандидатах берётся
/// раннее по времени, затем по execId. Прогон детерминирован: записи
/// упорядочены по (ExecTimeMs, ExecId), остатки ног вычисляются на лету по
/// мере скана, повторный вызов над тем же составом записей даёт те же связки.
/// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#requirement-synthetic-close-recognition-deterministic
/// Traceability: change:synthetic-close-logical-layer/design#d1
/// Traceability: change:synthetic-close-logical-layer/design#d2
/// </summary>
public static class SyntheticCloseRecognizer
{
	/// <summary>Зазор связки в миллисекундах — та же константа окна, что в сборке конструкций.</summary>
	private static readonly long LinkGapMs = ConstructionAssembler.WindowGapMinutes * 60_000L;

	/// <summary>Категория опционных записей журнала — материал сопоставления связок.</summary>
	private const string OptionCategory = "option";

	/// <summary>Категория линейных записей журнала — инициирующая сторона связки.</summary>
	private const string LinearCategory = "linear";

	#region Точка входа

	/// <summary>
	/// Распознаёт синтетические связки над записями одной конструкции.
	/// Возвращает связки в порядке прогона — по времени линейного исполнения,
	/// затем по execId; повторный вызов над тем же составом записей даёт тот же
	/// состав связок.
	/// </summary>
	/// <param name="executions">Записи конструкции: опционные и линейные исполнения, привязанные к одной конструкции.</param>
	/// <exception cref="ArgumentNullException">Записи не заданы.</exception>
	// Распознавание вычисляется при чтении, а не хранится в сборке: детерминизм
	// следует из чистоты функции.
	// Traceability: change:synthetic-close-logical-layer/design#d1
	public static IReadOnlyList<SyntheticCloseLink> Recognize(IReadOnlyCollection<AssemblyExecution> executions)
	{
		ArgumentNullException.ThrowIfNull(executions);

		// Порядок прогона фиксируется правилом (ExecTimeMs, ExecId), повторный
		// execId схлопывается на первой встрече — тот же приём, что в сборке:
		// состав входа не зависит от порядка выдачи хранилища.
		var seenExecIds = new HashSet<string>(StringComparer.Ordinal);
		var orderedExecutions = executions
			.OrderBy(execution => execution.ExecTimeMs)
			.ThenBy(execution => execution.ExecId, StringComparer.Ordinal)
			.Where(execution => seenExecIds.Add(execution.ExecId))
			.ToList();

		// Записи классифицируются один раз: опционные — пул кандидатов связок,
		// линейные — инициирующие стороны; нулевые количества и неразобранные
		// символы в сопоставление не попадают.
		var optionRecords = new List<OptionRecord>();
		var linearRecords = new Dictionary<string, LinearRecord>(StringComparer.Ordinal);
		var legSymbols = new Dictionary<LegKey, string>();
		foreach (var execution in orderedExecutions)
		{
			if (execution.SignedQuantity == 0m)
			{
				continue;
			}

			if (string.Equals(execution.Category, OptionCategory, StringComparison.Ordinal)
				&& OptionSymbolParser.TryParse(execution.Symbol, out var parts))
			{
				var key = new LegKey(parts!.BaseCoin, parts.ExpiryDate, parts.Strike, parts.Type);
				optionRecords.Add(new OptionRecord(execution, parts));

				// Первый по прогону символ ноги — детерминированное имя закрываемой
				// ноги в связке: все записи ноги ведут к одному инструменту.
				legSymbols.TryAdd(key, execution.Symbol);
			}
			else if (string.Equals(execution.Category, LinearCategory, StringComparison.Ordinal)
				&& LinearSymbolParser.TryParseBaseCoin(execution.Symbol, out var baseCoin))
			{
				linearRecords.Add(execution.ExecId, new LinearRecord(execution, baseCoin!));
			}
		}

		// Однопроходный скан: остатки ног накапливаются по мере прохождения
		// опционных записей, поэтому линейное исполнение сопоставляется с пулом
		// кандидатов при остатках на свой момент прогона.
		var usedOptionExecIds = new HashSet<string>(StringComparer.Ordinal);
		var legNets = new Dictionary<LegKey, decimal>();
		var links = new List<SyntheticCloseLink>();
		foreach (var execution in orderedExecutions)
		{
			if (execution.SignedQuantity == 0m)
			{
				continue;
			}

			if (string.Equals(execution.Category, OptionCategory, StringComparison.Ordinal)
				&& OptionSymbolParser.TryParse(execution.Symbol, out var passingParts))
			{
				var key = new LegKey(passingParts!.BaseCoin, passingParts.ExpiryDate, passingParts.Strike, passingParts.Type);
				legNets.TryGetValue(key, out var net);
				legNets[key] = net + execution.SignedQuantity;
			}
			else if (string.Equals(execution.Category, LinearCategory, StringComparison.Ordinal)
				&& LinearSymbolParser.TryParseBaseCoin(execution.Symbol, out var linearBaseCoin))
			{
				var linearRecord = linearRecords[execution.ExecId];
				var link = MatchLinear(linearRecord, optionRecords, usedOptionExecIds, legNets, legSymbols);
				if (link is not null)
				{
					links.Add(link);
				}
			}
		}

		return links;
	}

	#endregion

	#region Правила сопоставления

	/// <summary>
	/// Сопоставляет линейное исполнение с первым неиспользованным встречным
	/// опционным кандидатом. Пул кандидатов упорядочен по прогону, поэтому
	/// первый подходящий — раннее по времени, затем по execId.
	/// </summary>
	private static SyntheticCloseLink? MatchLinear(
		LinearRecord linearRecord,
		IReadOnlyList<OptionRecord> optionRecords,
		HashSet<string> usedOptionExecIds,
		Dictionary<LegKey, decimal> legNets,
		Dictionary<LegKey, string> legSymbols)
	{
		var linear = linearRecord.Execution;
		var baseCoin = linearRecord.BaseCoin;
		var quantity = decimal.Abs(linear.SignedQuantity);

		// Направления по ac-43: покупка фьючерса закрывает длинный пут
		// (встречный опцион — продаваемый CALL), продажа фьючерса — длинный
		// колл (встречный опцион — продаваемый PUT).
		var isPutClose = linear.SignedQuantity > 0m;
		var counterType = isPutClose ? OptionType.Call : OptionType.Put;
		var closedType = isPutClose ? OptionType.Put : OptionType.Call;

		foreach (var candidate in optionRecords)
		{
			if (usedOptionExecIds.Contains(candidate.Execution.ExecId))
			{
				continue;
			}

			var parts = candidate.Parts;
			if (parts.Type != counterType
				|| string.Equals(parts.BaseCoin, baseCoin, StringComparison.Ordinal) == false
				|| Math.Abs(candidate.Execution.ExecTimeMs - linear.ExecTimeMs) > LinkGapMs
				|| decimal.Abs(candidate.Execution.SignedQuantity) != quantity)
			{
				continue;
			}

			// Закрываемая нога — опцион противоположного типа на той же доске
			// (страйк и экспирация совпадают со встречным опционом); связка
			// допустима только против длинной ноги и не закрывает больше её
			// открытой части.
			var closedKey = new LegKey(parts.BaseCoin, parts.ExpiryDate, parts.Strike, closedType);
			if (legNets.TryGetValue(closedKey, out var closedNet) == false
				|| closedNet <= 0m
				|| quantity > closedNet
				|| legSymbols.TryGetValue(closedKey, out var closedSymbol) == false)
			{
				continue;
			}

			usedOptionExecIds.Add(candidate.Execution.ExecId);
			return new SyntheticCloseLink
			{
				LinearExecId = linear.ExecId,
				LinearExecTimeMs = linear.ExecTimeMs,
				CounterOptionExecId = candidate.Execution.ExecId,
				CounterOptionExecTimeMs = candidate.Execution.ExecTimeMs,
				BaseCoin = baseCoin,
				ClosedLegSymbol = closedSymbol,
				ClosedLegType = closedType,
				Strike = parts.Strike,
				BoardExpiryDate = parts.ExpiryDate,
				Quantity = quantity,
			};
		}

		return null;
	}

	#endregion

	#region Модель прогона

	/// <summary>Опционный кандидат связки: исполнение с разобранными частями символа.</summary>
	private sealed record OptionRecord(AssemblyExecution Execution, OptionSymbolParts Parts);

	/// <summary>Линейная сторона связки: исполнение с базовым активом из символа.</summary>
	private sealed record LinearRecord(AssemblyExecution Execution, string BaseCoin);

	/// <summary>Ключ опционной ноги: доска определяется активом, экспирацией, страйком и типом.</summary>
	private readonly record struct LegKey(string BaseCoin, DateTime ExpiryDate, decimal Strike, OptionType Type);

	#endregion
}
