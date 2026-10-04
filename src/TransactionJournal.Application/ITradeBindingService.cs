
namespace TransactionJournal.Application;

/// <summary>
/// Контракт use-case сервиса привязки сделок для тонких слоёв UI: перенос
/// сделки в другую конструкцию и возврат во «Входящие» из деталей, одиночная
/// и массовая привязка «Входящих». Экран зависит от интерфейса, а не от
/// конкретного сервиса и хранилища.
/// </summary>
// Перенос сделки и возврат во «Входящие» — операции принадлежности домена:
// экран деталей выполняет их через use-case сервис, не вводя собственных
// правил привязки.
// Traceability: openspec:ui/screens#requirement-trade-actions-in-detail
public interface ITradeBindingService
{
	/// <summary>Привязывает сделку к конструкции — из «Входящих» или переносом из другой конструкции; целевая привязка заменяет прежнюю.</summary>
	Task BindAsync(
		long constructionId,
		string execId,
		CancellationToken cancellationToken = default);

	/// <summary>Привязывает несколько сделок к одной конструкции одним атомарным действием.</summary>
	Task BindBatchAsync(
		long constructionId,
		IEnumerable<string> execIds,
		CancellationToken cancellationToken = default);

	/// <summary>Возвращает привязанную сделку во «Входящие», сохраняя комментарий сделки.</summary>
	Task UnbindAsync(string execId, CancellationToken cancellationToken = default);
}
