using Microsoft.EntityFrameworkCore;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Application;
using TransactionJournal.Domain;

namespace TransactionJournal.Infrastructure.UseCases;

/// <summary>
/// Use-case сервис привязки сделок: привязка из «Входящих», перенос между
/// конструкциями, возврат во «Входящие» и массовая привязка. Привязка биржевой
/// сделки хранится в пользовательских данных по ключу execId, ручной —
/// в её сырье, поэтому сделка любого источника находится ровно в одной
/// конструкции либо во «Входящих»; команды адресуются единым ключом сделки
/// (execId либо «manual:{id}») и обслуживают оба источника без отдельных
/// сценариев. Позиции и результаты — производные и пересчитываются при чтении,
/// никаких синхронных правок производных привязка не требует. Каждый вызов
/// создаёт собственный короткоживущий контекст, поэтому сервис безопасен
/// в длительных сессиях Blazor Server.
// Traceability: openspec:domain/constructions#requirement-trade-single-binding
// Traceability: openspec:domain/manual-trades#scenario-manual-trade-created-into-inbox
// Traceability: change:manual-journal-management/design#d4
/// </summary>
public sealed class TradeBindingService : ITradeBindingService
{
	private readonly DbContextOptions<JournalDbContext> _options;

	/// <summary>Создаёт сервис над опциями контекста журнала; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <exception cref="ArgumentNullException">Опции не заданы.</exception>
	public TradeBindingService(DbContextOptions<JournalDbContext> options)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
	}

	#region Привязка и перенос

	/// <summary>
	/// Привязывает сделку к конструкции — из «Входящих» или переносом из другой
	/// конструкции: операция одна и та же, целевая привязка заменяет прежнюю.
	/// Сделка исчезает из «Входящих» и начинает участвовать в позициях и результате
	/// целевой конструкции; комментарий сделки сохраняется. Сделка адресуется
	/// единым ключом: execId биржевой записи либо «manual:{id}» строки ручного
	/// сырья — привязка ручной сделки меняет её строку сырья.
	/// </summary>
	/// <param name="constructionId">Идентификатор целевой конструкции.</param>
	/// <param name="execId">Единый ключ сделки: биржевой execId либо «manual:{id}».</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ArgumentException">Ключ сделки не задан или состоит из пробелов.</exception>
	/// <exception cref="ConstructionNotFoundException">Целевая конструкция не найдена.</exception>
	/// <exception cref="TradeNotFoundException">Сделки с таким ключом нет в журнале.</exception>
	public async Task BindAsync(
		long constructionId,
		string execId,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(execId);

		// Привязка и перенос — одна операция: у биржевой сделки строка пользовательских
		// данных по execId получает целевую конструкцию, у ручной — её строка сырья;
		// второй принадлежности у сделки любого источника не появляется. Повторная
		// привязка после закрытия позиций доступна так же, как и первая: производные
		// пересчитаются при чтении.
		// Traceability: openspec:domain/constructions#scenario-binding-removes-from-inbox
		// Traceability: openspec:domain/constructions#scenario-rebind-recomputes-both
		// Traceability: change:manual-journal-management/design#d4
		using var db = CreateContext();
		await EnsureConstructionExistsAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		if (TryParseManualTradeId(execId, out var manualTradeId))
		{
			var manualTrade = await FindManualTradeAsync(db, manualTradeId, cancellationToken).ConfigureAwait(false);
			manualTrade.ConstructionId = constructionId;
			await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
			return;
		}

		await EnsureTradeExistsAsync(db, execId, cancellationToken).ConfigureAwait(false);
		var userdata = await LoadUserdataAsync(db, execId, cancellationToken).ConfigureAwait(false);
		if (userdata == null)
		{
			db.TradeUserdata.Add(new TradeUserdata { ExecId = execId, ConstructionId = constructionId });
		}
		else
		{
			userdata.ConstructionId = constructionId;
		}

		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Привязывает несколько сделок к одной конструкции одним действием. Привязка
	/// атомарна: любая неизвестная сделка или конструкция отказывает всю пачку,
	/// не меняя существующих привязок. Каждая сделка пачки адресуется единым
	/// ключом и обслуживается своим источником — биржевым или ручным.
	/// </summary>
	/// <param name="constructionId">Идентификатор целевой конструкции.</param>
	/// <param name="execIds">Единые ключи привязываемых сделок: execId либо «manual:{id}».</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ArgumentNullException">Список идентификаторов не задан.</exception>
	/// <exception cref="ArgumentException">Какой-либо идентификатор не задан или состоит из пробелов.</exception>
	/// <exception cref="ConstructionNotFoundException">Целевая конструкция не найдена.</exception>
	/// <exception cref="TradeNotFoundException">Какой-либо сделки нет в журнале.</exception>
	public async Task BindBatchAsync(
		long constructionId,
		IEnumerable<string> execIds,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(execIds);
		var ids = execIds.ToList();
		foreach (var id in ids)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(id);
		}

		// Массовая привязка сохраняет тот же инвариант, что и одиночная: каждая
		// сделка пачки получает ровно одну целевую конструкцию одним сохранением,
		// отказ на любой сделке откатывает всю пачку целиком. Ручные сделки пачки
		// привязываются в общем сохранении наравне с биржевыми.
		// Traceability: openspec:domain/constructions#scenario-batch-binding
		// Traceability: change:manual-journal-management/design#d4
		using var db = CreateContext();
		await EnsureConstructionExistsAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		var manualTradeIds = new HashSet<long>();
		var exchangeExecIds = new List<string>();
		foreach (var id in ids)
		{
			if (TryParseManualTradeId(id, out var manualTradeId))
			{
				manualTradeIds.Add(manualTradeId);
			}
			else
			{
				await EnsureTradeExistsAsync(db, id, cancellationToken).ConfigureAwait(false);
				exchangeExecIds.Add(id);
			}
		}

		var manualTrades = await db.ManualTrades
			.Where(trade => manualTradeIds.Contains(trade.Id))
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		if (manualTrades.Count != manualTradeIds.Count)
		{
			var found = manualTrades.Select(trade => trade.Id).ToHashSet();
			var missing = manualTradeIds.First(id => found.Contains(id) == false);
			throw new TradeNotFoundException(SyntheticCloseManualLinkMaterial.BuildManualKey(missing));
		}

		var existing = await db.TradeUserdata
			.Where(userdata => exchangeExecIds.Contains(userdata.ExecId))
			.ToDictionaryAsync(userdata => userdata.ExecId, StringComparer.Ordinal, cancellationToken)
			.ConfigureAwait(false);
		foreach (var trade in manualTrades)
		{
			trade.ConstructionId = constructionId;
		}

		foreach (var id in exchangeExecIds)
		{
			if (existing.TryGetValue(id, out var userdata))
			{
				userdata.ConstructionId = constructionId;
			}
			else
			{
				db.TradeUserdata.Add(new TradeUserdata { ExecId = id, ConstructionId = constructionId });
			}
		}

		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region Возврат во «Входящие»

	/// <summary>
	/// Возвращает привязанную сделку во «Входящие»: привязка снимается, комментарий
	/// сделки сохраняется, позиции прежней конструкции пересчитываются без этой
	/// сделки при чтении. Сделка адресуется единым ключом: execId биржевой записи
	/// либо «manual:{id}» строки ручного сырья.
	/// </summary>
	/// <param name="execId">Единый ключ сделки: биржевой execId либо «manual:{id}».</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ArgumentException">Ключ сделки не задан или состоит из пробелов.</exception>
	/// <exception cref="TradeNotFoundException">Сделки с таким ключом нет в журнале.</exception>
	public async Task UnbindAsync(string execId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(execId);

		// Возврат во «Входящие» — обнуление привязки, а не удаление записи: у биржевой
		// сделки обнуляется строка пользовательских данных, у ручной — её строка
		// сырья; комментарий сделки переживает перепривязки у обоих источников.
		// Traceability: openspec:domain/constructions#scenario-return-to-inbox
		// Traceability: change:manual-journal-management/design#d4
		using var db = CreateContext();
		if (TryParseManualTradeId(execId, out var manualTradeId))
		{
			var manualTrade = await FindManualTradeAsync(db, manualTradeId, cancellationToken).ConfigureAwait(false);
			manualTrade.ConstructionId = null;
			await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
			return;
		}

		await EnsureTradeExistsAsync(db, execId, cancellationToken).ConfigureAwait(false);
		var userdata = await LoadUserdataAsync(db, execId, cancellationToken).ConfigureAwait(false);
		if (userdata != null && userdata.ConstructionId != null)
		{
			userdata.ConstructionId = null;
			await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	#endregion

	#region Помощники

	private JournalDbContext CreateContext() => new(_options);

	/// <summary>Разбирает единый ключ сделки; true — ключ адресует строку ручного сырья.</summary>
	private static bool TryParseManualTradeId(string execId, out long manualTradeId) =>
		SyntheticCloseManualLinkMaterial.TryParseManualKey(execId, out manualTradeId);

	/// <summary>Проверяет существование целевой конструкции до изменения привязок.</summary>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	private static async Task EnsureConstructionExistsAsync(
		JournalDbContext db,
		long constructionId,
		CancellationToken cancellationToken)
	{
		var exists = await db.Constructions
			.AnyAsync(construction => construction.Id == constructionId, cancellationToken)
			.ConfigureAwait(false);
		if (exists == false)
		{
			throw new ConstructionNotFoundException(constructionId);
		}
	}

	/// <summary>Проверяет существование биржевой сделки в сыром хранилище: привязывать можно только реальные сделки журнала.</summary>
	/// <exception cref="TradeNotFoundException">Сделка не найдена.</exception>
	private static async Task EnsureTradeExistsAsync(
		JournalDbContext db,
		string execId,
		CancellationToken cancellationToken)
	{
		var exists = await db.RawExecutions
			.AnyAsync(execution => execution.ExecId == execId, cancellationToken)
			.ConfigureAwait(false);
		if (exists == false)
		{
			throw new TradeNotFoundException(execId);
		}
	}

	/// <summary>Загружает ручную сделку по идентификатору строки сырья или отказывает: сделка любого источника должна существовать.</summary>
	/// <exception cref="TradeNotFoundException">Ручной сделки с таким идентификатором нет.</exception>
	private static async Task<ManualTrade> FindManualTradeAsync(
		JournalDbContext db,
		long manualTradeId,
		CancellationToken cancellationToken)
	{
		return await db.ManualTrades
			.FirstOrDefaultAsync(trade => trade.Id == manualTradeId, cancellationToken)
			.ConfigureAwait(false)
			?? throw new TradeNotFoundException(SyntheticCloseManualLinkMaterial.BuildManualKey(manualTradeId));
	}

	/// <summary>Загружает строку пользовательских данных сделки по execId или null, если сделки во «Входящих» записи ещё нет.</summary>
	private static Task<TradeUserdata?> LoadUserdataAsync(
		JournalDbContext db,
		string execId,
		CancellationToken cancellationToken) =>
		db.TradeUserdata
			.FirstOrDefaultAsync(userdata => userdata.ExecId == execId, cancellationToken);

	#endregion
}

