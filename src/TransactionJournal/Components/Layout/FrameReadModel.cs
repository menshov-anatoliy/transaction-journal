using Microsoft.EntityFrameworkCore;
using TransactionJournal.Data;
using TransactionJournal.Domain;

namespace TransactionJournal.Components.Layout;

/// <summary>
/// Заголовок конструкции для транзитной вкладки каркаса: имя и ручной статус
/// без производных величин — вкладке достаточно опознать конструкцию и показать
/// её статус точкой.
/// </summary>
/// <param name="Id">Идентификатор конструкции.</param>
/// <param name="Name">Имя конструкции.</param>
/// <param name="Status">Ручной статус конструкции.</param>
public sealed record ConstructionHeader(long Id, string Name, ConstructionStatus Status);

/// <summary>
/// Read-модель каркаса «Терминала»: счётчик непривязанных сделок для бейджа
/// «Входящих» и заголовок открытой конструкции для её транзитной вкладки.
/// Каркас — тонкий слой над готовыми проекциями: счётчик берётся у read-модели
/// «Входящих» домена, заголовок читается напрямую из хранилища короткоживущим
/// контекстом; собственных доменных правил модель не вводит и ничего не мутирует.
/// </summary>
// Источник смысла: бейдж и транзитная вкладка определены требованием каркаса.
// Traceability: openspec:ui/screens#requirement-app-frame-navigation
public interface IFrameReadModel
{
	/// <summary>
	/// Возвращает число непривязанных сделок «Входящих» для бейджа вкладки.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Число непривязанных сделок; ноль означает пустые «Входящие».</returns>
	Task<int> CountInboxAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Возвращает заголовок конструкции для транзитной вкладки или null,
	/// если конструкция не существует.
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<ConstructionHeader?> FindConstructionHeaderAsync(
		long constructionId,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// Реализация read-модели каркаса над контекстом журнала: короткоживущий контекст
/// на вызов безопасен в длительных сессиях Blazor Server. Счётчик «Входящих»
/// делегируется доменной read-модели — материализация сырых записей остаётся
/// её ответственностью.
/// </summary>
public sealed class FrameReadModel : IFrameReadModel
{
	private readonly DbContextOptions<JournalDbContext> _options;
	private readonly InboxReadModel _inbox;

	/// <summary>Создаёт read-модель каркаса над опциями контекста журнала; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <param name="inbox">Доменная read-модель «Входящих».</param>
	/// <exception cref="ArgumentNullException">Аргументы не заданы.</exception>
	public FrameReadModel(DbContextOptions<JournalDbContext> options, InboxReadModel inbox)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_inbox = inbox ?? throw new ArgumentNullException(nameof(inbox));
	}

	/// <inheritdoc />
	/// <exception cref="TradeMaterializationException">Сырая запись повреждена или конфликтует по execId.</exception>
	/// <exception cref="InstrumentResolveException">Символ опциона не прошёл сверку со справочником.</exception>
	public async Task<int> CountInboxAsync(CancellationToken cancellationToken = default)
	{
		// Бейдж считает те же непривязанные сделки, что показывает экран «Входящих»:
		// одна проекция материализатора обслуживает и список, и счётчик.
		var trades = await _inbox.ListAsync(cancellationToken).ConfigureAwait(false);
		return trades.Count;
	}

	/// <inheritdoc />
	public async Task<ConstructionHeader?> FindConstructionHeaderAsync(
		long constructionId,
		CancellationToken cancellationToken = default)
	{
		using var db = new JournalDbContext(_options);
		var construction = await db.Constructions
			.FirstOrDefaultAsync(entity => entity.Id == constructionId, cancellationToken)
			.ConfigureAwait(false);
		return construction is null
			? null
			: new ConstructionHeader(construction.Id, construction.Name, construction.Status);
	}
}
