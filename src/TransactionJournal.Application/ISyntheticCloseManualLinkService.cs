using TransactionJournal.Domain.Data;

namespace TransactionJournal.Application;

/// <summary>
/// Контракт use-case сервиса ручных синтетических связок для тонких слоёв UI:
/// пользователь соединяет линейную сделку и встречный опцион одной конструкции
/// без 60-минутного окна распознавания. Сервис проверяет совпадение страйка
/// и экспирации встречного опциона с закрываемой ногой и согласованность
/// количества с количествами обеих сделок; связка удаляется свободно,
/// редактируется пересозданием.
/// </summary>
// Ручная связка — ручные данные логического слоя: действует наравне с
// распознанными, переживает инкрементную сборку и стирается полным пересбором.
// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
// Traceability: change:manual-journal-management/design#d5
public interface ISyntheticCloseManualLinkService
{
	/// <summary>
	/// Создаёт ручную связку из линейной сделки и встречного опциона одной
	/// конструкции. Обе сделки должны принадлежать конструкции, встречный
	/// опцион — совпадать страйком и экспирацией с закрываемой ногой,
	/// количество — не превышать количество ни одной из сделок; окно времени
	/// между сделками не проверяется.
	/// </summary>
	/// <param name="constructionId">Конструкция, внутри которой создаётся связка.</param>
	/// <param name="linearTradeId">Идентификатор линейной сделки: execId биржевой либо ключ ручной.</param>
	/// <param name="optionTradeId">Идентификатор встречной опционной сделки — с теми же правилами.</param>
	/// <param name="quantity">Количество связки: положительное, не больше количеств обеих сделок.</param>
	/// <param name="cancellationToken">Токен отмены операции.</param>
	/// <returns>Сохранённая ручная связка.</returns>
	/// <exception cref="ArgumentException">Идентификатор сделки не задан или состоит из пробелов.</exception>
	/// <exception cref="SyntheticCloseManualLinkValidationException">Нарушены правила создания связки.</exception>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	Task<SyntheticCloseManualLink> CreateAsync(
		long constructionId,
		string linearTradeId,
		string optionTradeId,
		decimal quantity,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Удаляет ручную связку: связка исчезает из логического слоя ближайшим
	/// чтением; распознанные связки правила не затрагивают.
	/// </summary>
	/// <param name="constructionId">Конструкция, внутри которой удаляется связка.</param>
	/// <param name="linkId">Идентификатор ручной связки.</param>
	/// <param name="cancellationToken">Токен отмены операции.</param>
	/// <exception cref="SyntheticCloseManualLinkNotFoundException">Связки с таким идентификатором в конструкции нет.</exception>
	Task DeleteAsync(long constructionId, long linkId, CancellationToken cancellationToken = default);
}
