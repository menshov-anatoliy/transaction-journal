using TransactionJournal.Bybit;

namespace TransactionJournal.Sync;

/// <summary>
/// Производственная реализация шлюза истории поверх подписанного клиента Bybit:
/// делегирует вызов без дополнительной логики, сохраняя read-only характер доступа —
/// синхронизации хватает API-ключа с правами только на чтение. Помимо истории
/// исполнения и delivery-записей шлюз отдаёт публичные спецификации инструментов
/// для пополнения справочника журнала.
/// Traceability: openspec:sync/bybit-history#requirement-read-only-access
/// </summary>
public sealed class BybitHistoryGateway : IBybitHistoryGateway, IBybitInstrumentSource
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

	#region IBybitInstrumentSource

	/// <summary>
	/// GET /v5/market/instruments-info — публичные спецификации инструментов:
	/// канонический источник справочника журнала для символов, встреченных в записях.
	/// </summary>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой retCode или неудачным HTTP-статусом после всех повторов.</exception>
	public Task<BybitPagedResponse<BybitInstrumentInfo>> GetInstrumentInfoAsync(
		BybitInstrumentInfoQuery query,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(query);
		return _client.GetInstrumentsInfoAsync(query, cancellationToken);
	}

	#endregion
}
