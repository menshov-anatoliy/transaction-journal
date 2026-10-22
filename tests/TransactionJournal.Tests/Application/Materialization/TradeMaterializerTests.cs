using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Materialization;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;
namespace TransactionJournal.Tests.Materialization;
/// <summary>
/// Проверки материализатора сделок «Входящих»: вывод сделок из сырых записей
/// исполнения с атрибутами биржевой записи (знак количества по стороне, execPrice,
/// execFee со знаком и feeCurrency, USDC ≡ USDT по ADR-0001) и идемпотентность
/// проекции при повторных синках.
/// </summary>
[TestClass]
public class TradeMaterializerTests
{
	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	// Каноническое время delivery инструмента из справочника: 29DEC23 08:00 UTC.
	private static readonly DateTimeOffset OptionDelivery = new(2023, 12, 29, 8, 0, 0, TimeSpan.Zero);

	[TestMethod]
	[Description("Инкрементальная догрузка приводит во «Входящие» только новые сделки")]
	public void TryIfIncrementalSyncBringsOnlyNewTradesToInbox()
	{
		// Arrange: первый синк принёс две сделки опциона — покупку и продажу.
		// Требование: после синка во «Входящих» появляются новые сделки биржи
		// и только они, с атрибутами из биржевой записи исполнения.
		// Traceability: openspec:sync/bybit-history#scenario-new-trades-land-in-inbox
		var materializer = CreateMaterializer();
		var firstSyncExecutions = new[]
		{
			RawOption("exec-1", "Buy", ExecMs(2023, 12, 28, 10, 0)),
			RawOption("exec-2", "Sell", ExecMs(2023, 12, 28, 11, 0)),
		};
		var inboxAfterFirstSync = materializer.Materialize(firstSyncExecutions).Trades;
		Assert.That(inboxAfterFirstSync.Count, Is.EqualTo(2));

		// Act: инкрементальная догрузка принесла одну новую сделку — материализатор
		// работает по всему сырью хранилища (прежние записи плюс новая).
		var secondSyncExecutions = firstSyncExecutions
			.Append(RawOption("exec-3", "Buy", ExecMs(2023, 12, 28, 12, 0)))
			.ToArray();
		var inboxAfterSecondSync = materializer.Materialize(secondSyncExecutions).Trades;

		// Assert: во «Входящих» появились только новые сделки; прежние не изменились
		// и не задвоились.
		Assert.That(inboxAfterSecondSync.Count, Is.EqualTo(3));
		Assert.That(
			inboxAfterSecondSync.Select(trade => trade.ExecId).ToArray(),
			Is.EqualTo(new[] { "exec-1", "exec-2", "exec-3" }));
		Assert.That(inboxAfterSecondSync.Take(2).ToArray(), Is.EqualTo(inboxAfterFirstSync.ToArray()));
	}

	[TestMethod]
	[Description("Атрибуты сделки соответствуют биржевой записи исполнения; USDC-комиссия приведена к USDT")]
	public void TryIfTradeAttributesMatchExchangeRecord()
	{
		// Arrange: покупка опциона BTC-29DEC23-45000-C по 45000 количеством 0.0001
		// с комиссией 0.01 USDC — журнал учитывает USDC в паритете 1:1 к USDT.
		// Требование: атрибуты сделки «Входящих» соответствуют биржевой записи.
		// Traceability: openspec:sync/bybit-history#requirement-new-records-land-in-inbox
		// Traceability: adr:docs/adr/0001-usdc-usdt-parity.md#usdc-usdt-parity-1-1
		var materializer = CreateMaterializer();
		var executedAt = new DateTimeOffset(2023, 12, 28, 10, 0, 0, TimeSpan.Zero);
		var rawExecutions = new[]
		{
			Raw("exec-buy-1", "option", "BTC-29DEC23-45000-C", executedAt.ToUnixTimeMilliseconds(),
				ExecutionPayload("exec-buy-1", "BTC-29DEC23-45000-C", "Buy", "45000", "0.0001", "0.01", "USDC",
					executedAt.ToUnixTimeMilliseconds(), isMaker: true)),
		};

		// Act
		var trades = materializer.Materialize(rawExecutions).Trades;

		// Assert: каждый атрибут повторяет биржевую запись; канонические атрибуты
		// опциона взяты из справочника инструментов.
		Assert.That(trades.Count, Is.EqualTo(1));
		var trade = trades[0];
		Assert.That(trade.ExecId, Is.EqualTo("exec-buy-1"));
		Assert.That(trade.Category, Is.EqualTo("option"));
		Assert.That(trade.Symbol, Is.EqualTo("BTC-29DEC23-45000-C"));
		Assert.That(trade.ExecutedAt, Is.EqualTo(executedAt));
		Assert.That(trade.Quantity, Is.EqualTo(0.0001m));
		Assert.That(trade.Price, Is.EqualTo(45000m));
		Assert.That(trade.Fee, Is.EqualTo(0.01m));
		Assert.That(trade.FeeCurrency, Is.EqualTo("USDT"));
		Assert.That(trade.IsMaker, Is.True);
		Assert.That(trade.Option, Is.Not.Null);
		Assert.That(trade.Option!.BaseCoin, Is.EqualTo("BTC"));
		Assert.That(trade.Option.OptionsType, Is.EqualTo(OptionType.Call));
		Assert.That(trade.Option.Strike, Is.EqualTo(45000m));
		Assert.That(trade.Option.DeliveryTime, Is.EqualTo(OptionDelivery));
	}

	[TestMethod]
	[Description("Продажа даёт отрицательное количество, знак комиссии rebate сохраняется")]
	public void TryIfSellSideGivesNegativeQuantityAndKeepsRebateSign()
	{
		// Arrange: продажа опциона с отрицательной комиссией (rebate) в BTC —
		// знак количества берётся по стороне Sell, знак комиссии остаётся знаком биржи,
		// а валюта вне паритета USDC/USDT не перекладывается.
		var materializer = CreateMaterializer();
		var rawExecutions = new[]
		{
			RawOption("exec-sell-1", "Sell", ExecMs(2023, 12, 28, 11, 0), execFee: "-0.00000001", feeCurrency: "BTC"),
		};

		// Act
		var trades = materializer.Materialize(rawExecutions).Trades;

		// Assert
		Assert.That(trades.Count, Is.EqualTo(1));
		Assert.That(trades[0].Quantity, Is.EqualTo(-0.0001m));
		Assert.That(trades[0].Fee, Is.EqualTo(-0.00000001m));
		Assert.That(trades[0].FeeCurrency, Is.EqualTo("BTC"));
	}

	[TestMethod]
	[Description("Повторная материализация того же сырья не меняет «Входящие»")]
	public void TryIfRepeatMaterializationKeepsInboxUnchanged()
	{
		// Arrange: повторный синк без новых записей отдаёт то же сырье хранилища.
		// Требование: повторная загрузка известных записей не создаёт дубликатов
		// доменных сущностей и не меняет прежние «Входящие».
		// Traceability: openspec:sync/bybit-history#scenario-repeat-sync-no-duplicates
		var materializer = CreateMaterializer();
		var rawExecutions = new[]
		{
			RawOption("exec-1", "Buy", ExecMs(2023, 12, 28, 10, 0)),
			Raw("exec-2", "linear", "BTCUSDT", ExecMs(2023, 12, 28, 10, 30),
				ExecutionPayload("exec-2", "BTCUSDT", "Sell", "42000", "0.01", "0.0042", "USDT", ExecMs(2023, 12, 28, 10, 30))),
		};

		// Act: вторая материализация получает те же записи, включая идентичный дубликат.
		var firstPass = materializer.Materialize(rawExecutions).Trades;
		var secondPass = materializer.Materialize(rawExecutions.Append(rawExecutions[0])).Trades;

		// Assert: количество и состав сделок не изменились.
		Assert.That(secondPass.Count, Is.EqualTo(2));
		Assert.That(secondPass.ToArray(), Is.EqualTo(firstPass.ToArray()));
	}

	[TestMethod]
	[Description("Линейная сделка не получает атрибуты опциона")]
	public void TryIfLinearTradeHasNoOptionAttributes()
	{
		// Arrange: запись категории linear не сверяется со справочником как опцион.
		var materializer = CreateMaterializer();
		var rawExecutions = new[]
		{
			Raw("exec-linear-1", "linear", "BTCUSDT", ExecMs(2023, 12, 28, 10, 30),
				ExecutionPayload("exec-linear-1", "BTCUSDT", "Buy", "42000", "0.01", "0.0042", "USDT",
					ExecMs(2023, 12, 28, 10, 30))),
		};

		// Act
		var trades = materializer.Materialize(rawExecutions).Trades;

		// Assert
		Assert.That(trades.Count, Is.EqualTo(1));
		Assert.That(trades[0].Category, Is.EqualTo("linear"));
		Assert.That(trades[0].Quantity, Is.EqualTo(0.01m));
		Assert.That(trades[0].Option, Is.Null);
	}

	[TestMethod]
	[Description("Сделки упорядочены по времени исполнения, при равенстве — по execId")]
	public void TryIfTradesOrderedByExecutionTimeThenExecId()
	{
		// Arrange: записи поданы в обратном порядке, две — с одинаковым временем.
		var materializer = CreateMaterializer();
		var rawExecutions = new[]
		{
			RawOption("exec-3", "Buy", ExecMs(2023, 12, 28, 12, 0)),
			RawOption("exec-2b", "Sell", ExecMs(2023, 12, 28, 11, 0)),
			RawOption("exec-2a", "Buy", ExecMs(2023, 12, 28, 11, 0)),
		};

		// Act
		var trades = materializer.Materialize(rawExecutions).Trades;

		// Assert: порядок результата не зависит от порядка входных записей.
		Assert.That(
			trades.Select(trade => trade.ExecId).ToArray(),
			Is.EqualTo(new[] { "exec-2a", "exec-2b", "exec-3" }));
	}

	[TestMethod]
	[Description("Запись Funding с заполненными торговыми атрибутами не материализуется сделкой")]
	public void TryIfFundingRecordIsNotMaterializedAsTrade()
	{
		// Фандинг linear-инструмента приходит с заполненными количеством, ценой
		// по котировк-цене и валидной стороной, но сделкой «Входящих» не является:
		// запись остаётся в сырье, сделку из неё журнал не выводит, а материализация
		// завершается без неразрешённых символов, ошибок и исключений.
		// Traceability: openspec:sync/bybit-history#requirement-non-trade-executions-are-not-trades
		// Traceability: openspec:sync/bybit-history#scenario-funding-record-is-not-a-trade
		// Arrange
		var materializer = CreateMaterializer();
		var execTimeMs = ExecMs(2023, 12, 28, 10, 0);
		var rawExecutions = new[]
		{
			Raw("exec-funding-1", "linear", "BTCUSDT", execTimeMs,
				ExecutionPayload("exec-funding-1", "BTCUSDT", "Buy", "42000", "0.01", "0.0042", "USDT", execTimeMs,
					execType: "Funding")),
		};

		// Act
		var result = materializer.Materialize(rawExecutions);

		// Assert: сделки нет, запись не отложена по символу.
		Assert.That(result.Trades, Is.Empty);
		Assert.That(result.UnresolvedSymbols, Is.Empty);
	}

	[TestMethod]
	[DataRow("AdlTrade")]
	[DataRow("")]
	[Description("Запись не-Trade типа исполнения пропускается без ошибок, соседняя Trade-запись материализуется")]
	public void TryIfNonTradeExecTypeIsSkippedAndNeighborTradeMaterialized(string execType)
	{
		// Неизвестный или пустой тип исполнения журнал не относит к сделкам: запись
		// пропускается тихо — без ошибки, предупреждения и неразрешённых символов, —
		// а соседняя Trade-запись того же вызова материализуется как прежде.
		// Traceability: openspec:sync/bybit-history#scenario-unknown-exec-type-is-not-a-trade
		// Arrange
		var materializer = CreateMaterializer();
		var execTimeMs = ExecMs(2023, 12, 28, 10, 0);
		var rawExecutions = new[]
		{
			Raw("exec-non-trade-1", "linear", "BTCUSDT", execTimeMs,
				ExecutionPayload("exec-non-trade-1", "BTCUSDT", "Buy", "42000", "0.01", "0.0042", "USDT", execTimeMs,
					execType: execType)),
			RawOption("exec-trade-1", "Buy", ExecMs(2023, 12, 28, 10, 30)),
		};

		// Act
		var result = materializer.Materialize(rawExecutions);

		// Assert: материализована только Trade-запись.
		Assert.That(result.Trades.Select(trade => trade.ExecId).ToList(), Is.EqualTo(new[] { "exec-trade-1" }));
		Assert.That(result.UnresolvedSymbols, Is.Empty);
	}

	[TestMethod]
	[Description("Повторная материализация набора Trade- и Funding-записей даёт идентичный результат")]
	public void TryIfRepeatMaterializationOfTradeAndFundingSetIsIdentical()
	{
		// Повторный синк отдаёт то же сырье, включая Funding-записи: результат
		// материализации детерминирован — сделки не задваиваются, фандинг не даёт
		// сделок, неразрешённых символов и ошибок нет.
		// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
		// Arrange
		var materializer = CreateMaterializer();
		var tradeExecTimeMs = ExecMs(2023, 12, 28, 10, 0);
		var fundingExecTimeMs = ExecMs(2023, 12, 28, 10, 30);
		var rawExecutions = new[]
		{
			Raw("exec-1", "linear", "BTCUSDT", tradeExecTimeMs,
				ExecutionPayload("exec-1", "BTCUSDT", "Buy", "42000", "0.01", "0.0042", "USDT", tradeExecTimeMs)),
			Raw("exec-funding-1", "linear", "BTCUSDT", fundingExecTimeMs,
				ExecutionPayload("exec-funding-1", "BTCUSDT", "Buy", "42050", "0.01", "0", "USDT", fundingExecTimeMs,
					execType: "Funding")),
		};

		// Act: вторая материализация получает тот же набор, включая идентичный дубликат Trade-записи.
		var firstPass = materializer.Materialize(rawExecutions);
		var secondPass = materializer.Materialize(rawExecutions.Append(rawExecutions[0]));

		// Assert: результат идентичен, единственная сделка — от Trade-записи.
		Assert.That(firstPass.Trades.Select(trade => trade.ExecId).ToList(), Is.EqualTo(new[] { "exec-1" }));
		Assert.That(secondPass.Trades.ToArray(), Is.EqualTo(firstPass.Trades.ToArray()));
		Assert.That(secondPass.UnresolvedSymbols, Is.Empty);
	}

	[TestMethod]
	[Description("Конфликт Trade-записей с одним execId роняет материализацию, Funding-двойник дубликата не создаёт")]
	public void TryIfFundingTwinOfTradeExecIdCreatesNoDuplicateWhileTradeConflictStillFails()
	{
		// Два разных Trade-payload с одним execId — конфликт источника: материализация
		// останавливается ошибкой, правило не ослаблено фильтром типов. Funding-запись
		// с тем же execId, что у Trade-записи, сделкой не становится и дубликата
		// не создаёт: единственная сделка остаётся от Trade-записи.
		// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
		// Arrange
		var materializer = CreateMaterializer();
		var execTimeMs = ExecMs(2023, 12, 28, 10, 0);
		var conflictingTrades = new[]
		{
			Raw("exec-dup", "linear", "BTCUSDT", execTimeMs,
				ExecutionPayload("exec-dup", "BTCUSDT", "Buy", "42000", "0.01", "0.0042", "USDT", execTimeMs)),
			Raw("exec-dup", "linear", "BTCUSDT", execTimeMs,
				ExecutionPayload("exec-dup", "BTCUSDT", "Sell", "42000", "0.01", "0.0042", "USDT", execTimeMs)),
		};

		// Act — Assert: конфликт Trade-записей по-прежнему останавливает материализацию.
		Assert.Throws<TradeMaterializationException>(() => materializer.Materialize(conflictingTrades));

		// Arrange: Funding-двойник отличается от Trade-записи ценой по котировк-цене.
		var tradeWithFundingTwin = new[]
		{
			Raw("exec-dup", "linear", "BTCUSDT", execTimeMs,
				ExecutionPayload("exec-dup", "BTCUSDT", "Buy", "42000", "0.01", "0.0042", "USDT", execTimeMs)),
			Raw("exec-dup", "linear", "BTCUSDT", execTimeMs,
				ExecutionPayload("exec-dup", "BTCUSDT", "Buy", "42050", "0.01", "0", "USDT", execTimeMs,
					execType: "Funding")),
		};

		// Act
		var result = materializer.Materialize(tradeWithFundingTwin);

		// Assert: сделка одна — от Trade-записи; Funding-двойник дубликата не создал.
		Assert.That(result.Trades.Select(trade => trade.ExecId).ToList(), Is.EqualTo(new[] { "exec-dup" }));
		Assert.That(result.Trades[0].Price, Is.EqualTo(42000m));
		Assert.That(result.UnresolvedSymbols, Is.Empty);
	}

	[TestMethod]
	[Description("Null-коллекция сырых записей отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullRawExecutions()
	{
		// Arrange
		var materializer = CreateMaterializer();

		// Act — Assert
		materializer.Materialize(null!);
	}

	[TestMethod]
	[Description("Null-сверщик инструментов отклоняется конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullInstrumentResolver()
	{
		// Arrange — Act — Assert
		new TradeMaterializer(null!);
	}

	[TestMethod]
	[Description("Некорректный JSON полезной нагрузки останавливает материализацию")]
	[ExpectedException(typeof(TradeMaterializationException))]
	public void ThrowOnMalformedPayloadJson()
	{
		// Arrange: сырое хранилище гарантий на форму JSON не даёт — повреждённая запись
		// должна останавливать материализацию с внятной ошибкой, а не пропускаться молча.
		var materializer = CreateMaterializer();
		var rawExecutions = new[] { Raw("exec-broken", "option", "BTC-29DEC23-45000-C", 0, "{ не json") };

		// Act — Assert
		materializer.Materialize(rawExecutions);
	}

	[TestMethod]
	[DataRow("Long")]
	[DataRow("")]
	[Description("Неизвестная сторона исполнения отклоняется")]
	[ExpectedException(typeof(TradeMaterializationException))]
	public void ThrowOnUnknownSide(string side)
	{
		// Arrange: сторона исполнения вне пары Buy/Sell не даёт знака количества.
		var materializer = CreateMaterializer();
		var execTimeMs = ExecMs(2023, 12, 28, 10, 0);
		var rawExecutions = new[]
		{
			Raw("exec-side", "option", "BTC-29DEC23-45000-C", execTimeMs,
				ExecutionPayload("exec-side", "BTC-29DEC23-45000-C", side, "45000", "0.0001", "0.01", "USDC", execTimeMs)),
		};

		// Act — Assert
		materializer.Materialize(rawExecutions);
	}

	[TestMethod]
	[DataRow("execPrice")]
	[DataRow("execQty")]
	[Description("Отсутствующая цена или количество исполнения отклоняется")]
	[ExpectedException(typeof(TradeMaterializationException))]
	public void ThrowOnMissingRequiredNumber(string missingField)
	{
		// Arrange: биржа кодирует отсутствующее число пустой строкой; сделка без
		// цены или количества не может попасть во «Входящие».
		var materializer = CreateMaterializer();
		var execTimeMs = ExecMs(2023, 12, 28, 10, 0);
		var execPrice = missingField == "execPrice" ? "" : "45000";
		var execQty = missingField == "execQty" ? "" : "0.0001";
		var rawExecutions = new[]
		{
			Raw("exec-empty", "option", "BTC-29DEC23-45000-C", execTimeMs,
				ExecutionPayload("exec-empty", "BTC-29DEC23-45000-C", "Buy", execPrice, execQty, "0.01", "USDC", execTimeMs)),
		};

		// Act — Assert
		materializer.Materialize(rawExecutions);
	}

	[TestMethod]
	[Description("Расхождение execId строки хранилища и полезной нагрузки останавливает материализацию")]
	[ExpectedException(typeof(TradeMaterializationException))]
	public void ThrowOnExecIdMismatchBetweenRowAndPayload()
	{
		// Arrange: execId — ключ дедупликации; расхождение строки и payload
		// означает повреждение сырья.
		var materializer = CreateMaterializer();
		var execTimeMs = ExecMs(2023, 12, 28, 10, 0);
		var rawExecutions = new[]
		{
			Raw("exec-row", "option", "BTC-29DEC23-45000-C", execTimeMs,
				ExecutionPayload("exec-payload", "BTC-29DEC23-45000-C", "Buy", "45000", "0.0001", "0.01", "USDC", execTimeMs)),
		};

		// Act — Assert
		materializer.Materialize(rawExecutions);
	}

	[TestMethod]
	[Description("Конфликтующие записи с одним execId останавливают материализацию")]
	[ExpectedException(typeof(TradeMaterializationException))]
	public void ThrowOnConflictingDuplicateExecId()
	{
		// Arrange: два разных payload с одним execId — источник записей с одним
		// идентификатором обязан быть единственным.
		var materializer = CreateMaterializer();
		var execTimeMs = ExecMs(2023, 12, 28, 10, 0);
		var rawExecutions = new[]
		{
			Raw("exec-conflict", "option", "BTC-29DEC23-45000-C", execTimeMs,
				ExecutionPayload("exec-conflict", "BTC-29DEC23-45000-C", "Buy", "45000", "0.0001", "0.01", "USDC", execTimeMs)),
			Raw("exec-conflict", "option", "BTC-29DEC23-45000-C", execTimeMs,
				ExecutionPayload("exec-conflict", "BTC-29DEC23-45000-C", "Sell", "45000", "0.0001", "0.01", "USDC", execTimeMs)),
		};

		// Act — Assert
		materializer.Materialize(rawExecutions);
	}

	[TestMethod]
	[Description("Символ опциона без спецификации с недоставленной доской откладывает запись и попадает в перечень неразрешённых")]
	public void TryIfUnknownSymbolSkipsTradeAndReportsSymbol()
	{
		// Arrange: запись исполнения опциона ETH, чьей спецификации нет в справочнике;
		// часы стоят до доставки доски 29DEC23 — инструмент живой, его спецификация
		// обязана попасть в справочник синком, разбор строки символа с доверием ему
		// запрещён.
		// Требование: символ без спецификации не прерывает материализацию — сделка
		// откладывается, символ перечислен в результате, остальные сделки строятся.
		// Traceability: openspec:sync/bybit-history#scenario-unresolved-symbol-degrades-to-warning
		var materializer = CreateMaterializer(UndeliveredEthBoardClock());
		var execTimeMs = ExecMs(2023, 12, 28, 10, 0);
		var rawExecutions = new[]
		{
			Raw("exec-eth", "option", "ETH-29DEC23-2000-C", execTimeMs,
				ExecutionPayload("exec-eth", "ETH-29DEC23-2000-C", "Buy", "200", "1", "0.02", "USDC", execTimeMs)),
			RawOption("exec-btc", "Buy", execTimeMs),
		};

		// Act
		var result = materializer.Materialize(rawExecutions);

		// Assert: сделка с неизвестным символом не материализована, символ перечислен
		// в стабильной форме; сделка разрешимого инструмента построена как обычно.
		Assert.That(result.UnresolvedSymbols, Is.EqualTo(new[] { "ETH-29DEC23-2000-C" }));
		Assert.That(result.Trades.Select(trade => trade.ExecId).ToList(), Is.EqualTo(new[] { "exec-btc" }));
	}

	[TestMethod]
	[Description("Повторные записи с тем же символом недоставленной доски не дублируют перечень")]
	public void TryIfRepeatedUnknownSymbolsReportedOnce()
	{
		// Arrange: две записи опциона без спецификации в справочнике при недоставленной
		// доске — перечень символов не должен содержать повторов.
		var materializer = CreateMaterializer(UndeliveredEthBoardClock());
		var execTimeMs = ExecMs(2023, 12, 28, 10, 0);
		var rawExecutions = new[]
		{
			Raw("exec-eth-1", "option", "ETH-29DEC23-2000-C", execTimeMs,
				ExecutionPayload("exec-eth-1", "ETH-29DEC23-2000-C", "Buy", "200", "1", "0.02", "USDC", execTimeMs)),
			Raw("exec-eth-2", "option", "ETH-29DEC23-2000-C", execTimeMs,
				ExecutionPayload("exec-eth-2", "ETH-29DEC23-2000-C", "Sell", "210", "1", "0.02", "USDC", execTimeMs)),
		};

		// Act
		var result = materializer.Materialize(rawExecutions);

		// Assert: символ перечислен один раз, сделок по нему нет.
		Assert.That(result.UnresolvedSymbols, Is.EqualTo(new[] { "ETH-29DEC23-2000-C" }));
		Assert.That(result.Trades, Is.Empty);
	}

	[TestMethod]
	[Description("Делистинговый символ с доставленной доской материализуется из частей символа без перечня неразрешённых")]
	public void TryIfDelistedSymbolExecutionMaterializesFromSymbol()
	{
		// Arrange: запись исполнения опциона ETH, чьей спецификации нет в справочнике,
		// а доска 29DEC23 08:00 UTC уже доставлена — биржа спецификацию делистингового
		// инструмента больше не отдаёт (отказ «контракт недоступен»).
		// Требование: истёкшая доска разрешается из символа — сделка материализуется
		// со спецификацией, выведенной из символа, и символ не попадает в перечень
		// неразрешённых.
		// Traceability: openspec:sync/bybit-history#scenario-delisted-option-resolves-from-symbol
		var materializer = CreateMaterializer();
		var execTimeMs = ExecMs(2023, 12, 28, 10, 0);
		var rawExecutions = new[]
		{
			Raw("exec-eth", "option", "ETH-29DEC23-2000-C", execTimeMs,
				ExecutionPayload("exec-eth", "ETH-29DEC23-2000-C", "Buy", "200", "1", "0.02", "USDC", execTimeMs)),
		};

		// Act
		var result = materializer.Materialize(rawExecutions);

		// Assert: сделка построена, канонические атрибуты выведены из символа,
		// перечень неразрешённых пуст.
		Assert.That(result.UnresolvedSymbols, Is.Empty);
		Assert.That(result.Trades.Count, Is.EqualTo(1));
		var trade = result.Trades[0];
		Assert.That(trade.ExecId, Is.EqualTo("exec-eth"));
		Assert.That(trade.Symbol, Is.EqualTo("ETH-29DEC23-2000-C"));
		Assert.That(trade.Option, Is.Not.Null);
		Assert.That(trade.Option!.BaseCoin, Is.EqualTo("ETH"));
		Assert.That(trade.Option.OptionsType, Is.EqualTo(OptionType.Call));
		Assert.That(trade.Option.Strike, Is.EqualTo(2000d));
		Assert.That(trade.Option.DeliveryTime, Is.EqualTo(new DateTimeOffset(2023, 12, 29, 8, 0, 0, TimeSpan.Zero)));
	}

	[TestMethod]
	[Description("Расхождение базового актива со справочником по-прежнему останавливает материализацию ошибкой сверки")]
	public void ThrowOnBaseCoinMismatchStillFails()
	{
		// Arrange: справочник хранит для символа чужой базовый актив — это конфликт
		// записи со справочником, а не отсутствие спецификации: замалчивание
		// расхождения скрыло бы подмену инструмента.
		// Требование: расхождение символа со справочником остаётся ошибкой сверки,
		// а не предупреждением деградации.
		// Traceability: openspec:sync/bybit-history#scenario-instrument-mismatch-still-fails
		var deliveryMs = OptionDelivery.ToUnixTimeMilliseconds();
		var mismatchedPayload =
			$$"""{"symbol":"BTC-29DEC23-45000-C","baseCoin":"ETH","quoteCoin":"USD","settleCoin":"USDC","status":"Trading","optionsType":"Call","deliveryTime":"{{deliveryMs}}","deliveryFeeRate":"0.00015"}""";
		var resolver = new InstrumentResolver(new InstrumentCatalog(new[]
		{
			new RawInstrument
			{
				Symbol = "BTC-29DEC23-45000-C",
				Category = "option",
				PayloadJson = mismatchedPayload,
				FetchedAt = FetchedAt,
			},
		}));
		var materializer = new TradeMaterializer(resolver);

		// Act — Assert
		Assert.Throws<InstrumentResolveException>(
			() => materializer.Materialize(new[] { RawOption("exec-btc", "Buy", ExecMs(2023, 12, 28, 10, 0)) }));
	}

	[TestMethod]
	[Description("Ручная сделка материализуется с источником «ручной ввод» и без execId")]
	public void TryIfManualTradeMaterializedWithSourceAndNoExecId()
	{
		// Arrange: ручная сделка сырья — покупка колла и продажа перпа, обе внесены
		// пользователем. Требование: ручная запись становится сделкой журнала с
		// признаком источника «ручной ввод», execId у неё отсутствует, атрибуты
		// повторяют ввод, а атрибуты опциона выведены сверкой со справочником.
		// Traceability: openspec:domain/manual-trades#scenario-manual-trade-source-attribute
		// Traceability: openspec:domain/manual-trades#requirement-manual-trade-entry
		var materializer = CreateMaterializer();
		var executedAt = new DateTimeOffset(2023, 12, 28, 12, 0, 0, TimeSpan.Zero);
		var manualTrades = new[]
		{
			Manual(1, "BTC-29DEC23-45000-C", "Buy", 0.0001m, 44000m, 0.01m, executedAt),
			Manual(2, "BTCUSDT", "Sell", 0.01m, 42000m, 0.0042m, executedAt.AddMinutes(30)),
		};

		// Act
		var trades = materializer.Materialize(Array.Empty<RawExecution>(), manualTrades).Trades;

		// Assert: обе ручные сделки в проекции; атрибуты повторяют ввод пользователя.
		Assert.That(trades.Count, Is.EqualTo(2));
		var optionTrade = trades[0];
		Assert.That(optionTrade.Source, Is.EqualTo(TradeSource.Manual));
		Assert.That(optionTrade.ExecId, Is.Null);
		Assert.That(optionTrade.ManualTradeId, Is.EqualTo(1));
		Assert.That(optionTrade.ConstructionId, Is.Null);
		Assert.That(optionTrade.Category, Is.EqualTo("option"));
		Assert.That(optionTrade.Symbol, Is.EqualTo("BTC-29DEC23-45000-C"));
		Assert.That(optionTrade.ExecutedAt, Is.EqualTo(executedAt));
		Assert.That(optionTrade.Quantity, Is.EqualTo(0.0001m));
		Assert.That(optionTrade.Price, Is.EqualTo(44000m));
		Assert.That(optionTrade.Fee, Is.EqualTo(0.01m));
		Assert.That(optionTrade.FeeCurrency, Is.EqualTo("USDT"));
		Assert.That(optionTrade.IsMaker, Is.False);
		Assert.That(optionTrade.Option, Is.Not.Null);
		Assert.That(optionTrade.Option!.Strike, Is.EqualTo(45000m));
		Assert.That(optionTrade.Option.DeliveryTime, Is.EqualTo(OptionDelivery));

		var linearTrade = trades[1];
		Assert.That(linearTrade.Source, Is.EqualTo(TradeSource.Manual));
		Assert.That(linearTrade.ExecId, Is.Null);
		Assert.That(linearTrade.Category, Is.EqualTo("linear"));
		// Продажа знакует количество минусом, как у биржевой стороны Sell.
		Assert.That(linearTrade.Quantity, Is.EqualTo(-0.01m));
		Assert.That(linearTrade.Option, Is.Null);
	}

	[TestMethod]
	[Description("Сделки обоих источников попадают в одну проекцию наравне друг с другом")]
	public void TryIfBothSourcesMaterializedInOneProjection()
	{
		// Arrange: биржевая запись исполнения и ручная сделка по одному инструменту
		// поданы одним вызовом. Требование: оба источника материализуются в общую
		// проекцию — биржевая сохраняет execId и источник «биржа», ручная не имеет
		// execId и помечена источником «ручной ввод»; при равном времени биржевые
		// записи идут раньше ручных, ручные упорядочены идентификатором сырья.
		// Traceability: openspec:domain/manual-trades#requirement-manual-trade-entry
		// Traceability: change:manual-journal-management/design#d1
		var materializer = CreateMaterializer();
		var sameMoment = ExecMs(2023, 12, 28, 10, 0);
		var rawExecutions = new[] { RawOption("exec-1", "Buy", sameMoment) };
		var manualTrades = new[]
		{
			Manual(2, "BTC-29DEC23-45000-C", "Buy", 0.0001m, 43000m, 0m, new DateTimeOffset(2023, 12, 28, 10, 0, 0, TimeSpan.Zero)),
			Manual(1, "BTC-29DEC23-45000-C", "Sell", 0.0001m, 46000m, 0m, new DateTimeOffset(2023, 12, 28, 10, 0, 0, TimeSpan.Zero)),
		};

		// Act
		var trades = materializer.Materialize(rawExecutions, manualTrades).Trades;

		// Assert: обе сделки в одной проекции; порядок детерминирован — биржевая
		// первой, ручные по возрастанию идентификатора сырья.
		Assert.That(trades.Count, Is.EqualTo(3));
		Assert.That(trades[0].Source, Is.EqualTo(TradeSource.Exchange));
		Assert.That(trades[0].ExecId, Is.EqualTo("exec-1"));
		Assert.That(trades[1].Source, Is.EqualTo(TradeSource.Manual));
		Assert.That(trades[1].ManualTradeId, Is.EqualTo(1));
		Assert.That(trades[2].Source, Is.EqualTo(TradeSource.Manual));
		Assert.That(trades[2].ManualTradeId, Is.EqualTo(2));
	}

	[TestMethod]
	[Description("Ручная сделка по неизвестному символу откладывается единым с биржевой деградацией")]
	public void TryIfManualUnknownOptionSymbolDefersTrade()
	{
		// Arrange: ручная сделка по опциону ETH без спецификации в справочнике при
		// недоставленной доске и ручная сделка разрешимого инструмента. Требование:
		// деградация неразрешённого символа едина для обоих источников — запись
		// откладывается, символ перечислен, остальные сделки строятся.
		// Traceability: openspec:sync/bybit-history#scenario-unresolved-symbol-degrades-to-warning
		var materializer = CreateMaterializer(UndeliveredEthBoardClock());
		var executedAt = new DateTimeOffset(2023, 12, 28, 10, 0, 0, TimeSpan.Zero);
		var manualTrades = new[]
		{
			Manual(1, "ETH-29DEC23-2000-C", "Buy", 1m, 200m, 0m, executedAt),
			Manual(2, "BTC-29DEC23-45000-C", "Buy", 0.0001m, 45000m, 0m, executedAt),
		};

		// Act
		var result = materializer.Materialize(Array.Empty<RawExecution>(), manualTrades);

		// Assert: отложена только сделка неизвестного символа.
		Assert.That(result.UnresolvedSymbols, Is.EqualTo(new[] { "ETH-29DEC23-2000-C" }));
		Assert.That(result.Trades.Select(trade => trade.ManualTradeId).ToList(), Is.EqualTo(new long?[] { 2 }));
	}

	[TestMethod]
	[Description("Повторная материализация того же ручного сырья не создаёт дубликатов")]
	public void TryIfRepeatedManualMaterializationIsIdempotent()
	{
		// Arrange: повторный переразбор отдаёт те же строки ручного сырья. Требование:
		// идентичность ручной записи несёт идентификатор строки сырья, повторная
		// материализация не задваивает сделку.
		// Traceability: openspec:domain/manual-trades#requirement-manual-trade-full-member
		var materializer = CreateMaterializer();
		var manualTrade = Manual(7, "BTCUSDT", "Buy", 0.01m, 42000m, 0.0042m, new DateTimeOffset(2023, 12, 28, 10, 0, 0, TimeSpan.Zero));

		// Act: второй вызов получает ту же строку дважды.
		var firstPass = materializer.Materialize(Array.Empty<RawExecution>(), new[] { manualTrade }).Trades;
		var secondPass = materializer.Materialize(Array.Empty<RawExecution>(), new[] { manualTrade, manualTrade }).Trades;

		// Assert: состав и порядок сделок не изменились.
		Assert.That(firstPass.Count, Is.EqualTo(1));
		Assert.That(secondPass.ToArray(), Is.EqualTo(firstPass.ToArray()));
	}

	[TestMethod]
	[Description("Конфликтующие ручные записи с одним идентификатором сырья останавливают материализацию")]
	[ExpectedException(typeof(TradeMaterializationException))]
	public void ThrowOnConflictingManualTradesWithSameId()
	{
		// Arrange: две разные строки с одним идентификатором — источник записей
		// с одним ключом обязан быть единственным, как и у биржевых execId.
		var materializer = CreateMaterializer();
		var executedAt = new DateTimeOffset(2023, 12, 28, 10, 0, 0, TimeSpan.Zero);

		// Act — Assert
		materializer.Materialize(Array.Empty<RawExecution>(), new[]
		{
			Manual(5, "BTCUSDT", "Buy", 0.01m, 42000m, 0m, executedAt),
			Manual(5, "BTCUSDT", "Sell", 0.01m, 42000m, 0m, executedAt),
		});
	}

	[TestMethod]
	[DataRow("Long")]
	[DataRow("")]
	[Description("Неизвестная сторона ручной сделки отклоняется")]
	[ExpectedException(typeof(TradeMaterializationException))]
	public void ThrowOnUnknownManualSide(string side)
	{
		// Arrange: сторона ручной сделки вне пары Buy/Sell не даёт знака количества.
		var materializer = CreateMaterializer();

		// Act — Assert
		materializer.Materialize(Array.Empty<RawExecution>(), new[]
		{
			Manual(3, "BTCUSDT", side, 0.01m, 42000m, 0m, new DateTimeOffset(2023, 12, 28, 10, 0, 0, TimeSpan.Zero)),
		});
	}

	[TestMethod]
	[Description("Null-строка ручного сырья отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullManualTradeRow()
	{
		// Arrange — Act — Assert
		CreateMaterializer().Materialize(Array.Empty<RawExecution>(), new ManualTrade?[] { null! });
	}

	#region Помощники

	private static TradeMaterializer CreateMaterializer() => new(new InstrumentResolver(CreateCatalog()));

	private static TradeMaterializer CreateMaterializer(TimeProvider timeProvider) =>
		new(new InstrumentResolver(CreateCatalog(), timeProvider));

	/// <summary>Часы до доставки доски ETH 29DEC23 08:00 UTC: символ без спецификации обязан деградировать.</summary>
	private static FixedTimeProvider UndeliveredEthBoardClock() => new(new DateTimeOffset(2023, 12, 1, 0, 0, 0, TimeSpan.Zero));

	/// <summary>Справочник инструментов: опцион BTC с delivery 29DEC23 08:00 UTC и линейный перп BTCUSDT.</summary>
	private static InstrumentCatalog CreateCatalog()
	{
		var deliveryMs = OptionDelivery.ToUnixTimeMilliseconds();
		var optionPayload =
			$$"""{"symbol":"BTC-29DEC23-45000-C","baseCoin":"BTC","quoteCoin":"USD","settleCoin":"USDC","status":"Trading","optionsType":"Call","deliveryTime":"{{deliveryMs}}","deliveryFeeRate":"0.00015"}""";
		var linearPayload =
			"""{"symbol":"BTCUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"BTC","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""";
		return new InstrumentCatalog(new[]
		{
			new RawInstrument
			{
				Symbol = "BTC-29DEC23-45000-C",
				Category = "option",
				PayloadJson = optionPayload,
				FetchedAt = FetchedAt,
			},
			new RawInstrument
			{
				Symbol = "BTCUSDT",
				Category = "linear",
				PayloadJson = linearPayload,
				FetchedAt = FetchedAt,
			},
		});
	}

	private static RawExecution RawOption(
		string execId,
		string side,
		long execTimeMs,
		string execFee = "0.01",
		string feeCurrency = "USDC") => Raw(
		execId, "option", "BTC-29DEC23-45000-C", execTimeMs,
		ExecutionPayload(execId, "BTC-29DEC23-45000-C", side, "45000", "0.0001", execFee, feeCurrency, execTimeMs));

	/// <summary>Ручная сделка сырья с атрибутами пользовательского ввода; комиссия задаётся в USDT.</summary>
	private static ManualTrade Manual(
		long id,
		string symbol,
		string side,
		decimal quantity,
		decimal price,
		decimal feeUsdt,
		DateTimeOffset executedAt,
		long? constructionId = null) => new()
	{
		Id = id,
		ExecutedAt = executedAt,
		Symbol = symbol,
		Side = side,
		Quantity = quantity,
		Price = price,
		FeeUsdt = feeUsdt,
		ConstructionId = constructionId,
		CreatedAt = FetchedAt,
		UpdatedAt = FetchedAt,
	};

	private static RawExecution Raw(string execId, string category, string symbol, long execTimeMs, string payloadJson) => new()
	{
		ExecId = execId,
		Category = category,
		Symbol = symbol,
		ExecTimeMs = execTimeMs,
		PayloadJson = payloadJson,
		FetchedAt = FetchedAt,
	};

	/// <summary>
	/// Запись исполнения в форме ответа execution-list: числа биржа шлёт строками.
	/// Тип исполнения по умолчанию Trade — торговые записи составляют большинство синка;
	/// не-Trade типы тесты фильтра задают явно.
	/// </summary>
	private static string ExecutionPayload(
		string execId,
		string symbol,
		string side,
		string execPrice,
		string execQty,
		string execFee,
		string? feeCurrency,
		long execTimeMs,
		bool isMaker = false,
		string execType = "Trade")
	{
		var feeCurrencyJson = feeCurrency is null ? "null" : $"\"{feeCurrency}\"";
		var isMakerJson = isMaker ? "true" : "false";
		return $$"""{"symbol":"{{symbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"{{execFee}}","execId":"{{execId}}","execPrice":"{{execPrice}}","execQty":"{{execQty}}","execType":"{{execType}}","execTime":"{{execTimeMs}}","feeCurrency":{{feeCurrencyJson}},"isMaker":{{isMakerJson}}}""";
	}

	private static long ExecMs(int year, int month, int day, int hour, int minute) =>
		new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	#endregion
}
