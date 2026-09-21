using Microsoft.EntityFrameworkCore;
using TransactionJournal.Data;

namespace TransactionJournal.Domain;

/// <summary>
/// Контракт use-case сервиса управления конструкциями для тонких слоёв UI:
/// экран деталей выполняет действия конструкции через этот интерфейс,
/// не завися от конкретного сервиса и хранилища.
/// </summary>
// UI — тонкий слой над готовыми контрактами: мутации конструкции идут
// через доменный use-case сервис, экранные тесты подменяют его заглушкой.
// Traceability: openspec:ui/screens#requirement-construction-actions
public interface IConstructionService
{
	/// <summary>Создаёт конструкцию с именем, капиталом и статусом «открыта».</summary>
	Task<Construction> CreateAsync(
		string name,
		decimal allocatedCapitalUsdt,
		string? comment = null,
		CancellationToken cancellationToken = default);

	/// <summary>Свободно переименовывает конструкцию, не затрагивая прочие данные.</summary>
	Task RenameAsync(
		long constructionId,
		string newName,
		CancellationToken cancellationToken = default);

	/// <summary>Меняет выделенный капитал — базу процентов результата.</summary>
	Task UpdateAllocatedCapitalAsync(
		long constructionId,
		decimal allocatedCapitalUsdt,
		CancellationToken cancellationToken = default);

	/// <summary>Свободно меняет ручной статус конструкции, включая архив.</summary>
	Task ChangeStatusAsync(
		long constructionId,
		ConstructionStatus status,
		CancellationToken cancellationToken = default);

	/// <summary>Переводит конструкцию в статус «архив», скрывая её из активных списков.</summary>
	Task ArchiveAsync(long constructionId, CancellationToken cancellationToken = default);

	/// <summary>Удаляет только пустую конструкцию; непустая отказывает с причиной.</summary>
	Task DeleteAsync(long constructionId, CancellationToken cancellationToken = default);

	/// <summary>Возвращает активный список конструкций без архивных.</summary>
	Task<IReadOnlyList<Construction>> ListActiveAsync(CancellationToken cancellationToken = default);

	/// <summary>Возвращает полный список конструкций для аналитики, включая архивные.</summary>
	Task<IReadOnlyList<Construction>> ListAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Use-case сервис управления конструкциями: создание с именем и выделенным
/// капиталом, свободное переименование, ручная смена статуса с архивацией,
/// чтение активных и полных списков и удаление только пустых конструкций.
/// Позиции и результаты — производные и через этот сервис не меняются.
/// Каждый вызов создаёт собственный короткоживущий контекст, поэтому сервис
/// безопасен в длительных сессиях Blazor Server.
// Traceability: openspec:domain/constructions#requirement-construction-management
/// Traceability: change:add-core-domain/design#d5
/// </summary>
public sealed class ConstructionService : IConstructionService
{
	private readonly DbContextOptions<JournalDbContext> _options;

	/// <summary>Создаёт сервис над опциями контекста журнала; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <exception cref="ArgumentNullException">Опции не заданы.</exception>
	public ConstructionService(DbContextOptions<JournalDbContext> options)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
	}

	#region Создание

	/// <summary>
	/// Создаёт конструкцию с именем, выделенным капиталом и комментарием.
	/// Новая конструкция открывается с ручным статусом «открыта» и появляется
	/// в активном списке.
	/// </summary>
	/// <param name="name">Имя конструкции.</param>
	/// <param name="allocatedCapitalUsdt">Выделенный капитал в USDT.</param>
	/// <param name="comment">Свободный комментарий конструкции; может отсутствовать.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Созданная конструкция.</returns>
	/// <exception cref="ArgumentException">Имя не задано или состоит из пробелов.</exception>
	public async Task<Construction> CreateAsync(
		string name,
		decimal allocatedCapitalUsdt,
		string? comment = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		// Новая конструкция открывается со статусом «открыта»: стартовое значение
		// фиксировано, автоматических статусов нет.
		// Traceability: openspec:domain/constructions#scenario-new-construction-default-open
		using var db = CreateContext();
		var construction = new Construction
		{
			Name = name,
			Status = ConstructionStatus.Open,
			AllocatedCapitalUsdt = allocatedCapitalUsdt,
			Comment = comment,
		};
		db.Constructions.Add(construction);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return construction;
	}

	#endregion

	#region Переименование

	/// <summary>
	/// Переименовывает конструкцию. Переименование свободно и затрагивает только
	/// имя: привязки, капитал, статус, комментарий и производные величины
	/// не изменяются.
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="newName">Новое имя конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ArgumentException">Новое имя не задано или состоит из пробелов.</exception>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	public async Task RenameAsync(
		long constructionId,
		string newName,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(newName);

		// Переименование меняет только имя: загруженная сущность модифицируется
		// исключительно по столбцу Name, привязки и прочие атрибуты не затрагиваются.
		// Traceability: openspec:domain/constructions#scenario-rename-preserves-everything
		using var db = CreateContext();
		var construction = await FindConstructionAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		construction.Name = newName;
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region Выделенный капитал

	/// <summary>
	/// Меняет выделенный капитал конструкции в USDT. Капитал хранится как текущее
	/// значение без истории изменений и служит базой процентов результата:
	/// изменение меняет только процентные величины, абсолютные результаты и позиции
	/// не затрагиваются.
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="allocatedCapitalUsdt">Новое значение выделенного капитала в USDT.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	public async Task UpdateAllocatedCapitalAsync(
		long constructionId,
		decimal allocatedCapitalUsdt,
		CancellationToken cancellationToken = default)
	{
		// Капитал — текущее значение без истории: правка заменяет число, а проценты
		// результата пересчитываются от нового значения ближайшим чтением аналитики;
		// абсолютные величины и позиции от капитала не зависят.
		// Traceability: openspec:domain/constructions#requirement-allocated-capital
		using var db = CreateContext();
		var construction = await FindConstructionAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		construction.AllocatedCapitalUsdt = allocatedCapitalUsdt;
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region Статус и архивация

	/// <summary>
	/// Меняет ручной статус конструкции на любое значение: смена свободна,
	/// ограничений на переходы между статусами нет.
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="status">Целевой статус конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	public async Task ChangeStatusAsync(
		long constructionId,
		ConstructionStatus status,
		CancellationToken cancellationToken = default)
	{
		// Статус — ручной и меняется свободно в любой момент: автоматических
		// переходов и запрещённых направлений нет.
		// Traceability: change:add-core-domain/design#d4
		using var db = CreateContext();
		var construction = await FindConstructionAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		construction.Status = status;
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Переводит конструкцию в статус «архив»: конструкция скрывается из активных
	/// списков, но остаётся доступной в аналитике вместе со своей историей.
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	public Task ArchiveAsync(long constructionId, CancellationToken cancellationToken = default) =>
		ChangeStatusAsync(constructionId, ConstructionStatus.Archived, cancellationToken);

	#endregion

	#region Удаление

	/// <summary>
	/// Удаляет конструкцию. Удаление возможно только для конструкций без привязанных
	/// сделок и внешних корректировок PnL; при нарушении отказывает с объяснением
	/// причины, не меняя данные конструкции.
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	/// <exception cref="ConstructionDeletionRefusedException">У конструкции есть сделки или корректировки.</exception>
	public async Task DeleteAsync(long constructionId, CancellationToken cancellationToken = default)
	{
		// Удаление допустимо только для пустой конструкции: блокирующие записи
		// считаются до попытки удаления, чтобы отказ объяснил причину и не тронул данные.
		// Traceability: openspec:domain/constructions#scenario-delete-only-when-empty
		using var db = CreateContext();
		var construction = await FindConstructionAsync(db, constructionId, cancellationToken).ConfigureAwait(false);

		var boundTradeCount = await db.TradeUserdata
			.CountAsync(userdata => userdata.ConstructionId == constructionId, cancellationToken)
			.ConfigureAwait(false);
		var adjustmentCount = await db.PnLAdjustments
			.CountAsync(adjustment => adjustment.ConstructionId == constructionId, cancellationToken)
			.ConfigureAwait(false);
		if (boundTradeCount > 0 || adjustmentCount > 0)
		{
			throw new ConstructionDeletionRefusedException(boundTradeCount, adjustmentCount);
		}

		db.Constructions.Remove(construction);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region Чтение списков

	/// <summary>
	/// Возвращает активный список конструкций: все, кроме архивных.
	/// Архивация скрывает конструкцию из операционного интерфейса, не удаляя её.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Конструкции вне архива в порядке создания.</returns>
	public async Task<IReadOnlyList<Construction>> ListActiveAsync(CancellationToken cancellationToken = default)
	{
		// Активные списки не показывают архивные конструкции.
		// Traceability: openspec:domain/constructions#scenario-archived-hidden-but-analyzed
		using var db = CreateContext();
		var active = await db.Constructions
			.Where(construction => construction.Status != ConstructionStatus.Archived)
			.OrderBy(construction => construction.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return active;
	}

	/// <summary>
	/// Возвращает полный список конструкций для аналитики, включая архивные
	/// вместе с их историей.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Все конструкции в порядке создания.</returns>
	public async Task<IReadOnlyList<Construction>> ListAllAsync(CancellationToken cancellationToken = default)
	{
		// Аналитика видит все конструкции, включая архивные.
		// Traceability: openspec:domain/constructions#scenario-archived-hidden-but-analyzed
		using var db = CreateContext();
		var all = await db.Constructions
			.OrderBy(construction => construction.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return all;
	}

	#endregion

	#region Помощники

	private JournalDbContext CreateContext() => new(_options);

	/// <summary>Загружает конструкцию по идентификатору или отказывает «не найдена».</summary>
	private static async Task<Construction> FindConstructionAsync(
		JournalDbContext db,
		long constructionId,
		CancellationToken cancellationToken)
	{
		var construction = await db.Constructions
			.FirstOrDefaultAsync(entity => entity.Id == constructionId, cancellationToken)
			.ConfigureAwait(false);
		if (construction == null)
		{
			throw new ConstructionNotFoundException(constructionId);
		}

		return construction;
	}

	#endregion
}
