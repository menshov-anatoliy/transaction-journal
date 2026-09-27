using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransactionJournal.Bybit;
using TransactionJournal.Data;
using TransactionJournal.Materialization;
using TransactionJournal.Sync;

namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Контракт use-case «Собрать конструкции» для тонких слоёв UI: полный пересбор
/// конструкций и привязок сделок из локального сырья одним действием. Экран
/// зависит от интерфейса, тесты подменяют его заглушкой.
/// </summary>
public interface IConstructionAssemblyService
{
	/// <summary>
	/// Полностью перестраивает конструкции и привязки сделок из сырых записей
	/// хранилища и возвращает счётчики итога пересбора.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены пересбора.</param>
	Task<ConstructionRebuildResult> RebuildAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Счётчики итога пересбора конструкций: конструкций создано, сделок привязано,
/// сделок осталось во «Входящих» — чисел для показа пользователю после команды.
// Traceability: change:add-construction-auto-assembly/design#d5
/// </summary>
public sealed record ConstructionRebuildResult
{
	/// <summary>Число построенных конструкций.</summary>
	public required int ConstructionsCount { get; init; }

	/// <summary>Число сделок, привязанных к конструкциям при пересборе.</summary>
	public required int BoundCount { get; init; }

	/// <summary>Число сделок, оставшихся непривязанными во «Входящих».</summary>
	public required int TradesInInbox { get; init; }
}

/// <summary>
/// Use-case пересбора конструкций: снимок сырых записей хранилища превращается
/// в план сборки детерминированным алгоритмом, затем план применяется к базе
/// одной транзакцией — доменные таблицы вычищаются и наполняются заново.
/// Удаление идёт прямым вычищением таблиц в обход поштучного удаления сервиса
/// конструкций: пересборка — массовая операция, карв-аут из запрета удаления
/// непустой конструкции. Каждый вызов создаёт короткоживущий контекст, поэтому
/// сервис безопасен в длительных сессиях Blazor Server.
// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#requirement-full-rebuild-semantics
/// Traceability: change:add-construction-auto-assembly/design#d1
/// </summary>
public sealed class ConstructionAssemblyService : IConstructionAssemblyService
{
	private readonly IJournalRawSnapshotStore _rawSnapshotStore;
	private readonly DbContextOptions<JournalDbContext> _options;
	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт команду пересбора над сырым хранилищем и опциями контекста журнала.</summary>
	/// <param name="rawSnapshotStore">Источник полного снимка сырых записей журнала.</param>
	/// <param name="options">Опции EF-контекста журнала; база развёрнута миграциями.</param>
	/// <param name="timeProvider">Поставщик времени для границы OTM-закрывающих; по умолчанию системные часы.</param>
	/// <exception cref="ArgumentNullException">Какая-либо обязательная зависимость не задана.</exception>
	public ConstructionAssemblyService(
		IJournalRawSnapshotStore rawSnapshotStore,
		DbContextOptions<JournalDbContext> options,
		TimeProvider? timeProvider = null)
	{
		_rawSnapshotStore = rawSnapshotStore ?? throw new ArgumentNullException(nameof(rawSnapshotStore));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	/// <inheritdoc cref="IConstructionAssemblyService.RebuildAsync" />
	public async Task<ConstructionRebuildResult> RebuildAsync(CancellationToken cancellationToken = default)
	{
		// Пересбор работает только над локальным сырьём: снимок читается целиком,
		// план строится в памяти и применяется одной транзакцией — сырьё, справочник
		// инструментов и состояние синхронизации операция не трогает.
		// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-rebuild-preserves-raw-storage
		var snapshot = await _rawSnapshotStore.LoadAsync(cancellationToken).ConfigureAwait(false);
		var plan = BuildPlan(snapshot);

		await ApplyPlanAsync(plan, cancellationToken).ConfigureAwait(false);

		return new ConstructionRebuildResult
		{
			ConstructionsCount = plan.Constructions.Count,
			BoundCount = plan.Bindings.Count,
			TradesInInbox = plan.InboxCount,
		};
	}

	#region План сборки из снимка сырья

	/// <summary>
	/// Строит план сборки из снимка сырых записей, не трогая базу: торговые записи
	/// выводятся из сырья напрямую (только биржевой тип Trade, знаковое количество
	/// по стороне), deliveries — из сырых delivery-записей и выведенных OTM-закрывающих.
	/// Справочник инструментов входом сборки не фильтруется: истёкшие опционы биржа
	/// отдаёт отказом 110023 и их спецификаций в справочнике нет, но сделки по ним
	/// обязаны попадать в сборку — атрибуты ноги разбираются из символа. Повторный
	/// прогон над тем же сырьём даёт тот же план. Публичен для контрольных сверок:
	/// диагностический тест сверяет план с контрольными показателями истории.
	// Traceability: change:add-construction-auto-assembly/design#d1
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-rebuild-reproduces-result
	/// </summary>
	public AssemblyPlan BuildPlan(JournalRawSnapshot snapshot)
	{
		var executions = BuildExecutions(snapshot.Executions);
		var deliveries = BuildDeliveries(snapshot, new InstrumentCatalog(snapshot.Instruments), executions, _timeProvider.GetUtcNow());

		return new ConstructionAssembler().Assemble(executions, deliveries);
	}

	/// <summary>Биржевой тип исполнения, из которого выводится сделка.</summary>
	private const string TradeExecType = "Trade";

	/// <summary>Сторона исполнения покупки — знак количества положительный.</summary>
	private const string BuySide = "Buy";

	/// <summary>Сторона исполнения продажи — знак количества отрицательный.</summary>
	private const string SellSide = "Sell";

	/// <summary>
	/// Выводит вход сборки из сырых записей: сделкой становится только исполнение
	/// биржевого типа Trade, знаковое количество берётся по стороне. Фандинг и
	/// прочие не-Trade записи сделками не являются и в сборку не попадают —
	/// так же, как и в материализаторе «Входящих». Повреждённый payload и
	/// неизвестная сторона остаются жёсткими ошибками.
	// Traceability: openspec:sync/bybit-history#requirement-non-trade-executions-are-not-trades
	/// </summary>
	private static List<AssemblyExecution> BuildExecutions(IReadOnlyList<RawExecution> rawExecutions)
	{
		var executions = new List<AssemblyExecution>(rawExecutions.Count);
		foreach (var rawExecution in rawExecutions)
		{
			var execution = ParseExecutionPayload(rawExecution);

			if (string.Equals(execution.ExecType, TradeExecType, StringComparison.OrdinalIgnoreCase) == false)
			{
				continue;
			}

			// Идентичность execId в строке хранилища и payload — условие идемпотентности:
			// расхождение означает повреждение сырья, как и в материализаторе.
			if (string.Equals(execution.ExecId, rawExecution.ExecId, StringComparison.Ordinal) == false)
			{
				throw new TradeMaterializationException(
					rawExecution.ExecId,
					$"Идентификатор исполнения полезной нагрузки ({execution.ExecId}) расходится со строкой хранилища ({rawExecution.ExecId}).");
			}

			var sideSign = execution.Side switch
			{
				BuySide => 1m,
				SellSide => -1m,
				_ => throw new TradeMaterializationException(
					rawExecution.ExecId,
					$"Сторона исполнения «{execution.Side}» не распознана: ожидается Buy или Sell."),
			};

			if (execution.ExecQty is null)
			{
				throw new TradeMaterializationException(
					rawExecution.ExecId,
					$"Запись исполнения {rawExecution.ExecId} не содержит исполненное количество (execQty).");
			}

			executions.Add(new AssemblyExecution
			{
				ExecId = rawExecution.ExecId,
				Category = rawExecution.Category,
				Symbol = execution.Symbol,
				ExecTimeMs = execution.ExecTimeMs,
				SignedQuantity = sideSign * execution.ExecQty.Value,
			});
		}

		return executions;
	}

	/// <summary>Разбирает JSON сырой записи в типизированную запись исполнения биржи.</summary>
	/// <exception cref="TradeMaterializationException">JSON некорректен или пуст.</exception>
	private static BybitExecution ParseExecutionPayload(RawExecution rawExecution)
	{
		try
		{
			return JsonSerializer.Deserialize<BybitExecution>(rawExecution.PayloadJson, BybitJson.Options)
				?? throw new TradeMaterializationException(
					rawExecution.ExecId,
					$"Полезная нагрузка записи исполнения {rawExecution.ExecId} оказалась пустой после разбора JSON.");
		}
		catch (JsonException exception)
		{
			throw new TradeMaterializationException(
				rawExecution.ExecId,
				$"Полезная нагрузка записи исполнения {rawExecution.ExecId} содержит некорректный JSON: {exception.Message}",
				exception);
		}
	}

	/// <summary>
	/// Собирает закрывающие события экспирации: реальные delivery-записи биржи плюс
	/// выведенные OTM-закрывающие — опционные символы, по которым были сделки, без
	/// биржевой записи, чьё время доставки уже наступило. Время доставки берётся из
	/// справочника, а для истёкших инструментов, которых в справочнике нет (биржа
	/// отвечает отказом 110023), — из символа: доска кодирует только дату, а биржа
	/// доставляет опционы в 08:00 UTC этой даты. OTM-событие выводится только по
	/// торговым символам: события по неторговым инструментам ничего не гасят, а
	/// лишние разрывы окон нарушили бы кластеризацию.
	// Traceability: adr:docs/adr/0002-option-expiry-closing-entries.md#option-expiry-closing-entries
	/// </summary>
	private static List<AssemblyDelivery> BuildDeliveries(
		JournalRawSnapshot snapshot,
		InstrumentCatalog catalog,
		IReadOnlyList<AssemblyExecution> executions,
		DateTimeOffset asOf)
	{
		var deliveries = snapshot.Deliveries
			.Select(delivery => new AssemblyDelivery
			{
				Symbol = delivery.Symbol,
				DeliveryTimeMs = delivery.DeliveryTimeMs,
			})
			.ToList();
		var deliveredSymbols = snapshot.Deliveries
			.Select(delivery => delivery.Symbol)
			.ToHashSet(StringComparer.Ordinal);

		// Торговые опционные символы берутся из входа сборки: нога может существовать
		// только у символа, по которому были сделки.
		foreach (var symbol in executions
			.Where(execution => string.Equals(execution.Category, InstrumentResolver.OptionsCategory, StringComparison.Ordinal))
			.Select(execution => execution.Symbol)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(symbol => symbol, StringComparer.Ordinal))
		{
			if (deliveredSymbols.Contains(symbol)
				|| OptionSymbolParser.TryParse(symbol, out _) == false)
			{
				continue;
			}

			if (TryGetDeliveryTime(catalog, symbol) is not { } deliveryTime
				|| deliveryTime > asOf)
			{
				continue;
			}

			deliveries.Add(new AssemblyDelivery
			{
				Symbol = symbol,
				DeliveryTimeMs = deliveryTime.ToUnixTimeMilliseconds(),
			});
		}

		return deliveries;
	}

	/// <summary>
	/// Определяет время доставки опционного символа: спецификация справочника точна,
	/// а для отсутствующих в нём истёкших инструментов время выводится из доски
	/// символа как 08:00 UTC даты экспирации.
	/// </summary>
	private static DateTimeOffset? TryGetDeliveryTime(InstrumentCatalog catalog, string symbol)
	{
		if (catalog.TryGet(symbol, out var entry) && entry.DeliveryTime is not null)
		{
			return entry.DeliveryTime;
		}

		return OptionSymbolParser.TryParse(symbol, out var parts)
			? new DateTimeOffset(parts!.ExpiryDate, TimeSpan.Zero).AddHours(8)
			: null;
	}

	#endregion

	#region Применение плана одной транзакцией

	/// <summary>
	/// Применяет план к базе одной транзакцией: вычищает доменные таблицы
	/// (привязки с комментариями сделок, корректировки PnL, комментарии позиций,
	/// ручные пометки закрытия и сами конструкции с любым содержимым) и создаёт
	/// конструкции и привязки заново. Сырьё и служебные таблицы синхронизации
	/// остаются нетронутыми.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-rebuild-wipes-manual-data
	/// </summary>
	private async Task ApplyPlanAsync(AssemblyPlan plan, CancellationToken cancellationToken)
	{
		using var db = new JournalDbContext(_options);
		await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

		// Дочерние записи вычищаются перед конструкциями: внешние ключи привязок и
		// корректировок запрещают тихое каскадное удаление, пересборка снимает их
		// явным вычищением целиком.
		await db.TradeUserdata.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
		await db.PnLAdjustments.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
		await db.PositionComments.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
		await db.ManualCloseMarks.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
		await db.Constructions.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

		// Конструкции вставляются в порядке плана: порядковый номер плана задаёт
		// и порядок строк базы, поэтому повторный прогон воспроизводит состояние.
		var created = plan.Constructions
			.Select(planned => new Construction
			{
				Name = planned.Name,
				Status = ConstructionStatus.Open,
			})
			.ToList();
		db.Constructions.AddRange(created);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		// Соответствие «номер плана → ключ базы» строится по фактическим
		// сгенерированным ключам, а не по предположению о нумерации SQLite.
		var databaseIdByPlanId = new Dictionary<long, long>(created.Count);
		for (var index = 0; index < plan.Constructions.Count; index++)
		{
			databaseIdByPlanId[plan.Constructions[index].Id] = created[index].Id;
		}

		db.TradeUserdata.AddRange(plan.Bindings
			.Select(binding => new TradeUserdata
			{
				ExecId = binding.Key,
				ConstructionId = databaseIdByPlanId[binding.Value],
			}));
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion
}
