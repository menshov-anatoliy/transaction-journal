namespace TransactionJournal.Application.Bybit;

/// <summary>
/// Ошибка ответа Bybit API: код retCode с сообщением биржи либо HTTP-статус,
/// когда тело не удалось разобрать как конверт V5. Тело ответа сохраняется для диагностики.
/// </summary>
public sealed class BybitApiException : Exception
{
	/// <summary>Код ошибки биржи retCode 10001 «params error» — общий код отказа в параметрах запроса.</summary>
	private const int ParamsErrorRetCode = 10001;

	/// <summary>
	/// Код ошибки биржи retCode 110023 — отказ «контракт недоступен для торговли»:
	/// биржа не отдаёт историю контракта, ограниченного в торговле.
	/// </summary>
	private const int ContractUnavailableRetCode = 110023;

	/// <summary>
	/// Подстрока сообщения биржи об отказе за глубину хранения истории: единственный
	/// различающий сигнал среди прочих отказов параметров с тем же retCode 10001.
	/// </summary>
	private const string HistoryBoundaryMarker = "earlier than 2 years";

	/// <summary>Код ошибки биржи retCode; null, если ответ не удалось разобрать.</summary>
	public int? RetCode { get; }

	/// <summary>
	/// Сообщение ошибки биржи retMsg: носитель сигнала пограничного отказа за глубину
	/// хранения истории, который защитный контур перебора распознаёт по подстроке.
	/// Traceability: openspec:sync/bybit-history#requirement-history-boundary-guard
	/// </summary>
	public string? RetMsg { get; }

	/// <summary>HTTP-статус неудачного ответа; null, если ошибка пришла в теле успешного ответа.</summary>
	public int? HttpStatusCode { get; }

	/// <summary>Тело ответа биржи целиком.</summary>
	public string? ResponseBody { get; }

	/// <summary>Создаёт ошибку по коду retCode и сообщению биржи.</summary>
	public BybitApiException(int retCode, string retMsg, string? responseBody = null)
		: base($"Bybit API ответил ошибкой: retCode={retCode}, retMsg=\"{retMsg}\".")
	{
		RetCode = retCode;
		RetMsg = retMsg;
		ResponseBody = responseBody;
	}

	/// <summary>
	/// Распознаёт пограничный отказ биржи при выходе запроса за глубину хранения истории:
	/// это сигнал защитного контура перебора истории — окно у границы повторяется один раз
	/// с зажатым временем начала, а запуск не помечается неуспешным. Биржа не выделяет
	/// такому отказу собственный retCode, поэтому распознавание требует совпадения и кода
	/// 10001, и подстроки сообщения без учёта регистра.
	/// Traceability: openspec:sync/bybit-history#requirement-history-boundary-guard
	/// </summary>
	public static bool IsHistoryBoundaryError(BybitApiException exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		return exception.RetCode == ParamsErrorRetCode
			&& exception.RetMsg?.Contains(HistoryBoundaryMarker, StringComparison.OrdinalIgnoreCase) == true;
	}

	/// <summary>
	/// Распознаёт отказ биржи «контракт недоступен для торговли»: это сигнал защитного
	/// контура недоступных контрактов — область перебора, чьё окно биржа отвергла этим
	/// отказом, считается недоступной, обход её окон прекращается без ретрая, а запуск
	/// продолжается со следующими областями. Распознавание идёт только по коду: биржа
	/// переиспользует retCode 110023 с разными формулировками retMsg, поэтому текст
	/// в матч включать нельзя.
	/// Traceability: openspec:sync/bybit-history#requirement-contract-unavailable-area-skip
	/// </summary>
	public static bool IsContractUnavailableError(BybitApiException exception)
	{
		ArgumentNullException.ThrowIfNull(exception);
		return exception.RetCode == ContractUnavailableRetCode;
	}

	/// <summary>Создаёт ошибку по HTTP-статусу, когда конверт retCode недоступен.</summary>
	public static BybitApiException FromHttpStatus(int statusCode, string responseBody)
	{
		return new BybitApiException(
			$"Bybit API ответил HTTP-статусом {statusCode}: \"{responseBody}\".",
			responseBody,
			httpStatusCode: statusCode);
	}

	/// <summary>
	/// Создаёт понятную пользователю ошибку блокировки доступа по HTTP 403: исчерпан
	/// IP-лимит, биржа блокирует доступ примерно на длительность паузы; повторите позже.
	/// Traceability: openspec:sync/bybit-history#requirement-api-limits-and-error-handling
	/// Traceability: change:add-bybit-sync/design#d5
	/// </summary>
	public static BybitApiException FromAccessBlocked(int statusCode, TimeSpan pause, string responseBody)
	{
		var pauseMinutes = (int)Math.Ceiling(pause.TotalMinutes);
		return new BybitApiException(
			$"Bybit отклонил запрос HTTP {statusCode} («access too frequent»): исчерпан IP-лимит, "
			+ $"биржа блокирует доступ примерно на {pauseMinutes} минут. Запустите синхронизацию позже.",
			responseBody,
			httpStatusCode: statusCode);
	}

	/// <summary>Создаёт ошибку разбора ответа биржи без кода retCode.</summary>
	public static BybitApiException FromMalformedBody(string message, string responseBody)
	{
		return new BybitApiException($"{message} Тело ответа: \"{responseBody}\".", responseBody);
	}

	private BybitApiException(string message, string responseBody, int? httpStatusCode = null)
		: base(message)
	{
		ResponseBody = responseBody;
		HttpStatusCode = httpStatusCode;
	}
}
