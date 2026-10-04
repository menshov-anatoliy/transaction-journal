namespace TransactionJournal.Hints.Display;

using TransactionJournal.Hints.Ports;

/// <summary>
/// Read-модель отображения подсказок — интерфейс поверх хранилища подсказок:
/// панель субъекта (живые по группам + история), индикаторы живых подсказок
/// списка конструкций, общий журнал с фильтрами и единственные мутации UI —
/// кнопки «Применено»/«Отклонено» и автопометка первого показа. Домен
/// журнала интерфейс не видит; компоненты Web получают её через DI.
// Traceability: openspec:ui/screens#requirement-ui-hint-section-groups
// Traceability: openspec:ui/screens#requirement-ui-hint-panel-in-construction
// Traceability: openspec:ui/screens#requirement-ui-hint-log
/// </summary>
public interface IHintDisplayReadModel
{
	/// <summary>
	/// Панель «Подсказки» конструкции: живые подсказки сверху по группам
	/// справочника v1 (пустые группы отсутствуют), терминальная история
	/// свёрнута; сортировка по времени генерации.
	// Traceability: openspec:ui/screens#requirement-ui-hint-panel-in-construction
	/// </summary>
	/// <param name="constructionId">Идентификатор конструкции-субъекта.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<HintPanelData> ReadConstructionPanelAsync(long constructionId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Та же панель для субъекта «журнал» — портфельные подсказки обзорного
	/// экрана с теми же правилами состава и группировки.
	// Traceability: openspec:ui/screens#requirement-ui-portfolio-hint-panel
	/// </summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<HintPanelData> ReadJournalPanelAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Число живых подсказок каждой конструкции для индикаторов списка;
	/// конструкции без живых подсказок в ответе отсутствуют.
	// Traceability: openspec:ui/screens#requirement-ui-construction-hint-badge
	/// </summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<IReadOnlyDictionary<long, int>> ReadLiveCountsByConstructionAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Общий read-only журнал подсказок по всем субъектам с фильтрами статуса,
	/// характера и группы; записи идут свежими сверху.
	// Traceability: openspec:ui/screens#requirement-ui-hint-log
	/// </summary>
	/// <param name="filter">Фильтр; null — весь журнал без ограничений.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<IReadOnlyList<HintRecord>> ReadLogAsync(HintLogFilter? filter = null, CancellationToken cancellationToken = default);

	/// <summary>
	/// Команда «Применено» — человек переводит живую подсказку в терминальный
	/// статус applied; прочие мутации UI подсказок не предоставляет.
	// Traceability: openspec:ui/screens#requirement-ui-hint-panel-in-construction
	/// </summary>
	/// <param name="hintId">Идентификатор записи подсказки.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>true — перевод выполнен; false — записи нет или она уже терминальная.</returns>
	Task<bool> ApplyAsync(long hintId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Команда «Отклонено» — человек переводит живую подсказку в терминальный
	/// статус dismissed; повтор подсказки в текущем окне дедупа подавлен.
	// Traceability: openspec:ui/screens#requirement-ui-hint-panel-in-construction
	/// </summary>
	/// <param name="hintId">Идентификатор записи подсказки.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>true — перевод выполнен; false — записи нет или она уже терминальная.</returns>
	Task<bool> DismissAsync(long hintId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Автопометка первого показа подсказки в UI; последующие показы запись
	/// не меняют.
	// Traceability: openspec:ui/screens#requirement-ui-hint-panel-in-construction
	/// </summary>
	/// <param name="hintId">Идентификатор записи подсказки.</param>
	/// <param name="seenAt">Момент показа.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task MarkSeenAsync(long hintId, DateTimeOffset seenAt, CancellationToken cancellationToken = default);
}
