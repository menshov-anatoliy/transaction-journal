using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain;

namespace TransactionJournal.Infrastructure.ReadModels;


/// <summary>
/// Read-модель «Входящих»: сделки без привязки к конструкции, выведенные
/// из сырых записей исполнения материализатором синхронизации. Атрибуты сделки
/// соответствуют биржевой записи — время, инструмент, знаковое количество,
/// цена, комиссия и её валюта; запись существует, пока пользователь не привяжет
/// сделку к конструкции. Чтение ничего не материализует в базу и безопасно
/// в длительных сессиях Blazor Server.
// Traceability: openspec:domain/constructions#requirement-trade-single-binding
/// Traceability: change:add-core-domain/design#d6
/// </summary>
public sealed class InboxReadModel
{
	private readonly DbContextOptions<JournalDbContext> _options;

	/// <summary>Создаёт read-модель над опциями контекста журнала; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <exception cref="ArgumentNullException">Опции не заданы.</exception>
	public InboxReadModel(DbContextOptions<JournalDbContext> options)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
	}

	/// <summary>
	/// Возвращает сделки «Входящих» от новых к старым: сырые записи исполнения и
	/// ручные сделки пользователя материализуются детерминированной проекцией в общий
	/// список наравне друг с другом, затем из списка уходят сделки с действующей
	/// привязкой к конструкции, а строки упорядочиваются по времени исполнения
	/// по убыванию с детерминированным тай-брейком идентификатора по убыванию.
	/// Хронологический порядок самого материализатора не затрагивается.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Непривязанные сделки обоих источников с атрибутами исходной записи.</returns>
	/// <exception cref="TradeMaterializationException">Сырая запись или ручная сделка повреждена либо конфликтует с записью того же идентификатора.</exception>
	/// <exception cref="InstrumentResolveException">Символ опциона не прошёл сверку со справочником инструментов.</exception>
	public async Task<IReadOnlyList<MaterializedTrade>> ListAsync(CancellationToken cancellationToken = default)
	{
		using var db = new JournalDbContext(_options);
		var rawExecutions = await db.RawExecutions
			.OrderBy(execution => execution.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var rawInstruments = await db.RawInstruments
			.OrderBy(instrument => instrument.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		// Ручные сделки — второе сырьё «Входящих»: читаются тем же чтением, что и
		// биржевые записи, и материализуются в общий список проекцией наравне с ними.
		// Traceability: openspec:domain/manual-trades#requirement-manual-trade-entry
		var manualTrades = await db.ManualTrades
			.OrderBy(trade => trade.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var boundExecIds = (await db.TradeUserdata
				.Where(userdata => userdata.ConstructionId != null)
				.Select(userdata => userdata.ExecId)
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false))
			.ToHashSet(StringComparer.Ordinal);

		// «Входящие» — проекция сырья материализатором журнала: атрибуты сделки
		// берутся из исходной записи, а не из пользовательских данных.
		// Traceability: openspec:sync/bybit-history#requirement-new-records-land-in-inbox
		var materializer = new TradeMaterializer(new InstrumentResolver(new InstrumentCatalog(rawInstruments)));
		// Порядок строк «Входящих» — по времени исполнения от новых к старым; при
		// равном времени строки детерминированы тай-брейком идентификатора по убыванию:
		// execId у биржевых, ключ строки сырья у ручных, у которых execId отсутствует.
		// Хронологический порядок Materialize как доменный договор не меняется.
		// Traceability: openspec:ui/screens#scenario-inbox-trades-newest-first
		// Traceability: openspec:domain/manual-trades#scenario-manual-trade-source-attribute
		var inbox = materializer.Materialize(rawExecutions, manualTrades).Trades
			// Привязанная сделка покидает «Входящие», пока привязка не снята возвратом:
			// у биржевой привязка живёт в пользовательских данных, у ручной — в её сырье.
			// Traceability: openspec:domain/constructions#scenario-binding-removes-from-inbox
			.Where(trade => trade.Source == TradeSource.Manual
				? trade.ConstructionId is null
				: boundExecIds.Contains(trade.ExecId!) == false)
			.OrderByDescending(trade => trade.ExecutedAt)
			.ThenByDescending(trade => trade.ExecId ?? ManualOrderKey(trade.ManualTradeId), StringComparer.Ordinal)
			.ToList();
		return inbox;
	}

	/// <summary>Ключ ручной сделки в тай-брейке порядка «Входящих»: идентификатор строки сырья.</summary>
	private static string ManualOrderKey(long? manualTradeId) => $"manual:{manualTradeId?.ToString(CultureInfo.InvariantCulture)}";
}

