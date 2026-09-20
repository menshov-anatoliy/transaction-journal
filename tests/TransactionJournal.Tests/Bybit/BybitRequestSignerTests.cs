using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Bybit;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Bybit;

/// <summary>
/// Проверки подписи запросов Bybit: HMAC-SHA256 вычисляется ровно по коду
/// официального C#-примера биржи (Encryption_HMAC.cs), результат — hex в нижнем
/// регистре, а queryString собирается вручную в заданном порядке.
/// </summary>
[TestClass]
public class BybitRequestSignerTests
{
	[TestMethod]
	[DataRow(1712345678000L, "bybit-api-key", 5000, "category=linear&symbol=BTCUSDT", "bybit-api-secret")]
	[DataRow(1701680884232L, "key-123", 5000, "category=option&symbol=BTC-27DEC24-65000-C", "secret-456")]
	[DataRow(1712345678000L, "key-123", 10000, "", "secret-456")]
	[Description("Подпись совпадает с результатом официального C#-примера Bybit для тех же входных данных")]
	public void TryIfSignatureMatchesOfficialBybitExample(
		long timestamp,
		string apiKey,
		int recvWindowMs,
		string queryString,
		string apiSecret)
	{
		// Arrange: эталон считается дословно кодом официального примера Bybit —
		// paramStr = timestamp + apiKey + recv_window + queryString, HMAC-SHA256, hex lower.
		// Traceability: change:add-bybit-sync/design#d1
		// Traceability: doc:docs/research/bybit-api.md#6-аутентификация-hmac-из-c-без-sdk
		var officialParamString = timestamp + apiKey + recvWindowMs + queryString;
		using var officialHmac = new HMACSHA256(Encoding.UTF8.GetBytes(apiSecret));
		var officialHash = officialHmac.ComputeHash(Encoding.UTF8.GetBytes(officialParamString));
		var expected = BitConverter.ToString(officialHash).Replace("-", string.Empty).ToLower();

		// Act: наша реализация подписывает те же входные данные.
		var actual = BybitRequestSigner.ComputeSignature(timestamp, apiKey, recvWindowMs, queryString, apiSecret);

		// Assert: подпись побайтово совпадает с официальным примером и остаётся hex lower.
		Assert.That(actual, Is.EqualTo(expected));
		Assert.That(actual, Does.Match("^[0-9a-f]+$"));
	}

	[TestMethod]
	[DataRow("category", "linear")]
	[Description("QueryString собирается вручную из пар ключ-значение")]
	public void TryIfQueryStringIsAssembledFromPairs(string firstKey, string firstValue)
	{
		// Arrange: порядок параметров значим — строка попадает и в URL, и в подпись.
		var parameters = new List<KeyValuePair<string, string>>
		{
			new(firstKey, firstValue),
			new("symbol", "BTC-27DEC24-65000-C"),
		};

		// Act: собираем строку запроса вручную, без автоматических кодировщиков.
		var queryString = BybitRequestSigner.BuildQueryString(parameters);

		// Assert: пары соединяются «=» и «&» ровно в заданном порядке.
		Assert.That(queryString, Is.EqualTo($"{firstKey}={firstValue}&symbol=BTC-27DEC24-65000-C"));
	}

	[TestMethod]
	[DataRow(null)]
	[Description("Отсутствие списка параметров — ошибка аргументов")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullQueryParameters(List<KeyValuePair<string, string>>? parameters)
	{
		// Arrange — список параметров не задан.

		// Act — сборка строки запроса от null-списка прерывается ошибкой аргументов.
		BybitRequestSigner.BuildQueryString(parameters!);
	}

	[TestMethod]
	[DataRow(1712345678000L, "bybit-api-key", 5000, "category=linear", null)]
	[DataRow(1712345678000L, null, 5000, "category=linear", "bybit-api-secret")]
	[DataRow(1712345678000L, "bybit-api-key", 5000, null, "bybit-api-secret")]
	[Description("Отсутствие секрета, ключа или строки запроса — ошибка аргументов")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnMissingSignatureInput(long timestamp, string? apiKey, int recvWindowMs, string? queryString, string? apiSecret)
	{
		// Arrange: любой пропуск обязательных входов подписи недопустим.

		// Act — вычисление подписи с null-аргументом прерывается ошибкой аргументов.
		BybitRequestSigner.ComputeSignature(timestamp, apiKey!, recvWindowMs, queryString!, apiSecret!);
	}
}
