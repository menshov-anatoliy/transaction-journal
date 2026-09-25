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
	/// <summary>Создаёт конструкцию с именем, необязательным капиталом и статусом «открыта».</summary>
	Task<Construction> CreateAsync(
		string name,
		decimal? allocatedCapitalUsdt,
		string? comment = null,
		CancellationToken cancellationToken = default);

	/// <summary>Свободно переименовывает конструкцию, не затрагивая прочие данные.</summary>
	Task RenameAsync(
		long constructionId,
		string newName,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Меняет выделенный капитал — базу процентов результата; null убирает значение.
	/// </summary>
	Task UpdateAllocatedCapitalAsync(
		long constructionId,
		decimal? allocatedCapitalUsdt,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Задаёт или очищает риск конструкции: значение и единица ввода меняются
	/// только парой — оба заданы или оба отсутствуют.
	/// </summary>
	Task UpdateRiskAsync(
		long constructionId,
		decimal? value,
		TargetUnit? unit,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Задаёт или очищает профит конструкции: значение и единица ввода меняются
	/// только парой — оба заданы или оба отсутствуют.
	/// </summary>
	Task UpdateProfitAsync(
		long constructionId,
		decimal? value,
		TargetUnit? unit,
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
/// Use-case сервис управления конструкциями: создание с именем и необязательным
/// выделенным капиталом, свободное переименование, правка капитала и плановых
/// границ результата (риск, профит), ручная смена статуса с архивацией,
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
	/// Капитал необязателен: без него конструкция создаётся без бюджета, и
	/// процентные величины результата не вычисляются. Новая конструкция
	/// открывается с ручным статусом «открыта» и появляется в активном списке.
	/// </summary>
	/// <param name="name">Имя конструкции.</param>
	/// <param name="allocatedCapitalUsdt">Выделенный капитал в USDT; null — капитал не задан.</param>
	/// <param name="comment">Свободный комментарий конструкции; может отсутствовать.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Созданная конструкция.</returns>
	/// <exception cref="ArgumentException">Имя не задано или состоит из пробелов.</exception>
	public async Task<Construction> CreateAsync(
		string name,
		decimal? allocatedCapitalUsdt,
		string? comment = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);

		// Новая конструкция открывается со статусом «открыта»: стартовое значение
		// фиксировано, автоматических статусов нет.
		// Traceability: openspec:domain/constructions#scenario-new-construction-default-open
		// Капитал необязателен: конструкция создаётся и без выделенного бюджета.
		// Traceability: openspec:domain/constructions#scenario-construction-created-without-capital
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
	/// Меняет выделенный капитал конструкции в USDT; null убирает значение.
	/// Капитал хранится как текущее значение без истории изменений и служит базой
	/// процентов результата: изменение и убирание меняют только процентные
	/// величины, абсолютные результаты и позиции не затрагиваются.
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="allocatedCapitalUsdt">Новое значение выделенного капитала в USDT; null убирает капитал.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	public async Task UpdateAllocatedCapitalAsync(
		long constructionId,
		decimal? allocatedCapitalUsdt,
		CancellationToken cancellationToken = default)
	{
		// Капитал — текущее значение без истории: правка заменяет число или убирает
		// его, а проценты результата пересчитываются ближайшим чтением аналитики;
		// абсолютные величины и позиции от капитала не зависят.
		// Traceability: openspec:domain/constructions#requirement-allocated-capital
		// Капитал можно убрать у существующей конструкции: null стирает значение.
		// Traceability: openspec:domain/constructions#scenario-capital-removable
		using var db = CreateContext();
		var construction = await FindConstructionAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		construction.AllocatedCapitalUsdt = allocatedCapitalUsdt;
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region Риск и профит

	/// <summary>
	/// Задаёт или очищает риск конструкции. Риск вводится положительным числом
	/// в одной единице — процентах или USDT, и введённая единица хранится
	/// первоисточником; правка заменяет и значение, и единицу. Значение и
	/// единица меняются только парой: оба заданы — установить, оба null —
	/// очистить, неполная пара отклоняется.
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="value">Значение риска; null вместе с null единицы убирает параметр.</param>
	/// <param name="unit">Единица ввода риска; null вместе с null значения убирает параметр.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ArgumentException">Задано ровно одно из двух: значение или единица; значение не положительное.</exception>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	public async Task UpdateRiskAsync(
		long constructionId,
		decimal? value,
		TargetUnit? unit,
		CancellationToken cancellationToken = default)
	{
		// Риск задаётся значением ровно в одной единице, и введённая единица
		// хранится первоисточником; правка целиком заменяет пару.
		// Traceability: openspec:domain/constructions#requirement-risk-profit-params
		ValidateTargetPair(value, unit);
		using var db = CreateContext();
		var construction = await FindConstructionAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		construction.RiskValue = value;
		construction.RiskUnit = unit;
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Задаёт или очищает профит конструкции. Профит вводится положительным
	/// числом в одной единице — процентах или USDT, и введённая единица хранится
	/// первоисточником; правка заменяет и значение, и единицу. Значение и
	/// единица меняются только парой: оба заданы — установить, оба null —
	/// очистить, неполная пара отклоняется.
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="value">Значение профита; null вместе с null единицы убирает параметр.</param>
	/// <param name="unit">Единица ввода профита; null вместе с null значения убирает параметр.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ArgumentException">Задано ровно одно из двух: значение или единица; значение не положительное.</exception>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	public async Task UpdateProfitAsync(
		long constructionId,
		decimal? value,
		TargetUnit? unit,
		CancellationToken cancellationToken = default)
	{
		// Профит задаётся значением ровно в одной единице, и введённая единица
		// хранится первоисточником; правка целиком заменяет пару.
		// Traceability: openspec:domain/constructions#requirement-risk-profit-params
		ValidateTargetPair(value, unit);
		using var db = CreateContext();
		var construction = await FindConstructionAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		construction.ProfitValue = value;
		construction.ProfitUnit = unit;
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Проверяет пару «значение + единица» риск/профит: пара либо задана целиком,
	/// либо оба поля пусты; заданное значение обязано быть положительным числом.
	/// </summary>
	/// <param name="value">Значение параметра.</param>
	/// <param name="unit">Единица ввода параметра.</param>
	/// <exception cref="ArgumentException">Задано ровно одно из двух или значение не положительное.</exception>
	private static void ValidateTargetPair(decimal? value, TargetUnit? unit)
	{
		// Неполная пара оставила бы параметр в невалидном состоянии: значение
		// без единицы или единица без значения не допускаются к хранению.
		// Traceability: openspec:domain/constructions#requirement-risk-profit-params
		if (value.HasValue != unit.HasValue)
		{
			throw new ArgumentException(
				"Параметр задаётся парой «значение и единица ввода»: оба поля должны быть заданы или оба пусты.");
		}

		// Риск — допустимый убыток, профит — целевая прибыль: оба параметра
		// выражаются положительным числом, знак подсказка добавляет сама.
		// Traceability: openspec:domain/constructions#requirement-risk-profit-params
		if (value is { } positive && positive <= 0m)
		{
			throw new ArgumentException("Значение параметра должно быть положительным числом.");
		}
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
