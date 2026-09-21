using Microsoft.EntityFrameworkCore;
using TransactionJournal.Data;

namespace TransactionJournal.Domain;

/// <summary>
/// Контракт use-case сервиса внешних корректировок PnL для тонких слоёв UI:
/// экран деталей добавляет корректировку формой и правит либо удаляет её
/// строкой таблицы, не завися от конкретного сервиса и хранилища.
/// </summary>
// Корректировки PnL живут в деталях конструкции — мутации идут через
// доменный контракт, отдельных экранов не появляется.
// Traceability: openspec:ui/screens#requirement-adjustments-in-detail
public interface IPnLAdjustmentService
{
	/// <summary>Добавляет внешнюю корректировку PnL к конструкции со знаковой суммой в USDT и комментарием.</summary>
	Task<PnLAdjustment> AddAsync(
		long constructionId,
		DateTimeOffset date,
		PnLAdjustmentSource source,
		decimal amountUsdt,
		string? comment = null,
		CancellationToken cancellationToken = default);

	/// <summary>Свободно правит все атрибуты внешней корректировки PnL: дату, источник, сумму и комментарий.</summary>
	Task EditAsync(
		long adjustmentId,
		DateTimeOffset date,
		PnLAdjustmentSource source,
		decimal amountUsdt,
		string? comment,
		CancellationToken cancellationToken = default);

	/// <summary>Удаляет внешнюю корректировку PnL — результат пересчитывается ближайшим чтением.</summary>
	Task DeleteAsync(long adjustmentId, CancellationToken cancellationToken = default);

	/// <summary>Возвращает внешние корректировки PnL конструкции в хронологическом порядке.</summary>
	Task<IReadOnlyList<PnLAdjustment>> ListAsync(
		long constructionId,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// Use-case сервис внешних корректировок PnL — слагаемых результата конструкции
/// без сделки: PnL торгового робота и ручные поправки. Корректировка добавляется
/// с датой, источником, знаковой суммой в USDT и комментарием, свободно правится
/// и удаляется вместе с любыми атрибутами; результат пересчитывается при чтении
/// с новым набором корректировок, журнал аудита изменений в MVP не ведётся.
/// Каждый вызов создаёт собственный короткоживущий контекст, поэтому сервис
/// безопасен в длительных сессиях Blazor Server.
// Traceability: openspec:domain/constructions#requirement-external-pnl-adjustments
// Traceability: change:add-core-domain/design#d5
/// </summary>
public sealed class PnLAdjustmentService : IPnLAdjustmentService
{
	private readonly DbContextOptions<JournalDbContext> _options;

	/// <summary>Создаёт сервис над опциями контекста журнала; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <exception cref="ArgumentNullException">Опции не заданы.</exception>
	public PnLAdjustmentService(DbContextOptions<JournalDbContext> options)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
	}

	#region Добавление

	/// <summary>
	/// Добавляет внешнюю корректировку PnL к конструкции: слагаемое результата без
	/// сделки с датой, источником «робот»/«ручная», знаковой суммой в USDT
	/// и комментарием.
	/// </summary>
	/// <param name="constructionId">Конструкция, в результат которой входит корректировка.</param>
	/// <param name="date">Дата корректировки.</param>
	/// <param name="source">Источник корректировки: «робот» или «ручная».</param>
	/// <param name="amountUsdt">Знаковая сумма в USDT: положительная увеличивает результат, отрицательная — уменьшает.</param>
	/// <param name="comment">Свободный комментарий; может отсутствовать.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Созданная корректировка.</returns>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	public async Task<PnLAdjustment> AddAsync(
		long constructionId,
		DateTimeOffset date,
		PnLAdjustmentSource source,
		decimal amountUsdt,
		string? comment = null,
		CancellationToken cancellationToken = default)
	{
		// Корректировка — самостоятельное слагаемое результата: никаких проверок
		// знака и содержания, пользователь волен вносить любое значение.
		// Traceability: openspec:domain/constructions#scenario-robot-adjustment-in-result
		using var db = CreateContext();
		await EnsureConstructionExistsAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		var adjustment = new PnLAdjustment
		{
			ConstructionId = constructionId,
			Date = date,
			Source = source,
			AmountUsdt = amountUsdt,
			Comment = comment,
		};
		db.PnLAdjustments.Add(adjustment);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return adjustment;
	}

	#endregion

	#region Правка

	/// <summary>
	/// Свободно правит все атрибуты внешней корректировки PnL: дату, источник,
	/// знаковую сумму и комментарий. Ограничений нет, результат пересчитывается
	/// ближайшим чтением с новым набором атрибутов.
	/// </summary>
	/// <param name="adjustmentId">Идентификатор корректировки.</param>
	/// <param name="date">Новая дата корректировки.</param>
	/// <param name="source">Новый источник корректировки.</param>
	/// <param name="amountUsdt">Новая знаковая сумма в USDT.</param>
	/// <param name="comment">Новый комментарий; null — убрать комментарий.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="PnLAdjustmentNotFoundException">Корректировка не найдена.</exception>
	public async Task EditAsync(
		long adjustmentId,
		DateTimeOffset date,
		PnLAdjustmentSource source,
		decimal amountUsdt,
		string? comment,
		CancellationToken cancellationToken = default)
	{
		// Правка заменяет атрибуты целиком и не ведёт журнал изменений — осознанное
		// ограничение MVP: производные величины пересчитаются при чтении.
		// Traceability: openspec:domain/constructions#scenario-adjustment-edit-delete-free
		using var db = CreateContext();
		var adjustment = await FindAdjustmentAsync(db, adjustmentId, cancellationToken).ConfigureAwait(false);
		adjustment.Date = date;
		adjustment.Source = source;
		adjustment.AmountUsdt = amountUsdt;
		adjustment.Comment = comment;
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region Удаление

	/// <summary>
	/// Удаляет внешнюю корректировку PnL. Удаление свободно и без ограничений:
	/// корректировка просто перестаёт быть слагаемым результата ближайшим чтением.
	/// </summary>
	/// <param name="adjustmentId">Идентификатор корректировки.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="PnLAdjustmentNotFoundException">Корректировка не найдена.</exception>
	public async Task DeleteAsync(long adjustmentId, CancellationToken cancellationToken = default)
	{
		// Удаление свободно: новых записей не требуется, результат пересчитается
		// без удалённого слагаемого ближайшим чтением.
		// Traceability: openspec:domain/constructions#scenario-adjustment-edit-delete-free
		using var db = CreateContext();
		var adjustment = await FindAdjustmentAsync(db, adjustmentId, cancellationToken).ConfigureAwait(false);
		db.PnLAdjustments.Remove(adjustment);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region Чтение

	/// <summary>
	/// Возвращает внешние корректировки PnL конструкции с полным набором атрибутов:
	/// дата, источник, знаковая сумма и комментарий.
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Корректировки конструкции в хронологическом порядке.</returns>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	public async Task<IReadOnlyList<PnLAdjustment>> ListAsync(
		long constructionId,
		CancellationToken cancellationToken = default)
	{
		// Читающий слой возвращает все четыре атрибута каждой корректировки:
		// результат конструкции собирается из них как из полноценных слагаемых.
		// Traceability: openspec:domain/constructions#scenario-adjustment-attributes-stored
		using var db = CreateContext();
		await EnsureConstructionExistsAsync(db, constructionId, cancellationToken).ConfigureAwait(false);

		// SQLite не переводит DateTimeOffset в ORDER BY — сортировка по дате
		// выполняется на клиенте после чтения строк.
		var adjustments = await db.PnLAdjustments
			.AsNoTracking()
			.Where(adjustment => adjustment.ConstructionId == constructionId)
			.OrderBy(adjustment => adjustment.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return adjustments
			.OrderBy(adjustment => adjustment.Date)
			.ThenBy(adjustment => adjustment.Id)
			.ToList();
	}

	#endregion

	#region Помощники

	private JournalDbContext CreateContext() => new(_options);

	/// <summary>Проверяет существование конструкции до изменения корректировок.</summary>
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

	/// <summary>Загружает корректировку по идентификатору.</summary>
	/// <exception cref="PnLAdjustmentNotFoundException">Корректировка не найдена.</exception>
	private static async Task<PnLAdjustment> FindAdjustmentAsync(
		JournalDbContext db,
		long adjustmentId,
		CancellationToken cancellationToken)
	{
		var adjustment = await db.PnLAdjustments
			.FirstOrDefaultAsync(candidate => candidate.Id == adjustmentId, cancellationToken)
			.ConfigureAwait(false);
		return adjustment ?? throw new PnLAdjustmentNotFoundException(adjustmentId);
	}

	#endregion
}
