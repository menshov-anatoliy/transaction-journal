using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Data;
using TransactionJournal.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Materialization;

/// <summary>
/// Проверки фасада полного переразбора журнала: доменные представления (сделки
/// «Входящих», закрывающие записи экспираций, предупреждения сверки) строятся
/// только из локальных сырых записей без сетевых запросов, а пересборка после
/// изменения правила разбора даёт согласованную проекцию без следов прежнего правила.
/// </summary>
[TestClass]
public class JournalMaterializerTests
{
	private const string CallSymbol = "BTC-29DEC23-45000-C";

	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	/// <summary>Каноническое время delivery справочника: 29DEC23 08:00 UTC.</summary>
	private static readonly DateTimeOffset OptionDelivery = new(2023, 12, 29, 8, 0, 0, TimeSpan.Zero);

	private static readonly long OptionDeliveryMs = OptionDelivery.ToUnixTimeMilliseconds();

	[TestMethod]
	[Description("Пересборка после изменения правила разбора даёт согласованный результат без сетевых запросов")]
	public void TryIfRebuildAfterParseRuleChangeGivesConsistentResult()
	{
		// Arrange: сырьё хранилища — справочник (колл BTC и линейный перп), исполнения
		// с комиссией в USDC и одна линейная сделка с комиссией в USDT, плюс ITM
		// delivery-запись с расходящимся deliveryRpl. Первый переразбор идёт по правилу
		// ADR-0001 (USDC ≡ USDT), затем правило разбора меняется: паритет отключён,
		// валюта комиссии остаётся биржевой. Фасад принимает только сырые записи —
		// сетевых запросов при переразборе нет по построению.
		// Требование: система способна перестроить доменные сущности из локального
		// хранилища сырых записей по изменённым правилам без обращения к Bybit.
		// Traceability: openspec:sync/bybit-history#scenario-raw-records-persisted-for-reparse
		// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
		var rawInstruments = CreateRawInstruments();
		var rawExecutions = CreateRawExecutions();
		var rawDeliveries = CreateRawDeliveries();
		var assignments = new Dictionary<string, string?> { ["exec-opt-1"] = "con-a" };
		var defaultRuleMaterializer = new JournalMaterializer();
		var changedRuleMaterializer = new JournalMaterializer(new KeepRawFeeCurrencyRule());

		// Act: первая сборка по действующему правилу.
		var beforeRuleChange = defaultRuleMaterializer.Materialize(rawInstruments, rawExecutions, rawDeliveries, assignments, AfterDelivery());

		// Assert: проекция полна — «Входящие», закрывающие записи и предупреждение сверки
		// выведены из сырья; USDC-комиссии приведены к USDT по ADR-0001.
		Assert.That(beforeRuleChange.InboxTrades.Count, Is.EqualTo(3));
		Assert.That(beforeRuleChange.InboxTrades.Single(trade => trade.ExecId == "exec-opt-1").FeeCurrency, Is.EqualTo("USDT"));
		Assert.That(beforeRuleChange.InboxTrades.Single(trade => trade.ExecId == "exec-opt-2").FeeCurrency, Is.EqualTo("USDT"));
		Assert.That(beforeRuleChange.InboxTrades.Single(trade => trade.ExecId == "exec-lin-1").FeeCurrency, Is.EqualTo("USDT"));
		Assert.That(beforeRuleChange.ExpiryClosingEntries.Count, Is.EqualTo(2));
		Assert.That(beforeRuleChange.ExpiryClosingEntries.All(entry => entry.EffectivePrice == 1000m), Is.True);
		Assert.That(beforeRuleChange.ReconciliationWarnings.Count, Is.EqualTo(1));
		Assert.That(beforeRuleChange.ReconciliationWarnings[0].Difference, Is.EqualTo(-0.07m));

		// Act: правило разбора изменено — то же сырьё пересобрано заново.
		var afterRuleChange = changedRuleMaterializer.Materialize(rawInstruments, rawExecutions, rawDeliveries, assignments, AfterDelivery());

		// Assert: пересборка согласована — состав и порядок сделок прежние, все атрибуты
		// кроме валюты комиссии не изменились, следы прежнего правила отсутствуют:
		// USDC-сделки получили USDC, USDT-сделка не затронута.
		Assert.That(afterRuleChange.InboxTrades.Count, Is.EqualTo(beforeRuleChange.InboxTrades.Count));
		for (var i = 0; i < beforeRuleChange.InboxTrades.Count; i++)
		{
			var before = beforeRuleChange.InboxTrades[i];
			var after = afterRuleChange.InboxTrades[i];
			Assert.That(after.ExecId, Is.EqualTo(before.ExecId));
			Assert.That(after.Symbol, Is.EqualTo(before.Symbol));
			Assert.That(after.ExecutedAt, Is.EqualTo(before.ExecutedAt));
			Assert.That(after.Quantity, Is.EqualTo(before.Quantity));
			Assert.That(after.Price, Is.EqualTo(before.Price));
			Assert.That(after.Fee, Is.EqualTo(before.Fee));
		}

		Assert.That(afterRuleChange.InboxTrades.Single(trade => trade.ExecId == "exec-opt-1").FeeCurrency, Is.EqualTo("USDC"));
		Assert.That(afterRuleChange.InboxTrades.Single(trade => trade.ExecId == "exec-opt-2").FeeCurrency, Is.EqualTo("USDC"));
		Assert.That(afterRuleChange.InboxTrades.Single(trade => trade.ExecId == "exec-lin-1").FeeCurrency, Is.EqualTo("USDT"));

		// Закрывающие записи и предупреждения выведены из того же сырья и не зависят
		// от правила валюты комиссии — пересчитаны теми же значениями.
		Assert.That(afterRuleChange.ExpiryClosingEntries, Is.EqualTo(beforeRuleChange.ExpiryClosingEntries));
		Assert.That(afterRuleChange.ReconciliationWarnings, Is.EqualTo(beforeRuleChange.ReconciliationWarnings));

		// Повторная сборка по изменённому правилу детерминирована: переразбор
		// не накапливает состояние между вызовами.
		var repeatedRebuild = changedRuleMaterializer.Materialize(rawInstruments, rawExecutions, rawDeliveries, assignments, AfterDelivery());
		Assert.That(repeatedRebuild.InboxTrades, Is.EqualTo(afterRuleChange.InboxTrades));
		Assert.That(repeatedRebuild.ExpiryClosingEntries, Is.EqualTo(afterRuleChange.ExpiryClosingEntries));
		Assert.That(repeatedRebuild.ReconciliationWarnings, Is.EqualTo(afterRuleChange.ReconciliationWarnings));
	}

	[TestMethod]
	[Description("Полный переразбор пустого хранилища даёт пустую проекцию")]
	public void TryIfEmptyRawStoreGivesEmptyProjection()
	{
		// Arrange: сырьё отсутствует — справочник, исполнения и delivery-записи пусты.
		var materializer = new JournalMaterializer();

		// Act
		var result = materializer.Materialize(
			Array.Empty<RawInstrument>(),
			Array.Empty<RawExecution>(),
			Array.Empty<RawDelivery>(),
			null,
			AfterDelivery());

		// Assert: все части проекции пусты, переразбор не создаёт сущностей из ничего.
		Assert.That(result.InboxTrades, Is.Empty);
		Assert.That(result.ExpiryClosingEntries, Is.Empty);
		Assert.That(result.ReconciliationWarnings, Is.Empty);
	}

	[TestMethod]
	[Description("Null-справочник сырых инструментов отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullRawInstruments()
	{
		// Arrange
		var materializer = new JournalMaterializer();

		// Act — Assert
		materializer.Materialize(null!, Array.Empty<RawExecution>(), Array.Empty<RawDelivery>(), null, AfterDelivery());
	}

	[TestMethod]
	[Description("Null-коллекция сырых записей исполнения отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullRawExecutions()
	{
		// Arrange
		var materializer = new JournalMaterializer();

		// Act — Assert
		materializer.Materialize(Array.Empty<RawInstrument>(), null!, Array.Empty<RawDelivery>(), null, AfterDelivery());
	}

	[TestMethod]
	[Description("Null-коллекция сырых delivery-записей отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullRawDeliveries()
	{
		// Arrange
		var materializer = new JournalMaterializer();

		// Act — Assert
		materializer.Materialize(Array.Empty<RawInstrument>(), Array.Empty<RawExecution>(), null!, null, AfterDelivery());
	}

	[TestMethod]
	[Description("Отрицательный допуск сверки отклоняется конструктором")]
	[ExpectedException(typeof(ArgumentOutOfRangeException))]
	public void ThrowOnNegativeReconciliationTolerance()
	{
		// Arrange — Act — Assert
		new JournalMaterializer(reconciliationTolerance: -0.01m);
	}

	#region Помощники

	/// <summary>Изменённое правило разбора для теста пересборки: валюта комиссии остаётся биржевой, паритет USDC/USDT отключён.</summary>
	private sealed class KeepRawFeeCurrencyRule : IFeeCurrencyRule
	{
		/// <inheritdoc />
		public string? Canonicalize(string? feeCurrency) => feeCurrency;
	}

	/// <summary>Момент переразбора после deliveryTime: экспирация уже наступила.</summary>
	private static DateTimeOffset AfterDelivery() => new(2023, 12, 29, 12, 0, 0, TimeSpan.Zero);

	/// <summary>
	/// Справочник инструментов: колл BTC с delivery 29DEC23 08:00 UTC и линейный перп BTCUSDT.
	/// </summary>
	private static RawInstrument[] CreateRawInstruments()
	{
		var callPayload =
			$$"""{"symbol":"{{CallSymbol}}","baseCoin":"BTC","quoteCoin":"USD","settleCoin":"USDC","status":"Trading","optionsType":"Call","deliveryTime":"{{OptionDeliveryMs}}","deliveryFeeRate":"0.00015"}""";
		var linearPayload =
			"""{"symbol":"BTCUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"BTC","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""";
		return
		[
			new RawInstrument { Symbol = CallSymbol, Category = "option", PayloadJson = callPayload, FetchedAt = FetchedAt },
			new RawInstrument { Symbol = "BTCUSDT", Category = "linear", PayloadJson = linearPayload, FetchedAt = FetchedAt },
		];
	}

	/// <summary>
	/// Исполнения: две покупки колла с комиссией в USDC (конструкция и «Входящие»)
	/// и линейная покупка с комиссией в USDT.
	/// </summary>
	private static RawExecution[] CreateRawExecutions() =>
	[
		Raw(
			"exec-opt-1", "option", CallSymbol, ExecMs(2023, 12, 28, 10, 0),
			ExecutionPayload("exec-opt-1", CallSymbol, "Buy", "100", "0.0002", "0.0002", "USDC", ExecMs(2023, 12, 28, 10, 0))),
		Raw(
			"exec-lin-1", "linear", "BTCUSDT", ExecMs(2023, 12, 28, 10, 30),
			ExecutionPayload("exec-lin-1", "BTCUSDT", "Buy", "42000", "0.01", "0.0042", "USDT", ExecMs(2023, 12, 28, 10, 30))),
		Raw(
			"exec-opt-2", "option", CallSymbol, ExecMs(2023, 12, 28, 11, 0),
			ExecutionPayload("exec-opt-2", CallSymbol, "Buy", "100", "0.0001", "0.0001", "USDC", ExecMs(2023, 12, 28, 11, 0))),
	];

	/// <summary>ITM delivery-запись колла: внутренняя стоимость 1000, deliveryRpl расходится с собственным расчётом.</summary>
	private static RawDelivery[] CreateRawDeliveries() =>
	[
		new RawDelivery
		{
			Symbol = CallSymbol,
			DeliveryTimeMs = OptionDeliveryMs,
			Category = "option",
			PayloadJson = DeliveryPayload(CallSymbol, OptionDeliveryMs, "46000", "45000", "0.0003", "0.2"),
			FetchedAt = FetchedAt,
		},
	];

	private static RawExecution Raw(string execId, string category, string symbol, long execTimeMs, string payloadJson) => new()
	{
		ExecId = execId,
		Category = category,
		Symbol = symbol,
		ExecTimeMs = execTimeMs,
		PayloadJson = payloadJson,
		FetchedAt = FetchedAt,
	};

	/// <summary>Запись исполнения в форме ответа execution-list: числа биржа шлёт строками.</summary>
	private static string ExecutionPayload(
		string execId,
		string symbol,
		string side,
		string execPrice,
		string execQty,
		string execFee,
		string? feeCurrency,
		long execTimeMs,
		bool isMaker = false)
	{
		var feeCurrencyJson = feeCurrency is null ? "null" : $"\"{feeCurrency}\"";
		var isMakerJson = isMaker ? "true" : "false";
		return $$"""{"symbol":"{{symbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"{{execFee}}","execId":"{{execId}}","execPrice":"{{execPrice}}","execQty":"{{execQty}}","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":{{feeCurrencyJson}},"isMaker":{{isMakerJson}}}""";
	}

	/// <summary>Delivery-запись в форме ответа delivery-record: числа биржа шлёт строками.</summary>
	private static string DeliveryPayload(
		string symbol,
		long deliveryTimeMs,
		string deliveryPrice,
		string strike,
		string fee,
		string deliveryRpl) =>
		$$"""{"symbol":"{{symbol}}","side":"Buy","deliveryTime":"{{deliveryTimeMs}}","entryPrice":"100","deliveryPrice":"{{deliveryPrice}}","strike":"{{strike}}","fee":"{{fee}}","position":"0.0003","deliveryRpl":"{{deliveryRpl}}"}""";

	private static long ExecMs(int year, int month, int day, int hour, int minute) =>
		new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	#endregion
}
