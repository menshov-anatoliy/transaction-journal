using Microsoft.EntityFrameworkCore;
using TransactionJournal.Data;

namespace TransactionJournal.Domain;

/// <summary>
/// Use-case сервис привязки сделок: привязка из «Входящих», перенос между
/// конструкциями, возврат во «Входящие» и массовая привязка. Привязка хранится
/// в пользовательских данных сделки по ключу execId, поэтому сделка находится
/// ровно в одной конструкции либо во «Входящих»; позиции и результаты —
/// производные и пересчитываются при чтении, никаких синхронных правок
/// производных привязка не требует. Каждый вызов создаёт собственный
/// короткоживущий контекст, поэтому сервис безопасен в длительных сессиях
/// Blazor Server.
// Traceability: openspec:domain/constructions#requirement-trade-single-binding
/// Traceability: change:add-core-domain/design#d5
/// </summary>
public sealed class TradeBindingService
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
	/// целевой конструкции; комментарий сделки сохраняется.
	/// </summary>
	/// <param name="constructionId">Идентификатор целевой конструкции.</param>
	/// <param name="execId">Биржевой идентификатор исполнения сделки.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ArgumentException">Идентификатор исполнения не задан или состоит из пробелов.</exception>
	/// <exception cref="ConstructionNotFoundException">Целевая конструкция не найдена.</exception>
	/// <exception cref="TradeNotFoundException">Сделки с таким execId нет в журнале.</exception>
	public async Task BindAsync(
		long constructionId,
		string execId,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(execId);

		// Привязка и перенос — одна операция: строка пользовательских данных по execId
		// получает целевую конструкцию, поэтому у сделки не может появиться второй
		// принадлежности. Повторная привязка после закрытия позиций доступна так же,
		// как и первая: производные пересчитаются при чтении.
		// Traceability: openspec:domain/constructions#scenario-binding-removes-from-inbox
		// Traceability: openspec:domain/constructions#scenario-rebind-recomputes-both
		using var db = CreateContext();
		await EnsureConstructionExistsAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
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
	/// не меняя существующих привязок.
	/// </summary>
	/// <param name="constructionId">Идентификатор целевой конструкции.</param>
	/// <param name="execIds">Идентификаторы исполнения привязываемых сделок.</param>
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
		// отказ на любой сделке откатывает всю пачку целиком.
		// Traceability: openspec:domain/constructions#scenario-batch-binding
		using var db = CreateContext();
		await EnsureConstructionExistsAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		foreach (var id in ids)
		{
			await EnsureTradeExistsAsync(db, id, cancellationToken).ConfigureAwait(false);
		}

		var existing = await db.TradeUserdata
			.Where(userdata => ids.Contains(userdata.ExecId))
			.ToDictionaryAsync(userdata => userdata.ExecId, StringComparer.Ordinal, cancellationToken)
			.ConfigureAwait(false);
		foreach (var id in ids)
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
	/// сделки при чтении.
	/// </summary>
	/// <param name="execId">Биржевой идентификатор исполнения сделки.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ArgumentException">Идентификатор исполнения не задан или состоит из пробелов.</exception>
	/// <exception cref="TradeNotFoundException">Сделки с таким execId нет в журнале.</exception>
	public async Task UnbindAsync(string execId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(execId);

		// Возврат во «Входящие» — обнуление привязки, а не удаление пользовательских
		// данных: комментарий сделки и строка по execId переживают перепривязки.
		// Traceability: openspec:domain/constructions#scenario-return-to-inbox
		using var db = CreateContext();
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

	/// <summary>Проверяет существование сделки в сыром хранилище: привязывать можно только реальные сделки журнала.</summary>
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

	/// <summary>Загружает строку пользовательских данных сделки по execId или null, если сделки во «Входящих» записи ещё нет.</summary>
	private static Task<TradeUserdata?> LoadUserdataAsync(
		JournalDbContext db,
		string execId,
		CancellationToken cancellationToken) =>
		db.TradeUserdata
			.FirstOrDefaultAsync(userdata => userdata.ExecId == execId, cancellationToken);

	#endregion
}
