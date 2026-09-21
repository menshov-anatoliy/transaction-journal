using Microsoft.EntityFrameworkCore;
using TransactionJournal.Data;

namespace TransactionJournal.Domain;

/// <summary>
/// Контракт use-case сервиса ручных пометок закрытия для тонких слоёв UI:
/// экран деталей ставит пометку из строки открытой позиции и правит либо
/// удаляет её из таблицы закрывающих записей, не завися от конкретного
/// сервиса и хранилища.
/// </summary>
// Ручная пометка закрытия ставится из строки позиции и сопровождается
// предупреждением об избыточной записи — мутации идут через доменный контракт.
// Traceability: openspec:ui/screens#requirement-manual-close-mark-from-position
public interface IManualCloseMarkService
{
	/// <summary>Добавляет ручную пометку закрытия позиции конструкции; цена null подставляется последней маркой при чтении.</summary>
	Task<ManualCloseMark> AddAsync(
		long constructionId,
		string symbol,
		DateTimeOffset markedAt,
		decimal? price = null,
		CancellationToken cancellationToken = default);

	/// <summary>Свободно правит инструмент, время и цену ручной пометки закрытия.</summary>
	Task EditAsync(
		long markId,
		string symbol,
		DateTimeOffset markedAt,
		decimal? price,
		CancellationToken cancellationToken = default);

	/// <summary>Удаляет ручную пометку закрытия — позиция возвращается в открытое состояние ближайшим чтением.</summary>
	Task DeleteAsync(long markId, CancellationToken cancellationToken = default);

	/// <summary>Возвращает ручные пометки конструкции в хронологическом порядке.</summary>
	Task<IReadOnlyList<ManualCloseMark>> ListAsync(
		long constructionId,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// Use-case сервис ручных пометок закрытия — пользовательских закрывающих записей
/// для инструментов без биржевых записей закрытия. Пометка добавляется с ценой
/// пользователя или без неё (производный слой подставляет последнюю известную
/// марку при чтении), свободно правится и удаляется — позиции пересчитываются
/// при чтении, поэтому удаление возвращает позицию в открытое состояние без
/// синхронных правок производных. Каждый вызов создаёт собственный
/// короткоживущий контекст, поэтому сервис безопасен в длительных сессиях
/// Blazor Server.
// Traceability: openspec:domain/constructions#requirement-position-close-lifecycle
// Traceability: change:add-core-domain/design#d3
/// </summary>
public sealed class ManualCloseMarkService : IManualCloseMarkService
{
	private readonly DbContextOptions<JournalDbContext> _options;

	/// <summary>Создаёт сервис над опциями контекста журнала; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <exception cref="ArgumentNullException">Опции не заданы.</exception>
	public ManualCloseMarkService(DbContextOptions<JournalDbContext> options)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
	}

	#region Добавление

	/// <summary>
	/// Добавляет ручную пометку закрытия позиции конструкции. Цена необязательна:
	/// незаданная цена разрешается производным слоем последней известной маркой
	/// инструмента при чтении.
	/// </summary>
	/// <param name="constructionId">Конструкция, внутри которой закрывается позиция.</param>
	/// <param name="symbol">Инструмент закрываемой позиции.</param>
	/// <param name="markedAt">Время пометки: задаёт место записи в хронологии закрывающих записей.</param>
	/// <param name="price">Цена закрытия по выбору пользователя; null — взять последнюю марку при чтении.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Созданная пометка.</returns>
	/// <exception cref="ArgumentException">Инструмент не задан или состоит из пробелов.</exception>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	public async Task<ManualCloseMark> AddAsync(
		long constructionId,
		string symbol,
		DateTimeOffset markedAt,
		decimal? price = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

		// Пометка — пользовательская запись домена: остаток не проверяется, избыточная
		// пометка превращается в предупреждение read-модели, а не в запрет.
		// Traceability: change:add-core-domain/design#risks-trade-offs
		using var db = CreateContext();
		await EnsureConstructionExistsAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		var mark = new ManualCloseMark
		{
			ConstructionId = constructionId,
			Symbol = symbol,
			Price = price,
			MarkedAt = markedAt,
		};
		db.ManualCloseMarks.Add(mark);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return mark;
	}

	#endregion

	#region Правка

	/// <summary>
	/// Свободно правит атрибуты ручной пометки: инструмент, время и цену. Перенос
	/// в другую конструкцию не выполняется — для этого пометка удаляется и заводится
	/// заново. Новые значения подхватываются ближайшим чтением позиций.
	/// </summary>
	/// <param name="markId">Идентификатор пометки.</param>
	/// <param name="symbol">Новый инструмент помечки.</param>
	/// <param name="markedAt">Новое время пометки.</param>
	/// <param name="price">Новая цена; null — вернуться к последней марке при чтении.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ArgumentException">Инструмент не задан или состоит из пробелов.</exception>
	/// <exception cref="ManualCloseMarkNotFoundException">Пометка не найдена.</exception>
	public async Task EditAsync(
		long markId,
		string symbol,
		DateTimeOffset markedAt,
		decimal? price,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

		// Правка свободна: пометка — пользовательская закрывающая запись, производный
		// слой пересчитает позиции новыми значениями без ограничений.
		// Traceability: openspec:domain/constructions#requirement-position-close-lifecycle
		using var db = CreateContext();
		var mark = await FindMarkAsync(db, markId, cancellationToken).ConfigureAwait(false);
		mark.Symbol = symbol;
		mark.MarkedAt = markedAt;
		mark.Price = price;
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region Удаление

	/// <summary>
	/// Удаляет ручную пометку закрытия. Позиция возвращается в открытое состояние
	/// с прежним остатком ближайшим чтением: закрывающая запись просто перестаёт
	/// участвовать в потоке.
	/// </summary>
	/// <param name="markId">Идентификатор пометки.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ManualCloseMarkNotFoundException">Пометка не найдена.</exception>
	public async Task DeleteAsync(long markId, CancellationToken cancellationToken = default)
	{
		// Удаление свободно и не трогает сделки: пересчёт при чтении сам «откроет»
		// позицию обратно, потому что закрывающая запись исчезнет из хронологии.
		// Traceability: openspec:domain/constructions#scenario-mark-removal-reopens
		using var db = CreateContext();
		var mark = await FindMarkAsync(db, markId, cancellationToken).ConfigureAwait(false);
		db.ManualCloseMarks.Remove(mark);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region Чтение

	/// <summary>Возвращает ручные пометки конструкции в хронологическом порядке.</summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	public async Task<IReadOnlyList<ManualCloseMark>> ListAsync(
		long constructionId,
		CancellationToken cancellationToken = default)
	{
		using var db = CreateContext();
		await EnsureConstructionExistsAsync(db, constructionId, cancellationToken).ConfigureAwait(false);

		// SQLite не переводит DateTimeOffset в ORDER BY — сортировка по времени
		// выполняется на клиенте после чтения строк.
		var marks = await db.ManualCloseMarks
			.AsNoTracking()
			.Where(mark => mark.ConstructionId == constructionId)
			.OrderBy(mark => mark.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return marks
			.OrderBy(mark => mark.MarkedAt)
			.ThenBy(mark => mark.Id)
			.ToList();
	}

	#endregion

	#region Помощники

	private JournalDbContext CreateContext() => new(_options);

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
	/// <exception cref="ManualCloseMarkNotFoundException">Пометка не найдена.</exception>
	private static async Task<ManualCloseMark> FindMarkAsync(
		JournalDbContext db,
		long markId,
		CancellationToken cancellationToken)
	{
		var mark = await db.ManualCloseMarks
			.FirstOrDefaultAsync(candidate => candidate.Id == markId, cancellationToken)
			.ConfigureAwait(false);
		return mark ?? throw new ManualCloseMarkNotFoundException(markId);
	}

	#endregion
}
