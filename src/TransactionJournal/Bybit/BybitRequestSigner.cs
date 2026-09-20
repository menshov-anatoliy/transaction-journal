using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TransactionJournal.Bybit;

/// <summary>
/// Сборка queryString и HMAC-SHA256-подписи GET-запросов Bybit.
/// Строка для подписи — конкатенация без разделителей timestamp + apiKey + recv_window + queryString,
/// алгоритм и формат результата (hex в нижнем регистре) повторяют официальный C#-пример Bybit.
/// Строка запроса собирается вручную и побайтово совпадает в URL и подписи.
/// Traceability: change:add-bybit-sync/design#d1
/// Traceability: doc:docs/research/bybit-api.md#6-аутентификация-hmac-из-c-без-sdk
/// </summary>
public static class BybitRequestSigner
{
	/// <summary>
	/// Собирает queryString вручную: пары «key=value», соединённые «&amp;», в заданном порядке.
	/// Значения подставляются как есть и должны быть готовы к использованию в URL,
	/// потому что одна и та же строка идёт и в URL запроса, и в подпись.
	/// </summary>
	/// <exception cref="ArgumentNullException">Список параметров не задан.</exception>
	public static string BuildQueryString(IReadOnlyList<KeyValuePair<string, string>> parameters)
	{
		ArgumentNullException.ThrowIfNull(parameters);

		var builder = new StringBuilder();
		for (var index = 0; index < parameters.Count; index++)
		{
			if (index > 0)
			{
				builder.Append('&');
			}

			builder.Append(parameters[index].Key).Append('=').Append(parameters[index].Value);
		}

		return builder.ToString();
	}

	/// <summary>
	/// Вычисляет подпись GET-запроса: HMAC-SHA256 от apiSecret по строке
	/// timestamp + apiKey + recvWindow + queryString; результат — hex в нижнем регистре.
	/// </summary>
	/// <exception cref="ArgumentNullException">Один из обязательных аргументов не задан.</exception>
	public static string ComputeSignature(long timestamp, string apiKey, int recvWindowMs, string queryString, string apiSecret)
	{
		ArgumentNullException.ThrowIfNull(apiKey);
		ArgumentNullException.ThrowIfNull(queryString);
		ArgumentNullException.ThrowIfNull(apiSecret);

		// Конкатенация без разделителей — ровно как в официальном примере Encryption_HMAC.cs;
		// числа форматируем инвариантной культурой, чтобы локаль не меняла строку подписи.
		var paramString = timestamp.ToString(CultureInfo.InvariantCulture)
			+ apiKey
			+ recvWindowMs.ToString(CultureInfo.InvariantCulture)
			+ queryString;

		using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret));
		var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(paramString));
		return Convert.ToHexString(hash).ToLowerInvariant();
	}
}
