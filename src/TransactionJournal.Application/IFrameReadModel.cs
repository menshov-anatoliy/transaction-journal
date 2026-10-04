using TransactionJournal.Domain;

namespace TransactionJournal.Application;

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
