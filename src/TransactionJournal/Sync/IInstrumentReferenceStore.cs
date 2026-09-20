using TransactionJournal.Bybit;
using TransactionJournal.Data;

namespace TransactionJournal.Sync;

/// <summary>
/// Порт сырого хранилища справочника инструментов: проверка известных символов
/// и идемпотентная вставка сырых спецификаций с продвижением счётчика запуска.
/// Уникальный индекс по символу на уровне БД страхует вставку от дублей.
/// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
/// </summary>
public interface IInstrumentReferenceStore
{
	/// <summary>
	/// Возвращает подмножество запрошенных символов, уже известных справочнику.
	/// </summary>
	/// <param name="symbols">Символы инструментов, встреченные в записях синхронизации.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="ArgumentNullException">Коллекция символов не задана.</exception>
	Task<IReadOnlySet<string>> FindKnownSymbolsAsync(
		IReadOnlyCollection<string> symbols,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Сохраняет новые спецификации инструментов в сыром виде и возвращает число фактически
	/// вставленных строк; известные символы пропускаются. Счётчик NewInstruments запуска
	/// продвигается той же транзакцией, что и вставка.
	/// </summary>
	/// <param name="category">Торговая категория инструментов: linear или option.</param>
	/// <param name="instruments">Спецификации из ответа instruments-info.</param>
	/// <param name="progressRun">Запуск, чей счётчик новых инструментов продвигается; null — прогресс не ведётся.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="ArgumentException">Категория не задана.</exception>
	/// <exception cref="ArgumentNullException">Коллекция спецификаций не задана.</exception>
	Task<int> WriteAsync(
		string category,
		IReadOnlyCollection<BybitInstrumentInfo> instruments,
		SyncRun? progressRun = null,
		CancellationToken cancellationToken = default);
}
