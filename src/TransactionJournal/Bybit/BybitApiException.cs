namespace TransactionJournal.Bybit;

/// <summary>
/// Ошибка ответа Bybit API: код retCode с сообщением биржи либо HTTP-статус,
/// когда тело не удалось разобрать как конверт V5. Тело ответа сохраняется для диагностики.
/// </summary>
public sealed class BybitApiException : Exception
{
	/// <summary>Код ошибки биржи retCode; null, если ответ не удалось разобрать.</summary>
	public int? RetCode { get; }

	/// <summary>HTTP-статус неудачного ответа; null, если ошибка пришла в теле успешного ответа.</summary>
	public int? HttpStatusCode { get; }

	/// <summary>Тело ответа биржи целиком.</summary>
	public string? ResponseBody { get; }

	/// <summary>Создаёт ошибку по коду retCode и сообщению биржи.</summary>
	public BybitApiException(int retCode, string retMsg, string? responseBody = null)
		: base($"Bybit API ответил ошибкой: retCode={retCode}, retMsg=\"{retMsg}\".")
	{
		RetCode = retCode;
		ResponseBody = responseBody;
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
