using System.Text.Json;

namespace TransactionJournal.Bybit;

/// <summary>
/// Общий разбор конверта ответов Bybit V5: проверка кода retCode и извлечение поля result
/// в типизированные записи. Подписанный API-клиент и публичный клиент тикеров получают
/// одинаковые семантики ошибок: ненулевой retCode и повреждённая форма тела превращаются
/// в BybitApiException с сохранением ответа для диагностики.
/// </summary>
internal static class BybitResponse
{
	private const string RetCodePropertyName = "retCode";
	private const string RetMsgPropertyName = "retMsg";
	private const string ResultPropertyName = "result";

	/// <summary>
	/// Разбирает поле result конверта Bybit в типизированный ответ; ошибки формы
	/// приводятся к понятному исключению с сохранением тела для диагностики.
	/// </summary>
	internal static T DeserializeResult<T>(string body, string path)
	{
		try
		{
			using var document = JsonDocument.Parse(body);
			var result = document.RootElement.GetProperty(ResultPropertyName);
			return result.Deserialize<T>(BybitJson.Options)
				?? throw BybitApiException.FromMalformedBody(
					$"Ответ {path} не удалось разобрать как {typeof(T).Name}.", body);
		}
		catch (JsonException exception)
		{
			throw BybitApiException.FromMalformedBody(
				$"Ответ {path} не соответствует ожидаемому формату результата: {exception.Message}", body);
		}
		catch (KeyNotFoundException)
		{
			throw BybitApiException.FromMalformedBody($"Ответ {path} не содержит поле result.", body);
		}
	}

	/// <summary>
	/// Проверяет конверт ответа: нулевой retCode означает успех, любое другое значение —
	/// ошибку биржи; отсутствие корректного retCode трактуется как повреждённый ответ.
	/// </summary>
	internal static void ThrowIfApiError(string body)
	{
		if (TryReadEnvelope(body, out var retCode, out var retMsg))
		{
			// Нулевой retCode — успешный ответ биржи; любое другое значение — ошибка.
			if (retCode != 0)
			{
				throw new BybitApiException(retCode, retMsg ?? string.Empty, body);
			}

			return;
		}

		throw BybitApiException.FromMalformedBody("Ответ Bybit не содержит корректное поле retCode.", body);
	}

	private static bool TryReadEnvelope(string body, out int retCode, out string? retMsg)
	{
		try
		{
			using var document = JsonDocument.Parse(body);
			var root = document.RootElement;
			if (root.ValueKind == JsonValueKind.Object
				&& root.TryGetProperty(RetCodePropertyName, out var retCodeElement)
				&& retCodeElement.ValueKind == JsonValueKind.Number
				&& retCodeElement.TryGetInt32(out retCode))
			{
				retMsg = root.TryGetProperty(RetMsgPropertyName, out var retMsgElement)
					&& retMsgElement.ValueKind == JsonValueKind.String
					? retMsgElement.GetString()
					: null;
				return true;
			}
		}
		catch (JsonException)
		{
			// Некорректный JSON возвращается вызывающей стороне как «нет конверта retCode».
		}

		retCode = 0;
		retMsg = null;
		return false;
	}
}
