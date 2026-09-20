using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Data;
using TransactionJournal.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Materialization;

/// <summary>
/// Проверки справочника инструментов: построение из сырых записей эндпоинта
/// instruments-info (категория, optionsType, baseCoin, deliveryTime) и поиск по символу.
/// </summary>
[TestClass]
public class InstrumentCatalogTests
{
	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	[TestMethod]
	[Description("Справочник строится из сырых записей instruments-info: категория, базовый актив, тип опциона и время delivery")]
	public void TryIfBuildsCatalogFromRawInstruments()
	{
		// Arrange: опцион BTC с delivery 27DEC24 08:00 UTC и линейный бессрочный BTCUSDT.
		// Требование: справочник инструментов строится из данных эндпоинта спецификаций —
		// канонические свойства берутся из него, а не из строки символа.
		// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
		var btcDeliveryMs = new DateTimeOffset(2024, 12, 27, 8, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
		var rawInstruments = new[]
		{
			Raw("BTC-27DEC24-2800-C", "option", OptionPayload("BTC", "Call", btcDeliveryMs)),
			Raw("BTCUSDT", "linear", "{\"symbol\":\"BTCUSDT\",\"contractType\":\"LinearPerpetual\",\"status\":\"Trading\",\"baseCoin\":\"BTC\",\"quoteCoin\":\"USDT\",\"settleCoin\":\"USDT\",\"deliveryTime\":\"0\",\"optionsType\":\"\"}"),
		};

		// Act
		var catalog = new InstrumentCatalog(rawInstruments);

		// Assert: запись опциона несёт канонические значения спецификации биржи.
		Assert.That(catalog.Count, Is.EqualTo(2));
		Assert.That(catalog.TryGet("BTC-27DEC24-2800-C", out var option), Is.True);
		Assert.That(option!.Category, Is.EqualTo("option"));
		Assert.That(option.BaseCoin, Is.EqualTo("BTC"));
		Assert.That(option.OptionsType, Is.EqualTo(OptionType.Call));
		Assert.That(option.DeliveryTime, Is.EqualTo(new DateTimeOffset(2024, 12, 27, 8, 0, 0, TimeSpan.Zero)));

		// Assert: у бессрочного контракта нет типа опциона и доставки (deliveryTime = 0).
		Assert.That(catalog.TryGet("BTCUSDT", out var linear), Is.True);
		Assert.That(linear!.Category, Is.EqualTo("linear"));
		Assert.That(linear.BaseCoin, Is.EqualTo("BTC"));
		Assert.That(linear.OptionsType, Is.Null);
		Assert.That(linear.DeliveryTime, Is.Null);
	}

	[TestMethod]
	[Description("Символа нет в справочнике — TryGet возвращает false")]
	public void TryIfUnknownSymbolIsNotFound()
	{
		// Arrange
		var catalog = new InstrumentCatalog(new[] { Raw("BTC-27DEC24-2800-C", "option", OptionPayload("BTC", "Call", 0)) });

		// Act
		var found = catalog.TryGet("ETH-3JAN23-1250-P", out var entry);

		// Assert
		Assert.That(found, Is.False);
		Assert.That(entry, Is.Null);
	}

	[TestMethod]
	[DataRow("")]
	[DataRow(" ")]
	[Description("Пустой символ отклоняется при поиске в справочнике")]
	[ExpectedException(typeof(ArgumentException))]
	public void ThrowOnTryGetWithEmptySymbol(string symbol)
	{
		// Arrange
		var catalog = new InstrumentCatalog([]);

		// Act — Assert
		catalog.TryGet(symbol, out _);
	}

	[TestMethod]
	[Description("Некорректный JSON спецификации останавливает построение справочника с FormatException")]
	[ExpectedException(typeof(FormatException))]
	public void ThrowOnMalformedInstrumentPayload()
	{
		// Arrange: сырое хранилище гарантий на форму JSON не даёт — повреждённая запись
		// должна останавливать построение справочника с внятной ошибкой.
		var rawInstruments = new[] { Raw("BTC-27DEC24-2800-C", "option", "{ не json") };

		// Act — Assert
		new InstrumentCatalog(rawInstruments);
	}

	[TestMethod]
	[Description("Null-коллекция сырых записей отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullRawInstruments()
	{
		// Arrange — Act — Assert
		new InstrumentCatalog(null!);
	}

	#region Помощники

	private static RawInstrument Raw(string symbol, string category, string payloadJson) => new()
	{
		Symbol = symbol,
		Category = category,
		PayloadJson = payloadJson,
		FetchedAt = FetchedAt,
	};

	/// <summary>Спецификация опциона в форме ответа instruments-info: числа биржа шлёт строками.</summary>
	private static string OptionPayload(string baseCoin, string optionsType, long deliveryTimeMs) =>
		$$"""{"symbol":"BTC-27DEC24-2800-C","baseCoin":"{{baseCoin}}","quoteCoin":"USD","settleCoin":"USDC","status":"Trading","optionsType":"{{optionsType}}","deliveryTime":"{{deliveryTimeMs}}","deliveryFeeRate":"0.00015"}""";

	#endregion
}
