using Microsoft.EntityFrameworkCore;
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
	/// Строит план сборки из снимка сырых записей: сделки выводятся материализатором
	/// (только записи биржевого типа Trade, знаковое количество по стороне), deliveries —
	/// из сырых delivery-записей и выведенных OTM-закрывающих. Повторный прогон над
	/// тем же сырьём даёт тот же план.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-rebuild-reproduces-result
	/// </summary>
	private AssemblyPlan BuildPlan(JournalRawSnapshot snapshot)
	{
		var catalog = new InstrumentCatalog(snapshot.Instruments);
		var tradeMaterializer = new TradeMaterializer(new InstrumentResolver(catalog));
		var trades = tradeMaterializer.Materialize(snapshot.Executions).Trades;

		var executions = trades
			.Select(trade => new AssemblyExecution
			{
				ExecId = trade.ExecId,
				Category = trade.Category,
				Symbol = trade.Symbol,
				ExecTimeMs = trade.ExecutedAt.ToUnixTimeMilliseconds(),
				SignedQuantity = trade.Quantity,
			})
			.ToList();
		var deliveries = BuildDeliveries(snapshot, catalog, executions, _timeProvider.GetUtcNow());

		return new ConstructionAssembler().Assemble(executions, deliveries);
	}

	/// <summary>
	/// Собирает закрывающие события экспирации: реальные delivery-записи биржи плюс
	/// выведенные OTM-закрывающие — опционные символы, по которым были сделки, без
	/// биржевой записи, чьё время доставки из справочника уже наступило. OTM-событие
	/// выводится только по торговым символам: события по неторговым инструментам
	/// ничего не гасят, а лишние разрывы окон нарушили бы кластеризацию.
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
				|| OptionSymbolParser.TryParse(symbol, out _) == false
				|| catalog.TryGet(symbol, out var entry) == false
				|| entry.DeliveryTime is null)
			{
				continue;
			}

			if (entry.DeliveryTime.Value <= asOf)
			{
				deliveries.Add(new AssemblyDelivery
				{
					Symbol = symbol,
					DeliveryTimeMs = entry.DeliveryTime.Value.ToUnixTimeMilliseconds(),
				});
			}
		}

		return deliveries;
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
