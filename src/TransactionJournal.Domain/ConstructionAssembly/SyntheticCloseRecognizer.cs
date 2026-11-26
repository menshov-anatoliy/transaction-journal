using TransactionJournal.Domain.Materialization;

namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Чистая функция распознавания синтетических закрытий ног конструкции по
/// шаблону правила ac-43: линейное исполнение образует связки со встречными
/// опционными исполнениями, когда совпадает базовый актив, зазор не превышает
/// 60 минут (та же константа окна, что в сборке), страйк и экспирация
/// встречного опциона совпадают с открытой опционной ногой той же доски,
/// встречный опцион продаётся (его покупка связкой не считается), а
/// направления соответствуют правилу: закрытие длинного PUT — покупка
/// фьючерса и продажа CALL, длинного CALL — продажа фьючерса и продажа PUT.
/// Связка покрывает минимум непокрытых остатков линейного исполнения и
/// встречного опциона: одно исполнение может входить в несколько связок, у
/// каждой записи остаётся непокрытый остаток, исчерпанная (нулевой остаток)
/// запись в связках не участвует, полного покрытия сторон не требуется; связка
/// не закрывает больше открытой части закрываемой ноги с учётом уже покрытого
/// связками. При нескольких кандидатах связки берут раннее по времени, затем
/// по execId. Прогон детерминирован: записи упорядочены по (ExecTimeMs,
/// ExecId), остатки сторон и ног вычисляются на лету по мере скана, повторный
/// вызов над тем же составом записей даёт те же связки.
/// Traceability: issue:#100
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
		// кандидатов при остатках на свой момент прогона. Непокрытые остатки
		// опционных кандидатов и накопленное связками покрытие закрываемых ног
		// ведутся рядом: связка расходует часть остатка записи, исчерпанная
		// запись и полностью покрытая нога дальнейших связок не образуют.
		// Traceability: issue:#100
		var optionRemainders = new Dictionary<string, decimal>(StringComparer.Ordinal);
		foreach (var optionRecord in optionRecords)
		{
			optionRemainders[optionRecord.Execution.ExecId] = decimal.Abs(optionRecord.Execution.SignedQuantity);
		}

		var coveredByClosedLeg = new Dictionary<LegKey, decimal>();
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
				links.AddRange(MatchLinear(linearRecord, optionRecords, optionRemainders, coveredByClosedLeg, legNets, legSymbols));
			}
		}

		return links;
	}

	#endregion

	#region Правила сопоставления

	/// <summary>
	/// Сопоставляет линейное исполнение со встречными опционными кандидатами,
	/// пока у линейной записи остаётся непокрытый остаток. Связка покрывает
	/// минимум непокрытых остатков линейной записи и кандидата, поэтому одно
	/// исполнение может входить в несколько связок, а исчерпанный кандидат
	/// (нулевой остаток) в связках не участвует. Пул кандидатов упорядочен по
	/// прогону, поэтому связки берут ранних по времени, затем по execId.
	/// Traceability: issue:#100
	/// </summary>
	private static List<SyntheticCloseLink> MatchLinear(
		LinearRecord linearRecord,
		IReadOnlyList<OptionRecord> optionRecords,
		Dictionary<string, decimal> optionRemainders,
		Dictionary<LegKey, decimal> coveredByClosedLeg,
		Dictionary<LegKey, decimal> legNets,
		Dictionary<LegKey, string> legSymbols)
	{
		var links = new List<SyntheticCloseLink>();
		var linear = linearRecord.Execution;
		var baseCoin = linearRecord.BaseCoin;

		// Направления по ac-43: покупка фьючерса закрывает длинный пут
		// (встречный опцион — продаваемый CALL), продажа фьючерса — длинный
		// колл (встречный опцион — продаваемый PUT).
		var isPutClose = linear.SignedQuantity > 0m;
		var counterType = isPutClose ? OptionType.Call : OptionType.Put;
		var closedType = isPutClose ? OptionType.Put : OptionType.Call;

		// Непокрытый остаток линейного исполнения: каждая связка расходует его
		// часть, исчерпанный остаток завершает подбор кандидатов — непокрытая
		// часть остаётся в инбоксе/позиции как сейчас.
		// Traceability: issue:#100
		var remaining = decimal.Abs(linear.SignedQuantity);

		foreach (var candidate in optionRecords)
		{
			if (remaining <= 0m)
			{
				break;
			}

			// Исчерпанный кандидат (нулевой остаток) дальнейших связок не образует.
			// Traceability: issue:#100
			var candidateRemaining = optionRemainders[candidate.Execution.ExecId];
			if (candidateRemaining <= 0m)
			{
				continue;
			}

			var parts = candidate.Parts;
			// Встречный опцион в связке ac-43 только продаётся: покупка колла
			// той же доски — самостоятельная ставка, а не закрытие длинного
			// пута (пут закрывается покупкой фьючерса против продажи колла).
			// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-synthetic-put-close-recognized
			if (parts.Type != counterType
				|| string.Equals(parts.BaseCoin, baseCoin, StringComparison.Ordinal) == false
				|| Math.Abs(candidate.Execution.ExecTimeMs - linear.ExecTimeMs) > LinkGapMs
				|| candidate.Execution.SignedQuantity >= 0m)
			{
				continue;
			}

			// Закрываемая нога — опцион противоположного типа на той же доске
			// (страйк и экспирация совпадают со встречным опционом); связка
			// допустима только против длинной ноги.
			var closedKey = new LegKey(parts.BaseCoin, parts.ExpiryDate, parts.Strike, closedType);
			if (legNets.TryGetValue(closedKey, out var closedNet) == false
				|| closedNet <= 0m
				|| legSymbols.TryGetValue(closedKey, out var closedSymbol) == false)
			{
				continue;
			}

			// Связка покрывает минимум непокрытых остатков сторон и не больше
			// оставшейся открытой части закрываемой ноги с учётом уже покрытого
			// связками: кандидат сверх открытой части пропускается.
			// Traceability: issue:#100
			coveredByClosedLeg.TryGetValue(closedKey, out var covered);
			var quantity = Math.Min(remaining, candidateRemaining);
			if (quantity > closedNet - covered)
			{
				continue;
			}

			optionRemainders[candidate.Execution.ExecId] = candidateRemaining - quantity;
			remaining -= quantity;
			coveredByClosedLeg[closedKey] = covered + quantity;
			links.Add(new SyntheticCloseLink
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
			});
		}

		return links;
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
