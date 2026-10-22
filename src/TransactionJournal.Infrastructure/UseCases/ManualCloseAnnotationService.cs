using Microsoft.EntityFrameworkCore;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Application;
using TransactionJournal.Domain;

namespace TransactionJournal.Infrastructure.UseCases;

/// <summary>
/// Use-case сервис ручных пометок закрытия — пользовательских закрывающих записей
/// для инструментов без биржевых записей закрытия. Пометка добавляется с ценой
/// пользователя или без неё (производный слой подставляет последнюю известную
/// котировку при чтении) и с количеством или без него (незаданное количество
/// закрывает весь остаток, заданное клэмпится остатком), свободно правится
/// и удаляется — позиции пересчитываются при чтении, поэтому удаление возвращает
/// позицию в открытое состояние без синхронных правок производных. Каждый вызов
/// создаёт собственный короткоживущий контекст, поэтому сервис безопасен
/// в длительных сессиях Blazor Server.
// Traceability: openspec:domain/constructions#requirement-position-close-lifecycle
// Traceability: change:add-core-domain/design#d3
// Traceability: change:manual-journal-management/design#d6
/// </summary>
public sealed class ManualCloseAnnotationService : IManualCloseAnnotationService
{
	private readonly DbContextOptions<JournalDbContext> _options;

	/// <summary>Создаёт сервис над опциями контекста журнала; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <exception cref="ArgumentNullException">Опции не заданы.</exception>
	public ManualCloseAnnotationService(DbContextOptions<JournalDbContext> options)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
	}

	#region Добавление

	/// <summary>
	/// Добавляет ручную пометку закрытия позиции конструкции. Цена необязательна:
	/// незаданная цена разрешается производным слоем последней известной котировкой
	/// инструмента при чтении. Количество необязательно: незаданное закрывает
	/// весь остаток на момент пометки, заданное клэмпится остатком производным
	/// слоем.
	/// </summary>
	/// <param name="constructionId">Конструкция, внутри которой закрывается позиция.</param>
	/// <param name="symbol">Инструмент закрываемой позиции.</param>
	/// <param name="annotatedAt">Время пометки: задаёт место записи в хронологии закрывающих записей.</param>
	/// <param name="price">Цена закрытия по выбору пользователя; null — взять последнюю котировку при чтении.</param>
	/// <param name="quantity">Количество закрываемой позиции; null — весь остаток на момент пометки.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Созданная пометка.</returns>
	/// <exception cref="ArgumentException">Инструмент не задан или состоит из пробелов; количество не положительно.</exception>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	public async Task<ManualCloseAnnotation> AddAsync(
		long constructionId,
		string symbol,
		DateTimeOffset annotatedAt,
		decimal? price = null,
		decimal? quantity = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
		EnsureQuantityIsValid(quantity);

		// Пометка — пользовательская запись домена: остаток не проверяется, избыточная
		// пометка превращается в предупреждение read-модели, а не в запрет.
		// Traceability: change:add-core-domain/design#risks-trade-offs
		using var db = CreateContext();
		await EnsureConstructionExistsAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		var quote = new ManualCloseAnnotation
		{
			ConstructionId = constructionId,
			Symbol = symbol,
			Price = price,
			Quantity = quantity,
			AnnotatedAt = annotatedAt,
		};
		db.ManualCloseAnnotations.Add(quote);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return quote;
	}

	#endregion

	#region Правка

	/// <summary>
	/// Свободно правит атрибуты ручной пометки: инструмент, время, цену и количество.
	/// Перенос в другую конструкцию не выполняется — для этого пометка удаляется
	/// и заводится заново. Новые значения подхватываются ближайшим чтением позиций.
	/// </summary>
	/// <param name="annotationId">Идентификатор пометки.</param>
	/// <param name="symbol">Новый инструмент помечки.</param>
	/// <param name="annotatedAt">Новое время пометки.</param>
	/// <param name="price">Новая цена; null — вернуться к последней котировке при чтении.</param>
	/// <param name="quantity">Новое количество; null — вернуться к закрытию всего остатка.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ArgumentException">Инструмент не задан или состоит из пробелов; количество не положительно.</exception>
	/// <exception cref="ManualCloseAnnotationNotFoundException">Пометка не найдена.</exception>
	public async Task EditAsync(
		long annotationId,
		string symbol,
		DateTimeOffset annotatedAt,
		decimal? price,
		decimal? quantity,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
		EnsureQuantityIsValid(quantity);

		// Правка свободна: пометка — пользовательская закрывающая запись, производный
		// слой пересчитает позиции новыми значениями без ограничений.
		// Traceability: openspec:domain/constructions#requirement-position-close-lifecycle
		using var db = CreateContext();
		var quote = await FindQuoteAsync(db, annotationId, cancellationToken).ConfigureAwait(false);
		quote.Symbol = symbol;
		quote.AnnotatedAt = annotatedAt;
		quote.Price = price;
		quote.Quantity = quantity;
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region Удаление

	/// <summary>
	/// Удаляет ручную пометку закрытия. Позиция возвращается в открытое состояние
	/// с прежним остатком ближайшим чтением: закрывающая запись просто перестаёт
	/// участвовать в потоке.
	/// </summary>
	/// <param name="annotationId">Идентификатор пометки.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ManualCloseAnnotationNotFoundException">Пометка не найдена.</exception>
	public async Task DeleteAsync(long annotationId, CancellationToken cancellationToken = default)
	{
		// Удаление свободно и не трогает сделки: пересчёт при чтении сам «откроет»
		// позицию обратно, потому что закрывающая запись исчезнет из хронологии.
		// Traceability: openspec:domain/constructions#scenario-mark-removal-reopens
		using var db = CreateContext();
		var quote = await FindQuoteAsync(db, annotationId, cancellationToken).ConfigureAwait(false);
		db.ManualCloseAnnotations.Remove(quote);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region Чтение

	/// <summary>Возвращает ручные пометки конструкции в хронологическом порядке.</summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	public async Task<IReadOnlyList<ManualCloseAnnotation>> ListAsync(
		long constructionId,
		CancellationToken cancellationToken = default)
	{
		using var db = CreateContext();
		await EnsureConstructionExistsAsync(db, constructionId, cancellationToken).ConfigureAwait(false);

		// SQLite не переводит DateTimeOffset в ORDER BY — сортировка по времени
		// выполняется на клиенте после чтения строк.
		var quotes = await db.ManualCloseAnnotations
			.AsNoTracking()
			.Where(quote => quote.ConstructionId == constructionId)
			.OrderBy(quote => quote.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return quotes
			.OrderBy(quote => quote.AnnotatedAt)
			.ThenBy(quote => quote.Id)
			.ToList();
	}

	#endregion

	#region Помощники

	private JournalDbContext CreateContext() => new(_options);

	/// <summary>
	/// Отвергает неположительное количество пометки: заданное количество — это
	/// часть остатка, поэтому оно обязано быть больше нуля; незаданное (null)
	/// означает весь остаток и валидации не подлежит.
	/// </summary>
	/// <param name="quantity">Проверяемое количество пометки.</param>
	/// <exception cref="ArgumentException">Количество задано и не больше нуля.</exception>
	private static void EnsureQuantityIsValid(decimal? quantity)
	{
		// Валидация на записи: незаданное количество — весь остаток (легитимно),
		// заданное — часть остатка и обязано быть положительным.
		// Traceability: openspec:domain/constructions#scenario-manual-mark-partial-quantity
		if (quantity is { } requested && requested <= 0m)
		{
			throw new ArgumentException($"Количество пометки должно быть больше нуля, получено {requested}.", nameof(quantity));
		}
	}

	/// <summary>Проверяет существование конструкции до изменения пометок.</summary>
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

	/// <summary>Загружает пометку по идентификатору.</summary>
	/// <exception cref="ManualCloseAnnotationNotFoundException">Пометка не найдена.</exception>
	private static async Task<ManualCloseAnnotation> FindQuoteAsync(
		JournalDbContext db,
		long annotationId,
		CancellationToken cancellationToken)
	{
		var quote = await db.ManualCloseAnnotations
			.FirstOrDefaultAsync(candidate => candidate.Id == annotationId, cancellationToken)
			.ConfigureAwait(false);
		return quote ?? throw new ManualCloseAnnotationNotFoundException(annotationId);
	}

	#endregion
}

