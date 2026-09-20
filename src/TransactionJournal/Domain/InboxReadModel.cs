using Microsoft.EntityFrameworkCore;
using TransactionJournal.Data;
using TransactionJournal.Materialization;

namespace TransactionJournal.Domain;

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
	/// Возвращает сделки «Входящих» в хронологическом порядке: сырые записи
	/// исполнения материализуются детерминированной проекцией синхронизации,
	/// затем из списка уходят сделки с действующей привязкой к конструкции.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Непривязанные сделки с атрибутами биржевой записи.</returns>
	/// <exception cref="TradeMaterializationException">Сырая запись повреждена или конфликтует с другой записью того же execId.</exception>
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
		var boundExecIds = (await db.TradeUserdata
				.Where(userdata => userdata.ConstructionId != null)
				.Select(userdata => userdata.ExecId)
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false))
			.ToHashSet(StringComparer.Ordinal);

		// «Входящие» — проекция сырых записей материализатора синхронизации:
		// атрибуты сделки берутся из биржевой записи, а не из пользовательских данных.
		// Traceability: openspec:sync/bybit-history#requirement-new-records-land-in-inbox
		var materializer = new TradeMaterializer(new InstrumentResolver(new InstrumentCatalog(rawInstruments)));
		var inbox = materializer.Materialize(rawExecutions)
			// Привязанная сделка покидает «Входящие», пока привязка не снята возвратом.
			// Traceability: openspec:domain/constructions#scenario-binding-removes-from-inbox
			.Where(trade => boundExecIds.Contains(trade.ExecId) == false)
			.ToList();
		return inbox;
	}
}
