using Microsoft.EntityFrameworkCore;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain;
using TransactionJournal.Application;

namespace TransactionJournal.Infrastructure.ReadModels;

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

