using TransactionJournal.Bybit;

namespace TransactionJournal.Sync;

/// <summary>
/// Производственная реализация шлюза истории поверх подписанного клиента Bybit:
/// делегирует вызов без дополнительной логики, сохраняя read-only характер доступа —
/// синхронизации хватает API-ключа с правами только на чтение.
/// Traceability: openspec:sync/bybit-history#requirement-read-only-access
/// </summary>
public sealed class BybitHistoryGateway : IBybitHistoryGateway
{
	private readonly BybitApiClient _client;

	/// <summary>Создаёт шлюз над готовым подписанным клиентом Bybit.</summary>
	/// <exception cref="ArgumentNullException">Клиент не задан.</exception>
	public BybitHistoryGateway(BybitApiClient client)
	{
		_client = client ?? throw new ArgumentNullException(nameof(client));
	}

	#region IBybitHistoryGateway

	/// <summary>GET /v5/execution/list — страница истории исполнения категории с курсором пагинации.</summary>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой retCode или неудачным HTTP-статусом после всех повторов.</exception>
	public Task<BybitPagedResponse<BybitExecution>> GetExecutionListAsync(
		BybitExecutionListQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		return _client.GetExecutionListAsync(query, cancellationToken);
	}

	/// <summary>
	/// GET /v5/asset/delivery-record — страница delivery-записей экспираций категории
	/// с курсором пагинации: источник закрывающих записей журнала.
	/// </summary>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой retCode или неудачным HTTP-статусом после всех повторов.</exception>
	public Task<BybitPagedResponse<BybitDeliveryRecord>> GetDeliveryRecordAsync(
		BybitDeliveryRecordQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		return _client.GetDeliveryRecordAsync(query, cancellationToken);
	}

	#endregion
}
