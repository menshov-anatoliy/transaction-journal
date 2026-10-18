using TransactionJournal.Domain.Data;

namespace TransactionJournal.Application.ManualTrades;

/// <summary>
/// Контракт use-case сервиса ручных сделок для тонких слоёв UI и API: внесение
/// ручной сделки во «Входящие» либо сразу в конструкцию, правка её атрибутов
/// и удаление. Сервис проверяет значения формы (дата не в будущем, количество
/// и цена положительны, комиссия неотрицательна), разрешает символ справочником
/// инструментов и охраняет неизменяемость биржевых записей; производные данные
/// пересчитываются при чтении по действующим паттернам журнала.
/// </summary>
// Ручная сделка вносится пользователем и доступна правке и удалению в любом
// месте — во «Входящих» и в конструкции; биржевые записи неизменяемы.
// Traceability: openspec:domain/manual-trades#requirement-manual-trade-entry
// Traceability: openspec:domain/manual-trades#requirement-manual-trade-edit-delete
public interface IManualTradeService
{
	/// <summary>
	/// Вносит ручную сделку во «Входящие» либо сразу в указанную конструкцию.
	/// Символ перед сохранением сверяется со справочником инструментов.
	/// </summary>
	/// <param name="draft">Данные формы ручного ввода.</param>
	/// <param name="cancellationToken">Токен отмены операции.</param>
	/// <returns>Сохранённая ручная сделка.</returns>
	/// <exception cref="ArgumentException">Инструмент не задан или состоит из пробелов.</exception>
	/// <exception cref="ManualTradeValidationException">Значения формы нарушают правила валидации.</exception>
	/// <exception cref="ConstructionNotFoundException">Указанная конструкция не найдена.</exception>
	/// <exception cref="ManualSymbolResolutionException">Символ не разрешён справочником и биржей.</exception>
	Task<ManualTrade> CreateAsync(ManualTradeDraft draft, CancellationToken cancellationToken = default);

	/// <summary>
	/// Правит атрибуты ручной сделки; изменения подхватываются производными
	/// данными при ближайшем чтении. Привязка сделки правкой не меняется —
	/// перенос выполняют существующие команды привязки.
	/// </summary>
	/// <param name="id">Идентификатор ручной сделки.</param>
	/// <param name="draft">Новые значения атрибутов; привязка черновика игнорируется.</param>
	/// <param name="cancellationToken">Токен отмены операции.</param>
	/// <returns>Обновлённая ручная сделка.</returns>
	/// <exception cref="ArgumentException">Инструмент не задан или состоит из пробелов.</exception>
	/// <exception cref="ManualTradeValidationException">Значения формы нарушают правила валидации.</exception>
	/// <exception cref="ManualTradeImmutableException">Идентификатор адресует биржевую запись.</exception>
	/// <exception cref="ManualTradeNotFoundException">Ручной сделки с таким идентификатором нет.</exception>
	/// <exception cref="ManualSymbolResolutionException">Символ не разрешён справочником и биржей.</exception>
	Task<ManualTrade> EditAsync(long id, ManualTradeDraft draft, CancellationToken cancellationToken = default);

	/// <summary>
	/// Удаляет ручную сделку из любого места — из «Входящих» и из конструкции;
	/// позиции и метрики пересчитываются при чтении без удалённой сделки.
	/// </summary>
	/// <param name="id">Идентификатор ручной сделки.</param>
	/// <param name="cancellationToken">Токен отмены операции.</param>
	/// <exception cref="ManualTradeImmutableException">Идентификатор адресует биржевую запись.</exception>
	/// <exception cref="ManualTradeNotFoundException">Ручной сделки с таким идентификатором нет.</exception>
	Task DeleteAsync(long id, CancellationToken cancellationToken = default);
}
