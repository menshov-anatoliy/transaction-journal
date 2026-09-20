using TransactionJournal.Bybit;

namespace TransactionJournal.Sync;

/// <summary>
/// Шлюз истории Bybit для sync engine: узкий порт над подписанным HttpClient-клиентом,
/// чтобы проходы и движок синхронизации проверялись на фиктивном шлюзе без сети.
/// Порт повторяет read-only характер клиента: методов, изменяющих торговый аккаунт, здесь нет.
/// Traceability: change:add-bybit-sync/design#d2
/// Traceability: openspec:sync/bybit-history#requirement-read-only-access
/// </summary>
public interface IBybitHistoryGateway
{
	/// <summary>GET /v5/execution/list — страница истории исполнения категории с курсором пагинации.</summary>
	/// <param name="query">Параметры одного вызова эндпоинта: категория, границы окна, размер страницы, курсор.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой retCode или неудачным HTTP-статусом после всех повторов.</exception>
	Task<BybitPagedResponse<BybitExecution>> GetExecutionListAsync(
		BybitExecutionListQuery query,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// GET /v5/asset/delivery-record — страница delivery-записей экспираций категории
	/// с курсором пагинации: источник закрывающих записей журнала.
	/// </summary>
	/// <param name="query">Параметры одного вызова эндпоинта: категория, границы окна, размер страницы, курсор.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой retCode или неудачным HTTP-статусом после всех повторов.</exception>
	Task<BybitPagedResponse<BybitDeliveryRecord>> GetDeliveryRecordAsync(
		BybitDeliveryRecordQuery query,
		CancellationToken cancellationToken = default);
}
