using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain;
using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Domain.Materialization;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Application.Sync;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Infrastructure.ReadModels;

/// <summary>
/// Интеграционные проверки read-модели экрана деталей конструкции на стыке
/// с метриками аналитики и потоком закрывающих записей: сводка присоединяет
/// метрики с периодом и отметкой марок, сделки и комментарии читаются из
/// пользовательских данных, закрывающие записи перечисляются с типом,
/// инструментом и суммой, а пустые таблицы передаются пустыми списками —
/// экран показывает по ним явное сообщение об отсутствии.
/// Traceability: openspec:ui/screens#requirement-construction-detail-screen
/// </summary>
[TestClass]
public class ConstructionDetailReadModelTests
{
	private const string CallSymbol = "BTC-29DEC23-45000-C";
	private const string LinearSymbol = "BTCUSDT";

	/// <summary>Живая опционная нога сценария времени жизни: доска 25JUN27 08:00 UTC.</summary>
	private const string FutureCallSymbol = "BTC-25JUN27-106000-C";

	/// <summary>Фиксированный момент чтения метрик времени жизни.</summary>
	private static readonly DateTimeOffset LifetimeNow = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

	// Открытые ноги сценария сортировки — дальняя доска 2030 года: её экспирация
	// ещё не наступила на любых часах, поэтому статус ног определяется сделками.
	private const string OpenCallHigh = "ETH-01JAN30-2500-C";
	private const string OpenPutHigh = "ETH-01JAN30-2500-P";
	private const string OpenCallLow = "ETH-01JAN30-2000-C";

	// Закрытые ноги сценария — эталонный пример спеки: доски 25DEC26, 25SEP26
	// и 29MAY26 с фьючерсом; каждая нога прикрыта встречной сделкой до поставки,
	// поэтому прошедшие экспирации не меняют её статус на любых часах.
	private const string DecCall2200 = "ETH-25DEC26-2200-C";
	private const string DecPut2200 = "ETH-25DEC26-2200-P";
	private const string DecCall1900 = "ETH-25DEC26-1900-C";
	private const string DecPut1900 = "ETH-25DEC26-1900-P";
	private const string SepCall2200 = "ETH-25SEP26-2200-C";
	private const string SepPut2200 = "ETH-25SEP26-2200-P";
	private const string SepCall1900 = "ETH-25SEP26-1900-C";
	private const string SepCall1800 = "ETH-25SEP26-1800-C";
	private const string SepPut1800 = "ETH-25SEP26-1800-P";
	private const string MayCall1900 = "ETH-29MAY26-1900-C";
	private const string MayPut1900 = "ETH-29MAY26-1900-P";

	// Инструменты сценариев автоматической сборки: затухающий стреддл доски
	// 25SEP26 с фьючерсом ETHUSDT и живой стреддл доски 25DEC27.
	private const string EthCallSep = "ETH-25SEP26-1600-C-USDT";
	private const string EthPutSep = "ETH-25SEP26-1600-P-USDT";
	private const string EthCallDec27 = "ETH-25DEC27-2100-C-USDT";
	private const string EthPutDec27 = "ETH-25DEC27-2100-P-USDT";
	private const string EthPerpSymbol = "ETHUSDT";

	// Инструменты кейса синтетического закрытия XAUT 15.10.2026: стрэддл доски
	// 30OCT26 08:00 UTC, связка с линейным перпом и скальп 0.04 XAUTUSDT.
	private const string XautCall = "XAUT-30OCT26-4400-C-USDT";
	private const string XautPut = "XAUT-30OCT26-4400-P-USDT";
	private const string XautPerp = "XAUTUSDT";

	/// <summary>Момент «сейчас» пересбора: сделки истории позади, доска 25DEC27 ещё жива.</summary>
	private static readonly DateTimeOffset AssemblyNow = new(2026, 12, 10, 12, 0, 0, TimeSpan.Zero);

	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	/// <summary>Каноническое время delivery инструмента опциона из справочника: 29DEC23 08:00 UTC.</summary>
	private static readonly DateTimeOffset OptionDelivery = new(2023, 12, 29, 8, 0, 0, TimeSpan.Zero);

	private static readonly long OptionDeliveryMs = OptionDelivery.ToUnixTimeMilliseconds();

	private string _databasePath = null!;
	private ConstructionService _constructionService = null!;
	private TradeBindingService _bindingService = null!;
	private CommentService _commentService = null!;
	private ManualCloseMarkService _markService = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке
		// со справочником линейного перпа и опциона с delivery 29DEC23.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-construction-detail-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
			SeedInstrumentCatalog(db);
		}

		_constructionService = new ConstructionService(CreateOptions());
		_bindingService = new TradeBindingService(CreateOptions());
		_commentService = new CommentService(CreateOptions());
		_markService = new ManualCloseMarkService(CreateOptions());
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Пул соединений SQLite держит файл базы открытым — сбрасываем его перед удалением.
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

		// Временная база и соседние WAL/SHM-файлы удаляются после каждой проверки.
		foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
		{
			var file = _databasePath + suffix;
			if (File.Exists(file))
			{
				File.Delete(file);
			}
		}

	}

	[TestMethod]
	[Description("Сводка деталей присоединяет метрики с периодом и отметкой марок, комментарий — к заголовку")]
	public async Task TryIfSummaryJoinsMetricsPeriodMarksAndComment()
	{
		// Arrange: конструкция с капиталом 1000 и комментарием; покупка 0.1 BTC
		// по 42000 с комиссией 1 привязана к конструкции; у сделки и позиции
		// оставлены комментарии; свежая марка провайдера — 44000 на 2026-09-20 12:00.
		var construction = await _constructionService.CreateAsync("Календарь сентябрь", 1000m);
		await _commentService.SetConstructionCommentAsync(construction.Id, "тестовая конструкция");
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await _bindingService.BindAsync(construction.Id, "exec-buy");
		await _commentService.SetTradeCommentAsync("exec-buy", "вход в календарь");
		await _commentService.SetPositionCommentAsync(construction.Id, LinearSymbol, "базовая позиция");
		var receivedAt = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
		var readModel = CreateDetailReadModel(new StubFreshMarkSource(44000m, receivedAt));

		// Act: читаем данные экрана деталей.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: сводка показывает итог, процент от капитала, разбивку по
		// реализованному и нереализованному результату, сумму корректировок,
		// период с длительностью и отметку времени марок; комментарий — рядом
		// со сводкой.
		// Требование: детали показывают сводку метрик с периодом и отметкой марок.
		// Traceability: openspec:ui/screens#scenario-detail-summary-metrics-period
		Assert.That(data.Name, Is.EqualTo("Календарь сентябрь"));
		Assert.That(data.Status, Is.EqualTo(ConstructionStatus.Open));
		Assert.That(data.AllocatedCapitalUsdt, Is.EqualTo(1000m));
		Assert.That(data.Comment, Is.EqualTo("тестовая конструкция"));
		Assert.That(data.Metrics.RealizedPnL, Is.EqualTo(-1m));
		Assert.That(data.Metrics.UnrealizedPnL, Is.EqualTo(200m));
		Assert.That(data.Metrics.AdjustmentsPnL, Is.EqualTo(0m));
		Assert.That(data.Metrics.TotalPnL, Is.EqualTo(199m));
		Assert.That(data.Metrics.TotalPnLPercent, Is.EqualTo(19.9m));
		Assert.That(data.Metrics.OpenedAt, Is.EqualTo(new DateTimeOffset(2023, 12, 28, 10, 0, 0, TimeSpan.Zero)));
		Assert.That(data.Metrics.ClosedAt, Is.Null);
		Assert.That(data.Metrics.Duration, Is.Not.Null);
		Assert.That(data.HasOpenResidual, Is.True);
		Assert.That(data.HasMarkFailure, Is.False);
		Assert.That(data.MarksAsOf, Is.EqualTo(receivedAt));

		// Строка позиции несёт общий P&L (реализованный плюс нереализованный) и его
		// процент от выделенного капитала конструкции: 199 от 1000 — 19.9%;
		// части результата передаются из метрик раздельно со своими процентами:
		// реализованный −1 (комиссия) — −0.1% капитала, нереализованный 200
		// (оценка остатка маркой 44000) — 20% капитала.
		// Требование: строка позиции показывает общий P&L с процентом от капитала,
		// реализованная и нереализованная части — отдельными величинами с процентом.
		// Traceability: openspec:ui/screens#scenario-detail-position-row-entry-close-total
		// Traceability: openspec:ui/screens#scenario-detail-position-pnl-parts
		var position = data.Positions.Single();
		Assert.That(position.RealizedPnL, Is.EqualTo(-1m));
		Assert.That(position.RealizedPnLPercent, Is.EqualTo(-0.1m));
		Assert.That(position.UnrealizedPnL, Is.EqualTo(200m));
		Assert.That(position.UnrealizedPnLPercent, Is.EqualTo(20m));
		Assert.That(position.TotalPnL, Is.EqualTo(199m));
		Assert.That(position.TotalPnLPercent, Is.EqualTo(19.9m));
	}

	[TestMethod]
	[Description("Строка открытой опционной ноги несёт величины времени жизни, фьючерсная нога — прочерк")]
	// Колонки времени жизни таблицы позиций выводятся только открытым опционным
	// ногам: сутки, распад и состав тултипа проходят из вычислителя аналитики
	// по символу; фьючерсная нога остаётся прочерком по всем пяти величинам.
	// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#requirement-construction-detail-screen
	public async Task TryIfOptionRowCarriesLifetimeMetricsAndFutureRowShowsDash()
	{
		// Arrange: конструкция с открытой опционной ногой (покупка 1 колла 106000)
		// и открытым фьючерсным остатком; тикер опциона несёт индексную цену и
		// тету, история снимков держит тету суточной давности; часы зафиксированы.
		await SeedFutureCallInstrumentAsync();
		var construction = await _constructionService.CreateAsync("Долгий колл", 1000m);
		await AddRawExecutionAsync("opt-buy", FutureCallSymbol, "option", "Buy", "1", "15000", ExecMs(2026, 10, 10, 10, 0), "USDT");
		await AddLinearTradeAsync("perp-buy", "Buy", "0.1", "42000", "1", ExecMs(2026, 10, 10, 11, 0));
		await _bindingService.BindBatchAsync(construction.Id, ["opt-buy", "perp-buy"]);
		await AddMarketSnapshotAsync(FutureCallSymbol, markPrice: 15000m, theta: -1.0m, receivedAt: LifetimeNow.AddHours(-25));
		var markSource = new StubFreshMarkSource(15000m, LifetimeNow.AddHours(-1), underlyingPrice: 110000m, theta: -2.5m);
		var readModel = CreateDetailReadModel(markSource, new FixedTimeProvider(LifetimeNow));

		// Act: читаем данные экрана деталей.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: строка опциона несёт остаток 249 полных суток (249 сут 20 ч до
		// доставки), распад тетой на остаток, разложение марки на внутреннюю
		// (110000 − 106000) и временную (15000 − 4000) стоимость и вчерашний
		// распад по снимку суточной давности.
		// Требование: метрики времени жизни открытой опционной ноги в строке.
		// Traceability: openspec:analytics/option-lifetime#requirement-leg-decay-and-days-to-expiry
		// Traceability: openspec:analytics/option-lifetime#requirement-leg-intrinsic-time-value
		// Traceability: openspec:analytics/option-lifetime#requirement-leg-decay-acceleration
		var optionRow = data.Positions.Single(position => position.Symbol == FutureCallSymbol);
		Assert.That(optionRow.DaysToExpiry, Is.EqualTo(249));
		Assert.That(optionRow.DailyDecayUsdt, Is.EqualTo(-2.5m));
		Assert.That(optionRow.IntrinsicValueUsdt, Is.EqualTo(4000m));
		Assert.That(optionRow.TimeValueUsdt, Is.EqualTo(11000m));
		Assert.That(optionRow.DecayDayAgoUsdt, Is.EqualTo(-1.0m));

		// Assert: фьючерсная нога не участвует в метриках времени жизни —
		// все пять величин прочерком, остальные колонки не затронуты.
		// Требование: фьючерсная нога без величин времени жизни.
		// Traceability: openspec:analytics/option-lifetime#requirement-leg-decay-and-days-to-expiry
		var futureRow = data.Positions.Single(position => position.Symbol == LinearSymbol);
		Assert.That(futureRow.DaysToExpiry, Is.Null);
		Assert.That(futureRow.DailyDecayUsdt, Is.Null);
		Assert.That(futureRow.IntrinsicValueUsdt, Is.Null);
		Assert.That(futureRow.TimeValueUsdt, Is.Null);
		Assert.That(futureRow.DecayDayAgoUsdt, Is.Null);

		// Assert: саммари конструкции построено по открытой опционной ноге,
		// дальняя экспирация далека от порога предупреждения.
		// Требование: саммари времени жизни в сводке деталей.
		// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
		Assert.That(data.Metrics.Lifetime, Is.Not.Null);
		Assert.That(data.Metrics.Lifetime!.Summary!.DaysToNearestExpiry, Is.EqualTo(249));
		Assert.That(data.Metrics.Lifetime.Summary.NetDailyDecayUsdt, Is.EqualTo(-2.5m));
		Assert.That(data.Metrics.RiskWarnings.Count(warning => warning.Reason == ConstructionRiskReason.ExpiryApproaching), Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Детали без капитала дают абсолютные величины без процентов и введённые единицы риск/профит")]
	public async Task TryIfDetailsWithoutCapitalHidePercentsAndCarryEnteredTargetUnit()
	{
		// Arrange: конструкция без выделенного капитала с риском 5% и профитом
		// 300 USDT; покупка 0.1 BTC по 42000 с комиссией 1 привязана к ней;
		// свежая марка провайдера — 44000.
		var construction = await _constructionService.CreateAsync("Без бюджета", null);
		await _constructionService.UpdateRiskAsync(construction.Id, 5m, TargetUnit.Percent);
		await _constructionService.UpdateProfitAsync(construction.Id, 300m, TargetUnit.Usdt);
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await _bindingService.BindAsync(construction.Id, "exec-buy");
		var readModel = CreateDetailReadModel(new StubFreshMarkSource(44000m, FetchedAt));

		// Act: читаем данные экрана деталей.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: сводка и строки позиций показывают абсолютные величины
		// результата; процент от капитала в сводке не вычисляется, проценты
		// в скобках строк позиций отсутствуют — процентные поля null, а не ноль.
		// Требование: детали без капитала показывают абсолютные величины
		// без процентов.
		// Traceability: openspec:ui/screens#scenario-detail-no-capital-no-percent
		Assert.That(data.AllocatedCapitalUsdt, Is.Null);
		Assert.That(data.Metrics.RealizedPnL, Is.EqualTo(-1m));
		Assert.That(data.Metrics.UnrealizedPnL, Is.EqualTo(200m));
		Assert.That(data.Metrics.TotalPnL, Is.EqualTo(199m));
		Assert.That(data.Metrics.TotalPnLPercent, Is.Null);
		var position = data.Positions.Single();
		Assert.That(position.RealizedPnL, Is.EqualTo(-1m));
		Assert.That(position.UnrealizedPnL, Is.EqualTo(200m));
		Assert.That(position.TotalPnL, Is.EqualTo(199m));
		Assert.That(position.RealizedPnLPercent, Is.Null);
		Assert.That(position.UnrealizedPnLPercent, Is.Null);
		Assert.That(position.TotalPnLPercent, Is.Null);

		// Assert: сводка несёт величины риск/профит обеими единицами; без капитала
		// конвертер вернул только введённые единицы: проценты риска без USDT
		// и USDT профита без процентов.
		// Требование: без капитала вторая единица не вычисляется.
		// Traceability: openspec:analytics/performance#scenario-conversion-needs-capital
		Assert.That(data.RiskPercent, Is.EqualTo(5m));
		Assert.That(data.RiskUsdt, Is.Null);
		Assert.That(data.ProfitUsdt, Is.EqualTo(300m));
		Assert.That(data.ProfitPercent, Is.Null);
	}

	[TestMethod]
	[Description("Строки позиций и сделок несут атрибуты таблиц с комментариями своих уровней")]
	public async Task TryIfPositionAndTradeRowsCarryAttributesAndComments()
	{
		// Arrange: конструкция с покупкой 0.1 BTC по 42000 (комиссия 1) и продажей
		// 0.04 по 42500 (комиссия 0.5); комментарии оставлены на сделке покупки
		// и на позиции.
		var construction = await _constructionService.CreateAsync("Календарь сентябрь", 3000m);
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await AddLinearTradeAsync("exec-sell", "Sell", "0.04", "42500", "0.5", ExecMs(2023, 12, 28, 11, 0));
		await _bindingService.BindBatchAsync(construction.Id, ["exec-buy", "exec-sell"]);
		await _commentService.SetTradeCommentAsync("exec-buy", "вход в календарь");
		await _commentService.SetPositionCommentAsync(construction.Id, LinearSymbol, "частичный выход");
		var readModel = CreateDetailReadModel(new StubFreshMarkSource(44000m, FetchedAt));

		// Act: читаем данные экрана деталей.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: позиция несёт картину своих записей — средние цены входа и
		// закрытия, общий P&L, комиссии и времена —
		// с комментарием позиции; сделки перечислены от новых к старым
		// с атрибутами биржевой записи и комментарием своей сделки.
		// Требование: строка позиции показывает вход, выход и итог.
		// Traceability: openspec:ui/screens#scenario-detail-position-row-entry-close-total
		var position = data.Positions.Single();
		Assert.That(position.Symbol, Is.EqualTo(LinearSymbol));
		Assert.That(position.Residual, Is.EqualTo(0.06m));
		Assert.That(position.AverageEntryPrice, Is.EqualTo(42000m));
		Assert.That(position.AverageClosePrice, Is.EqualTo(42500m));
		Assert.That(position.TotalPnL, Is.EqualTo(138.5m));
		Assert.That(position.AccumulatedFees, Is.EqualTo(1.5m));
		Assert.That(position.OpenedAt, Is.EqualTo(new DateTimeOffset(2023, 12, 28, 10, 0, 0, TimeSpan.Zero)));
		Assert.That(position.ClosedAt, Is.Null);
		Assert.That(position.IsOpen, Is.True);
		Assert.That(position.Comment, Is.EqualTo("частичный выход"));

		// Требование: таблица сделок деталей начинается с самой поздней сделки.
		// Traceability: openspec:ui/screens#scenario-detail-trades-newest-first
		Assert.That(data.Trades.Select(trade => trade.ExecId).ToArray(), Is.EqualTo(new[] { "exec-sell", "exec-buy" }));
		var newest = data.Trades[0];
		Assert.That(newest.Symbol, Is.EqualTo(LinearSymbol));
		Assert.That(newest.IsBuy, Is.False);
		Assert.That(newest.AmountUsdt, Is.EqualTo(1700m));
		Assert.That(newest.Comment, Is.Null);
		var oldest = data.Trades[1];
		Assert.That(oldest.IsBuy, Is.True);
		Assert.That(oldest.Quantity, Is.EqualTo(0.1m));
		Assert.That(oldest.Price, Is.EqualTo(42000m));
		Assert.That(oldest.AmountUsdt, Is.EqualTo(4200m));
		Assert.That(oldest.Fee, Is.EqualTo(1m));
		Assert.That(oldest.Comment, Is.EqualTo("вход в календарь"));
	}

	[TestMethod]
	[Description("Таблица сделок начинается с самой поздней сделки и детерминирована при равном времени")]
	public async Task TryIfTradeRowsNewestFirstAndDeterministicOnEqualTime()
	{
		// Arrange: три привязанные сделки — ранняя и две с равным временем
		// исполнения, вставленные в порядке, обратном ожидаемому списку.
		// Требование: таблица сделок начинается с самой поздней по времени
		// исполнения сделки; при равном времени порядок строк детерминирован
		// тай-брейком execId по убыванию и не меняется между перечитываниями.
		// Traceability: openspec:ui/screens#scenario-detail-trades-newest-first
		var construction = await _constructionService.CreateAsync("Хронология сделок", 1000m);
		await AddLinearTradeAsync("exec-late-z", "Buy", "0.01", "42000", "0", ExecMs(2023, 12, 28, 11, 0));
		await AddLinearTradeAsync("exec-early", "Buy", "0.01", "41000", "0", ExecMs(2023, 12, 28, 10, 0));
		await AddLinearTradeAsync("exec-late-a", "Buy", "0.01", "43000", "0", ExecMs(2023, 12, 28, 11, 0));
		await _bindingService.BindBatchAsync(construction.Id, new[] { "exec-late-z", "exec-early", "exec-late-a" });
		var readModel = CreateDetailReadModel(new StubFreshMarkSource(44000m, FetchedAt));

		// Act: дважды читаем данные экрана деталей.
		var firstRead = await readModel.ReadAsync(construction.Id);
		var secondRead = await readModel.ReadAsync(construction.Id);

		// Assert: самая поздняя сделка в начале; при равном времени выше строка
		// с большим execId, порядок стабилен между перечитываниями.
		var expectedOrder = new[] { "exec-late-z", "exec-late-a", "exec-early" };
		Assert.That(firstRead.Trades.Select(trade => trade.ExecId).ToArray(), Is.EqualTo(expectedOrder));
		Assert.That(secondRead.Trades.Select(trade => trade.ExecId).ToArray(), Is.EqualTo(expectedOrder));
	}

	[TestMethod]
	[Description("Строка позиции несёт ровно выводимые столбцы — служебные величины остатка убраны")]
	public void TryIfPositionRowCarriesExactlyDisplayedColumns()
	{
		// Assert: контракт строки зафиксирован составом публичных свойств — поля
		// служебных величин оценки открытого остатка (средняя остатка, марка как
		// цена) из строки убраны вместе с их колонками; стоимость и процент
		// изменения цены открытого остатка добавлены выводимыми столбцами; распад
		// остатка линейной ноги на синтетическую и прочую части — двумя колонками;
		// величины времени жизни открытой опционной ноги — пятью колонками.
		// Требование: строка позиции показывает вход, выход и итог,
		// колонки «Средняя», «Марка» и «Нереализов.» отсутствуют.
		// Traceability: openspec:ui/screens#scenario-detail-position-row-entry-close-total
		// Traceability: change:synthetic-close-logical-layer/specs/ui/screens/spec#scenario-detail-linear-leg-splits-synthetic-and-scalps
		// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#requirement-construction-detail-screen
		var columns = typeof(ConstructionPositionRow).GetProperties().Select(property => property.Name).ToArray();
		Assert.That(columns, Is.EqualTo(new[]
		{
			nameof(ConstructionPositionRow.Symbol),
			nameof(ConstructionPositionRow.Residual),
			nameof(ConstructionPositionRow.AverageEntryPrice),
			nameof(ConstructionPositionRow.AverageClosePrice),
			nameof(ConstructionPositionRow.RealizedPnL),
			nameof(ConstructionPositionRow.RealizedPnLPercent),
			nameof(ConstructionPositionRow.UnrealizedPnL),
			nameof(ConstructionPositionRow.UnrealizedPnLPercent),
			nameof(ConstructionPositionRow.TotalPnL),
			nameof(ConstructionPositionRow.TotalPnLPercent),
			nameof(ConstructionPositionRow.AccumulatedFees),
			nameof(ConstructionPositionRow.OpenedAt),
			nameof(ConstructionPositionRow.ClosedAt),
			nameof(ConstructionPositionRow.IsOpen),
			nameof(ConstructionPositionRow.Comment),
			nameof(ConstructionPositionRow.MarkValue),
			nameof(ConstructionPositionRow.PriceChangePercent),
			nameof(ConstructionPositionRow.SyntheticResidual),
			nameof(ConstructionPositionRow.OtherResidual),
			nameof(ConstructionPositionRow.DaysToExpiry),
			nameof(ConstructionPositionRow.DailyDecayUsdt),
			nameof(ConstructionPositionRow.IntrinsicValueUsdt),
			nameof(ConstructionPositionRow.TimeValueUsdt),
			nameof(ConstructionPositionRow.DecayDayAgoUsdt),
		}));
	}

	[TestMethod]
	[Description("Строки позиций несут стоимость и процент изменения цены открытого остатка")]
	public async Task TryIfPositionRowsCarryMarkValueAndPriceChangePercent()
	{
		// Arrange: конструкция с открытой ногой — покупка 1 BTC по 100 — и закрытой
		// ногой опциона — покупка 1 по 100 и продажа 1 по 90; свежая марка
		// провайдера — 110 для обоих инструментов.
		var construction = await _constructionService.CreateAsync("Смешанная", 1000m);
		await AddLinearTradeAsync("exec-buy", "Buy", "1", "100", "0", ExecMs(2023, 12, 28, 10, 0));
		await _bindingService.BindAsync(construction.Id, "exec-buy");
		await AddOptionTradeAsync("exec-opt-buy", "Buy", "1", "100");
		await AddOptionTradeAsync("exec-opt-sell", "Sell", "1", "90");
		await _bindingService.BindBatchAsync(construction.Id, ["exec-opt-buy", "exec-opt-sell"]);
		var readModel = CreateDetailReadModel(new StubFreshMarkSource(110m, FetchedAt));

		// Act: читаем данные экрана деталей.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: открытая нога несёт стоимость по марке 110 × 1 = 110 и процент
		// изменения цены (110 − 100) / 100 × 100 = 10; закрытая нога обе величины
		// имеет null при живом реализованном результате (90 − 100) × 1 = −10.
		// Требование: стоимость и процент изменения цены открытого остатка
		// проходят в строку позиции из метрик, закрытая позиция их не имеет.
		// Traceability: openspec:analytics/performance#requirement-open-remainder-price-change-percent
		var open = data.Positions.Single(row => row.Symbol == LinearSymbol);
		Assert.That(open.IsOpen, Is.True);
		Assert.That(open.MarkValue, Is.EqualTo(110m));
		Assert.That(open.PriceChangePercent, Is.EqualTo(10m));
		var closed = data.Positions.Single(row => row.Symbol == CallSymbol);
		Assert.That(closed.IsOpen, Is.False);
		Assert.That(closed.MarkValue, Is.Null);
		Assert.That(closed.PriceChangePercent, Is.Null);
		Assert.That(closed.RealizedPnL, Is.EqualTo(-10m));
	}

	[TestMethod]
	[Description("Строки синтетических закрытий и распад линейной ноги обслуживают кейс XAUT: 0.30 синтетики и 0.04 скальпов")]
	// Кейс эталонный: стрэддл 0.5, связка «перп 0.3 + продажа 0.3 колла» и
	// скальп 0.04 дают остаток XAUTUSDT 0.34 с распадом 0.30 синтетики и
	// 0.04 прочего остатка; величина лока связки — 35.697 USDT.
	// Traceability: change:synthetic-close-logical-layer/specs/ui/screens/spec#scenario-detail-linear-leg-splits-synthetic-and-scalps
	// Traceability: change:synthetic-close-logical-layer/specs/ui/screens/spec#requirement-detail-synthetic-close-line
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#requirement-synthetic-close-locked-value
	public async Task TryIfSyntheticCloseRowsAndLinearSplitServeXautCase()
	{
		// Arrange: справочник кейса, конструкция с пятью сделками эталонного
		// состава и живая марка для метрик позиций.
		SeedXautInstruments();
		var construction = await _constructionService.CreateAsync("XAUT-кейс", 1000m);
		await SeedXautCaseAsync();
		await _bindingService.BindBatchAsync(construction.Id, ["open-put", "open-call", "case-perp", "case-call", "scalp-perp"]);
		var readModel = CreateDetailReadModel(new StubFreshMarkSource(4300m, FetchedAt));

		// Act: читаем данные экрана деталей.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: распознана ровно одна связка с ногой пут, количеством 0.3 и
		// локом 35.697 USDT; остаток перпа 0.34 распадается на 0.30 синтетики
		// и 0.04 прочего остатка; ноги опционов распада не несут.
		Assert.That(data.SyntheticCloses, Has.Count.EqualTo(1));
		var link = data.SyntheticCloses!.Single();
		Assert.That(link.ClosedLegSymbol, Is.EqualTo(XautPut));
		Assert.That(link.Quantity, Is.EqualTo(0.3m));
		Assert.That(link.LockedValueUsdt, Is.EqualTo(35.697m));
		Assert.That(link.Disputed, Is.False);
		// Пара execId распознанной связки доходит в строку read-модели: по ней
		// UI адресует связку в командах оспаривания и возврата.
		// Traceability: change:add-synthetic-close-dispute-ui/design#d1
		Assert.That(link.LinearExecId, Is.EqualTo("case-perp"));
		Assert.That(link.CounterOptionExecId, Is.EqualTo("case-call"));
		var perp = data.Positions.Single(row => row.Symbol == XautPerp);
		Assert.That(perp.Residual, Is.EqualTo(0.34m));
		Assert.That(perp.SyntheticResidual, Is.EqualTo(0.3m));
		Assert.That(perp.OtherResidual, Is.EqualTo(0.04m));
		var put = data.Positions.Single(row => row.Symbol == XautPut);
		Assert.That(put.SyntheticResidual, Is.Null);
		Assert.That(put.OtherResidual, Is.Null);
		var call = data.Positions.Single(row => row.Symbol == XautCall);
		Assert.That(call.SyntheticResidual, Is.Null);
		Assert.That(call.OtherResidual, Is.Null);
	}

	[TestMethod]
	[Description("Оспоренная связка остаётся строкой с признаком спора, а распад линейной ноги отсутствует")]
	// Спор исключает связку из логического слоя: лок строки не пересчитывается,
	// признак спора виден, синтетическая часть остатка ноги обнуляется.
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-dispute-hides-logical-link
	// Traceability: change:synthetic-close-logical-layer/specs/ui/screens/spec#scenario-detail-synthetic-locked-line-shown
	public async Task TryIfDisputedLinkKeepsRowWithFlagAndSplitAbsent()
	{
		// Arrange: кейс XAUT с оспоренной связкой «case-perp × case-call».
		SeedXautInstruments();
		var construction = await _constructionService.CreateAsync("XAUT-спор", 1000m);
		await SeedXautCaseAsync();
		await _bindingService.BindBatchAsync(construction.Id, ["open-put", "open-call", "case-perp", "case-call", "scalp-perp"]);
		var disputeService = new SyntheticCloseDisputeService(CreateOptions());
		await disputeService.DisputeAsync(construction.Id, "case-perp", "case-call");
		var readModel = CreateDetailReadModel(new StubFreshMarkSource(4300m, FetchedAt));

		// Act: читаем данные экрана деталей после оспаривания.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: строка связки осталась с признаком спора и прежней величиной
		// лока; распад остатка перпа отсутствует — колонки null при остатке 0.34.
		Assert.That(data.SyntheticCloses, Has.Count.EqualTo(1));
		var link = data.SyntheticCloses!.Single();
		Assert.That(link.Disputed, Is.True);
		Assert.That(link.LockedValueUsdt, Is.EqualTo(35.697m));
		var perp = data.Positions.Single(row => row.Symbol == XautPerp);
		Assert.That(perp.Residual, Is.EqualTo(0.34m));
		Assert.That(perp.SyntheticResidual, Is.Null);
		Assert.That(perp.OtherResidual, Is.Null);
	}

	[TestMethod]
	[Description("Деталь без синтетических связок передаёт пустой список и не несёт распада остатков")]
	// Пустота — обычное состояние деталей: список связок передаётся пустым,
	// колонки распада остаются null у всех позиций.
	// Traceability: change:synthetic-close-logical-layer/specs/ui/screens/spec#scenario-detail-synthetic-line-absent
	public async Task TryIfDetailWithoutSyntheticClosesServesEmptyListAndNoSplit()
	{
		// Arrange: конструкция с одной линейной сделкой без опционных ног.
		var construction = await _constructionService.CreateAsync("Без связок", 1000m);
		await AddLinearTradeAsync("exec-lone", "Buy", "1", "43000", "0", ExecMs(2023, 12, 28, 10, 0));
		await _bindingService.BindAsync(construction.Id, "exec-lone");
		var readModel = CreateDetailReadModel(new StubFreshMarkSource(44000m, FetchedAt));

		// Act: читаем данные экрана деталей.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: список связок пуст, распад у позиции отсутствует.
		Assert.That(data.SyntheticCloses, Is.Empty);
		var position = data.Positions.Single(row => row.Symbol == LinearSymbol);
		Assert.That(position.SyntheticResidual, Is.Null);
		Assert.That(position.OtherResidual, Is.Null);
	}

	[TestMethod]
	[Description("Сводная «с учётом зафиксированного синтетикой» суммирует реализованный результат и локи связок кейса XAUT")]
	// Эталонный кейс XAUT: реализованный результат конструкции −46.6 USDT и
	// лок действующей связки ≈ +35.7 USDT дают сводную величину ≈ −10.9 USDT.
	// Traceability: change:add-synthetic-adjusted-finresult/specs/domain/synthetic-close/spec#scenario-adjusted-total-sums-realized-and-locks
	public async Task TryIfSyntheticAdjustedTotalSumsRealizedResultAndActiveLocks()
	{
		// Arrange: кейс XAUT с довершающим выходом скальпа «sell 0.04 @ 3099.91»:
		// FIFO-результат перпа (−1042 × 0.04 − 0.64) вместе с ногами опционов
		// даёт реализованный результат −46.6 USDT, связка действует с локом 35.697.
		SeedXautInstruments();
		var construction = await _constructionService.CreateAsync("XAUT-сводная", 1000m);
		await SeedXautCaseAsync();
		await AddXautExecutionAsync("scalp-exit", XautPerp, "linear", "Sell", "0.04", "3099.91", "0", ExecMs(2026, 10, 15, 16, 0), "USDC");
		await _bindingService.BindBatchAsync(construction.Id, ["open-put", "open-call", "case-perp", "case-call", "scalp-perp", "scalp-exit"]);
		var readModel = CreateDetailReadModel(new StubFreshMarkSource(4300m, FetchedAt));

		// Act: читаем данные экрана деталей.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: сводная величина — реализованный −46.6 плюс лок 35.697 —
		// равна −10.903 с допуском округления; связка действует неоспоренной.
		Assert.That(data.Metrics.RealizedPnL, Is.EqualTo(-46.6m).Within(0.001m));
		Assert.That(data.Metrics.SyntheticAdjustedTotalUsdt, Is.EqualTo(-10.903m).Within(0.001m));
		var link = data.SyntheticCloses!.Single();
		Assert.That(link.Disputed, Is.False);
		Assert.That(link.LockedValueUsdt, Is.EqualTo(35.697m));
	}

	[TestMethod]
	[Description("Сводная складывается из действующих связок: оспоренная исключается, ручная включается наравне")]
	// Состав суммы определяет логический слой: лок оспоренной распознанной
	// связки в сводную не входит, ручная связка той же пары входит в неё
	// наравне с распознанными.
	// Traceability: change:add-synthetic-adjusted-finresult/specs/domain/synthetic-close/spec#scenario-adjusted-total-uses-active-links
	public async Task TryIfSyntheticAdjustedTotalUsesActiveLinksOnly()
	{
		// Arrange: кейс XAUT с оспоренной распознанной связкой и ручной связкой
		// той же парой количеством 0.3 — действует только ручная.
		SeedXautInstruments();
		var construction = await _constructionService.CreateAsync("XAUT-спор-и-ручная", 1000m);
		await SeedXautCaseAsync();
		await _bindingService.BindBatchAsync(construction.Id, ["open-put", "open-call", "case-perp", "case-call", "scalp-perp"]);
		await new SyntheticCloseDisputeService(CreateOptions()).DisputeAsync(construction.Id, "case-perp", "case-call");
		var manualLink = await new SyntheticCloseManualLinkService(CreateOptions()).CreateAsync(construction.Id, "case-perp", "case-call", 0.3m);
		var readModel = CreateDetailReadModel(new StubFreshMarkSource(4300m, FetchedAt));

		// Act: читаем данные экрана деталей после спора и ручной связки.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: распознанная строка осталась со спором, ручная действует без
		// спора; сводная величина собрана из реализованного результата и лока
		// ровно одной действующей ручной связки.
		Assert.That(data.SyntheticCloses, Has.Count.EqualTo(2));
		Assert.That(data.SyntheticCloses!.Single(row => row.ManualLinkId == null).Disputed, Is.True);
		var manualRow = data.SyntheticCloses!.Single(row => row.ManualLinkId == manualLink.Id);
		Assert.That(manualRow.Disputed, Is.False);
		Assert.That(manualRow.LockedValueUsdt, Is.EqualTo(35.697m));
		Assert.That(data.Metrics.SyntheticAdjustedTotalUsdt, Is.EqualTo(data.Metrics.RealizedPnL + 35.697m).Within(0.001m));
	}

	[TestMethod]
	[Description("Сводная отсутствует без действующих связок: нет связок вовсе и когда все связки оспорены")]
	// Null — отсутствие величины, а не ноль: конструкция без связок и
	// конструкция, у которой оспорена единственная связка, сводную не публикуют.
	// Traceability: change:add-synthetic-adjusted-finresult/specs/domain/synthetic-close/spec#scenario-adjusted-total-absent-without-active-links
	public async Task TryIfSyntheticAdjustedTotalAbsentWithoutActiveLinks()
	{
		// Arrange: конструкция с одной линейной сделкой без связок и кейс XAUT
		// с оспоренной единственной связкой.
		var plain = await _constructionService.CreateAsync("Без связок", 1000m);
		await AddLinearTradeAsync("exec-lone", "Buy", "1", "43000", "0", ExecMs(2023, 12, 28, 10, 0));
		await _bindingService.BindAsync(plain.Id, "exec-lone");
		SeedXautInstruments();
		var disputed = await _constructionService.CreateAsync("XAUT-всё-оспорено", 1000m);
		await SeedXautCaseAsync();
		await _bindingService.BindBatchAsync(disputed.Id, ["open-put", "open-call", "case-perp", "case-call", "scalp-perp"]);
		await new SyntheticCloseDisputeService(CreateOptions()).DisputeAsync(disputed.Id, "case-perp", "case-call");
		var readModel = CreateDetailReadModel(new StubFreshMarkSource(4300m, FetchedAt));

		// Act: читаем данные обеих конструкций.
		var plainData = await readModel.ReadAsync(plain.Id);
		var disputedData = await readModel.ReadAsync(disputed.Id);

		// Assert: сводной величины нет в обоих чтениях — поле остаётся null.
		Assert.That(plainData.Metrics.SyntheticAdjustedTotalUsdt, Is.Null);
		Assert.That(disputedData.Metrics.SyntheticAdjustedTotalUsdt, Is.Null);
	}

	[TestMethod]
	[Description("Сбой марок не мешает сводной: величина вычисляется при недоступных тикерах")]
	// Локи связок детерминированы к экспирации и от марок не зависят:
	// деградация гасит только нереализованные метрики, сводная остаётся
	// суммой реализованного результата и локов действующих связок.
	// Traceability: change:add-synthetic-adjusted-finresult/specs/domain/synthetic-close/spec#scenario-adjusted-total-marks-failure-independent
	public async Task TryIfSyntheticAdjustedTotalComputedOnMarkFailure()
	{
		// Arrange: кейс XAUT с действующей связкой и сбойным провайдером марок —
		// публичные тикеры недоступны.
		SeedXautInstruments();
		var construction = await _constructionService.CreateAsync("XAUT-без-марок", 1000m);
		await SeedXautCaseAsync();
		await _bindingService.BindBatchAsync(construction.Id, ["open-put", "open-call", "case-perp", "case-call", "scalp-perp"]);
		var readModel = CreateDetailReadModel(new FailingMarkSource());

		// Act: читаем данные экрана деталей при сбое марок.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: сбой марок признан, нереализованная часть отсутствует,
		// а сводная величина собрана из реализованного результата и лока 35.697.
		Assert.That(data.HasMarkFailure, Is.True);
		Assert.That(data.Metrics.UnrealizedPnL, Is.Null);
		Assert.That(data.Metrics.SyntheticAdjustedTotalUsdt, Is.EqualTo(data.Metrics.RealizedPnL + 35.697m).Within(0.001m));
	}

	[TestMethod]
	[Description("Публикация сводной не меняет инструментальных метрик: значения с величиной и без неё совпадают")]
	// Сводная — аддитивное поле поверх готовых метрик: реализованный и
	// нереализованный результат, итог, остатки позиций и признак открытости
	// одинаковы при действующей связке и после её оспаривания, когда сводной нет.
	// Traceability: change:add-synthetic-adjusted-finresult/specs/domain/synthetic-close/spec#scenario-adjusted-total-instrumental-metrics-untouched
	public async Task TryIfSyntheticAdjustedTotalLeavesInstrumentalMetricsUnchanged()
	{
		// Arrange: кейс XAUT с действующей связкой; спор выполняется между
		// чтениями одной и той же конструкции.
		SeedXautInstruments();
		var construction = await _constructionService.CreateAsync("XAUT-инварианты", 1000m);
		await SeedXautCaseAsync();
		await _bindingService.BindBatchAsync(construction.Id, ["open-put", "open-call", "case-perp", "case-call", "scalp-perp"]);
		var readModel = CreateDetailReadModel(new StubFreshMarkSource(4300m, FetchedAt));

		// Act: первое чтение — связка действует; после оспаривания читаем снова.
		var withActiveLink = await readModel.ReadAsync(construction.Id);
		await new SyntheticCloseDisputeService(CreateOptions()).DisputeAsync(construction.Id, "case-perp", "case-call");
		var withoutActiveLink = await readModel.ReadAsync(construction.Id);

		// Assert: сводная публикуется только при действующей связке, а все
		// инструментальные величины между чтениями не изменились.
		Assert.That(withActiveLink.Metrics.SyntheticAdjustedTotalUsdt, Is.Not.Null);
		Assert.That(withoutActiveLink.Metrics.SyntheticAdjustedTotalUsdt, Is.Null);
		Assert.That(withoutActiveLink.Metrics.RealizedPnL, Is.EqualTo(withActiveLink.Metrics.RealizedPnL));
		Assert.That(withoutActiveLink.Metrics.UnrealizedPnL, Is.EqualTo(withActiveLink.Metrics.UnrealizedPnL));
		Assert.That(withoutActiveLink.Metrics.TotalPnL, Is.EqualTo(withActiveLink.Metrics.TotalPnL));
		Assert.That(withoutActiveLink.HasOpenResidual, Is.EqualTo(withActiveLink.HasOpenResidual));
		var perpWith = withActiveLink.Positions.Single(row => row.Symbol == XautPerp);
		var perpWithout = withoutActiveLink.Positions.Single(row => row.Symbol == XautPerp);
		Assert.That(perpWithout.Residual, Is.EqualTo(perpWith.Residual));
		Assert.That(perpWithout.IsOpen, Is.EqualTo(perpWith.IsOpen));
		Assert.That(perpWithout.RealizedPnL, Is.EqualTo(perpWith.RealizedPnL));
		var putWith = withActiveLink.Positions.Single(row => row.Symbol == XautPut);
		var putWithout = withoutActiveLink.Positions.Single(row => row.Symbol == XautPut);
		Assert.That(putWithout.Residual, Is.EqualTo(putWith.Residual));
		Assert.That(putWithout.RealizedPnL, Is.EqualTo(putWith.RealizedPnL));
	}

	[TestMethod]
	[Description("Строки позиций упорядочены: открытые раньше закрытых, далее экспирация, страйк и тип")]
	public async Task TryIfPositionRowsOrderedByStatusExpiryStrikeAndType()
	{
		// Arrange: открытые ноги дальней доски 2030 года и закрытые ноги эталонного
		// примера спеки — доски 25DEC26, 25SEP26 и 29MAY26 с фьючерсом ETHUSDT.
		// Требование: порядок строк — открытые раньше закрытых; внутри группы выше
		// стоит опцион дальней экспирации, при равной экспирации — большего страйка,
		// при равном страйке — CALL раньше PUT; фьючерс идёт в конце группы.
		// Traceability: openspec:ui/screens#scenario-detail-positions-ordered-by-status-and-type
		SeedOrderingInstruments();
		var construction = await _constructionService.CreateAsync("Сортировка ног", 1000m);
		await AddRawExecutionAsync("o1", OpenCallHigh, "option", "Buy", "1", "100", ExecMs(2029, 12, 20, 10, 0), "USDC");
		await AddRawExecutionAsync("o2", OpenPutHigh, "option", "Buy", "1", "100", ExecMs(2029, 12, 20, 10, 0), "USDC");
		await AddRawExecutionAsync("o3", OpenCallLow, "option", "Buy", "1", "100", ExecMs(2029, 12, 20, 10, 0), "USDC");

		// Закрытая нога — покупка и встречная продажа до поставки: нулевой остаток
		// исключает автозакрытие прошедших экспираций и делает набор детерминированным.
		async Task<string[]> CloseLegAsync(string prefix, string symbol, string category, string feeCurrency)
		{
			var buyExecId = $"{prefix}-buy";
			var sellExecId = $"{prefix}-sell";
			await AddRawExecutionAsync(buyExecId, symbol, category, "Buy", "1", "100", ExecMs(2026, 5, 20, 10, 0), feeCurrency);
			await AddRawExecutionAsync(sellExecId, symbol, category, "Sell", "1", "100", ExecMs(2026, 5, 21, 10, 0), feeCurrency);
			return new[] { buyExecId, sellExecId };
		}

		var execIds = new List<string> { "o1", "o2", "o3" };
		execIds.AddRange(await CloseLegAsync("dec2200c", DecCall2200, "option", "USDC"));
		execIds.AddRange(await CloseLegAsync("dec2200p", DecPut2200, "option", "USDC"));
		execIds.AddRange(await CloseLegAsync("dec1900c", DecCall1900, "option", "USDC"));
		execIds.AddRange(await CloseLegAsync("dec1900p", DecPut1900, "option", "USDC"));
		execIds.AddRange(await CloseLegAsync("sep2200c", SepCall2200, "option", "USDC"));
		execIds.AddRange(await CloseLegAsync("sep2200p", SepPut2200, "option", "USDC"));
		execIds.AddRange(await CloseLegAsync("sep1900c", SepCall1900, "option", "USDC"));
		execIds.AddRange(await CloseLegAsync("sep1800c", SepCall1800, "option", "USDC"));
		execIds.AddRange(await CloseLegAsync("sep1800p", SepPut1800, "option", "USDC"));
		execIds.AddRange(await CloseLegAsync("may1900c", MayCall1900, "option", "USDC"));
		execIds.AddRange(await CloseLegAsync("may1900p", MayPut1900, "option", "USDC"));
		execIds.AddRange(await CloseLegAsync("perp", EthPerpSymbol, "linear", "USDT"));
		await _bindingService.BindBatchAsync(construction.Id, execIds);
		var readModel = CreateDetailReadModel(new StubFreshMarkSource(200m, FetchedAt));

		// Act: читаем таблицу позиций конструкции.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: открытые ноги — при равной экспирации выше страйк 2500, CALL
		// раньше PUT; закрытые ноги повторяют эталонный пример спеки: 25DEC26
		// выше 25SEP26, серия 25SEP26 выше 29MAY26, внутри серии большие страйки
		// и CALL раньше PUT, фьючурс ниже всех опционов.
		Assert.That(data.Positions.Select(position => position.Symbol).ToArray(), Is.EqualTo(new[]
		{
			OpenCallHigh,
			OpenPutHigh,
			OpenCallLow,
			DecCall2200,
			DecPut2200,
			DecCall1900,
			DecPut1900,
			SepCall2200,
			SepPut2200,
			SepCall1900,
			SepCall1800,
			SepPut1800,
			MayCall1900,
			MayPut1900,
			EthPerpSymbol,
		}));
		// Первые три строки открыты, остальные закрыты.
		Assert.That(data.Positions.Take(3).Select(position => position.IsOpen), Is.All.True);
		Assert.That(data.Positions.Skip(3).Select(position => position.IsOpen), Is.All.False);
	}

	[TestMethod]
	[Description("Закрывающие записи перечисляются с типом, инструментом и суммой закрытия")]
	public async Task TryIfClosingEntriesListedWithKindSymbolAndAmount()
	{
		// Arrange: две конструкции — опционная с delivery-закрытием (внутренняя
		// стоимость 1000) и линейная с ручной пометкой по цене пользователя.
		var optionConstruction = await _constructionService.CreateAsync("Колл BTC", 700m);
		await AddOptionTradeAsync("exec-call-buy", "Buy", "0.0001", "100");
		await _bindingService.BindAsync(optionConstruction.Id, "exec-call-buy");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.RawDeliveries.Add(Delivery(deliveryPrice: "46000", strike: "45000", fee: "0", deliveryRpl: "0.09"));
			await db.SaveChangesAsync();
		}

		var linearConstruction = await _constructionService.CreateAsync("Пердл BTC", 300m);
		await AddLinearTradeAsync("exec-linear-buy", "Buy", "0.01", "42000", "0", ExecMs(2023, 12, 28, 10, 0));
		await _bindingService.BindAsync(linearConstruction.Id, "exec-linear-buy");
		var markedAt = new DateTimeOffset(2023, 12, 30, 10, 0, 0, TimeSpan.Zero);
		await _markService.AddAsync(linearConstruction.Id, LinearSymbol, markedAt, 42100m);

		var readModel = CreateDetailReadModel(new StubFreshMarkSource(44000m, FetchedAt));

		// Act: читаем детали обеих конструкций.
		var optionData = await readModel.ReadAsync(optionConstruction.Id);
		var linearData = await readModel.ReadAsync(linearConstruction.Id);

		// Assert: таблица закрывающих записей перечисляет delivery-запись
		// и ручную пометку с типом, инструментом и суммой — денежным потоком
		// закрытия по эффективной цене.
		// Требование: закрывающие записи показываются при их наличии.
		// Traceability: openspec:ui/screens#scenario-detail-closing-entries-shown
		var delivery = optionData.ClosingEntries.Single();
		Assert.That(delivery.Kind, Is.EqualTo(PositionClosingKind.Delivery));
		Assert.That(delivery.Symbol, Is.EqualTo(CallSymbol));
		Assert.That(delivery.ClosedAt, Is.EqualTo(OptionDelivery));
		Assert.That(delivery.Quantity, Is.EqualTo(-0.0001m));
		Assert.That(delivery.Price, Is.EqualTo(1000m));
		Assert.That(delivery.AmountUsdt, Is.EqualTo(0.1m));

		var mark = linearData.ClosingEntries.Single();
		Assert.That(mark.Kind, Is.EqualTo(PositionClosingKind.ManualMark));
		Assert.That(mark.Symbol, Is.EqualTo(LinearSymbol));
		Assert.That(mark.Quantity, Is.EqualTo(-0.01m));
		Assert.That(mark.Price, Is.EqualTo(42100m));
		Assert.That(mark.AmountUsdt, Is.EqualTo(421m));
	}

	[TestMethod]
	[Description("Пустая конструкция даёт четыре пустые таблицы — экран покажет сообщения об отсутствии")]
	public async Task TryIfEmptyConstructionGivesFourEmptyTables()
	{
		// Arrange: конструкция без сделок, пометок и корректировок.
		var construction = await _constructionService.CreateAsync("Пустая", 500m);
		var readModel = CreateDetailReadModel(new StubFreshMarkSource(44000m, FetchedAt));

		// Act: читаем данные экрана деталей.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: все четыре таблицы пусты — экран показывает явное сообщение
		// об отсутствии записей, а не пустую разметку; периода нет.
		// Требование: пустая таблица показывает явное сообщение.
		// Traceability: openspec:ui/screens#scenario-detail-empty-table-message
		Assert.That(data.Positions, Is.Empty);
		Assert.That(data.Trades, Is.Empty);
		Assert.That(data.ClosingEntries, Is.Empty);
		Assert.That(data.Adjustments, Is.Empty);
		Assert.That(data.Metrics.OpenedAt, Is.Null);
		Assert.That(data.Metrics.Duration, Is.Null);
		Assert.That(data.HasOpenResidual, Is.False);
	}

	[TestMethod]
	[Description("Корректировки перечисляются по дате с источником и суммой, входя в метрики сводки")]
	public async Task TryIfAdjustmentsOrderedByDateWithSourceAndAmount()
	{
		// Arrange: конструкция с двумя корректировками — ручной −3 от 5 января
		// и роботной +12.5 от 3 января; в базу внесены в обратном порядке.
		var construction = await _constructionService.CreateAsync("Календарь сентябрь", 3000m);
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.PnLAdjustments.Add(new PnLAdjustment
			{
				ConstructionId = construction.Id,
				Date = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero),
				Source = PnLAdjustmentSource.Manual,
				AmountUsdt = -3m,
				Comment = "правка округления",
			});
			db.PnLAdjustments.Add(new PnLAdjustment
			{
				ConstructionId = construction.Id,
				Date = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.Zero),
				Source = PnLAdjustmentSource.Robot,
				AmountUsdt = 12.5m,
				Comment = null,
			});
			await db.SaveChangesAsync();
		}

		var readModel = CreateDetailReadModel(new StubFreshMarkSource(44000m, FetchedAt));

		// Act: читаем данные экрана деталей.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: строки таблицы корректировок упорядочены по дате и несут
		// источник, сумму и описание; сумма корректировок входит в метрики сводки.
		Assert.That(data.Adjustments.Select(adjustment => adjustment.AmountUsdt).ToArray(), Is.EqualTo(new decimal[] { 12.5m, -3m }));
		Assert.That(data.Adjustments[0].Source, Is.EqualTo(PnLAdjustmentSource.Robot));
		Assert.That(data.Adjustments[0].Description, Is.Null);
		Assert.That(data.Adjustments[1].Source, Is.EqualTo(PnLAdjustmentSource.Manual));
		Assert.That(data.Adjustments[1].Description, Is.EqualTo("правка округления"));
		Assert.That(data.Metrics.AdjustmentsPnL, Is.EqualTo(9.5m));
	}

	[TestMethod]
	[Description("Сбой марок деградирует только нереализованную часть и отметку времени марок")]
	public async Task TryIfMarkFailureDegradesUnrealizedAndMarksOnly()
	{
		// Arrange: конструкция с открытым остатком 0.1 BTC; заглушка провайдера
		// марок имитирует недоступность публичных тикеров ошибкой сети.
		var construction = await _constructionService.CreateAsync("Длинный BTC", 1000m);
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await _bindingService.BindAsync(construction.Id, "exec-buy");
		var readModel = CreateDetailReadModel(new FailingMarkSource());

		// Act: чтение данных экрана деталей при сбойном провайдере.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: сбой марок оставил нереализованную оценку и итог непостроенными
		// с признаком сбоя и неизвестной отметкой времени; реализованный результат
		// (−1 комиссия) виден; строка позиции без общего P&L и его процента.
		// Требование: нереализованные величины сопровождаются отметкой времени
		// марок, сбой показывается признаком; общий PnL открытой позиции при сбое —
		// null вместе с нереализованной частью.
		// Traceability: openspec:ui/screens#scenario-detail-summary-metrics-period
		// Traceability: openspec:analytics/performance#scenario-position-total-pnl-mark-failure
		Assert.That(data.HasMarkFailure, Is.True);
		Assert.That(data.HasOpenResidual, Is.True);
		Assert.That(data.MarksAsOf, Is.Null);
		Assert.That(data.Metrics.UnrealizedPnL, Is.Null);
		Assert.That(data.Metrics.TotalPnL, Is.Null);
		Assert.That(data.Metrics.RealizedPnL, Is.EqualTo(-1m));
		var position = data.Positions.Single();
		Assert.That(position.TotalPnL, Is.Null);
		Assert.That(position.TotalPnLPercent, Is.Null);
		// Раздельные части при сбое марок: нереализованная деградирует в null
		// вместе с общим P&L и своим процентом, реализованный результат остаётся
		// видимым с процентом от капитала (−1 от 1000 — −0.1%).
		// Требование: сбой марок не подменяет реализованные величины.
		// Traceability: openspec:ui/screens#scenario-detail-position-pnl-parts
		Assert.That(position.RealizedPnL, Is.EqualTo(-1m));
		Assert.That(position.RealizedPnLPercent, Is.EqualTo(-0.1m));
		Assert.That(position.UnrealizedPnL, Is.Null);
		Assert.That(position.UnrealizedPnLPercent, Is.Null);
		Assert.That(position.Residual, Is.EqualTo(0.1m));
		Assert.That(position.AverageEntryPrice, Is.EqualTo(42000m));
		// Стоимость и процент изменения цены открытой ноги при сбое марок — null
		// наряду с нереализованной частью; реализованный результат и средняя
		// цена входа остаются живыми.
		// Требование: сбой марок гасит производные оценки остатка.
		// Traceability: openspec:analytics/performance#scenario-mark-failure-nulls-price-change
		Assert.That(position.MarkValue, Is.Null);
		Assert.That(position.PriceChangePercent, Is.Null);
	}

	[TestMethod]
	[Description("Строка ручной пометки несёт идентификатор для правки и удаления из закрывающих записей")]
	public async Task TryIfManualMarkRowCarriesMarkId()
	{
		// Arrange: конструкция с покупкой 0.01 BTC и ручной пометкой закрытия
		// по цене пользователя.
		var construction = await _constructionService.CreateAsync("Пердл BTC", 300m);
		await AddLinearTradeAsync("exec-linear-buy", "Buy", "0.01", "42000", "0", ExecMs(2023, 12, 28, 10, 0));
		await _bindingService.BindAsync(construction.Id, "exec-linear-buy");
		var mark = await _markService.AddAsync(
			construction.Id,
			LinearSymbol,
			new DateTimeOffset(2023, 12, 30, 10, 0, 0, TimeSpan.Zero),
			42100m);
		var readModel = CreateDetailReadModel(new StubFreshMarkSource(44000m, FetchedAt));

		// Act: читаем данные экрана деталей.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: строка закрывающих записей с ручной пометкой несёт её
		// идентификатор — экран правит и удаляет пометку из таблицы по нему.
		// Требование: поставленная пометка правится и удаляется из закрывающих записей.
		// Traceability: openspec:ui/screens#requirement-manual-close-mark-from-position
		var row = data.ClosingEntries.Single();
		Assert.That(row.Kind, Is.EqualTo(PositionClosingKind.ManualMark));
		Assert.That(row.ManualMarkId, Is.EqualTo(mark.Id));
	}

	[TestMethod]
	[Description("Пометка к нулевому остатку доходит предупреждением об избыточной записи")]
	public async Task TryIfRedundantMarkWarningSurfacedToDetail()
	{
		// Arrange: позиция BTCUSDT закрыта встречными сделками (остаток 0);
		// поверх поставлена ручная пометка — остаток на её момент уже нулевой.
		var construction = await _constructionService.CreateAsync("Пердл BTC", 300m);
		await AddLinearTradeAsync("exec-buy", "Buy", "0.01", "42000", "0", ExecMs(2023, 12, 28, 10, 0));
		await AddLinearTradeAsync("exec-sell", "Sell", "0.01", "42500", "0", ExecMs(2023, 12, 28, 11, 0));
		await _bindingService.BindBatchAsync(construction.Id, ["exec-buy", "exec-sell"]);
		await _markService.AddAsync(
			construction.Id,
			LinearSymbol,
			new DateTimeOffset(2023, 12, 30, 10, 0, 0, TimeSpan.Zero),
			42100m);
		var readModel = CreateDetailReadModel(new StubFreshMarkSource(44000m, FetchedAt));

		// Act: читаем данные экрана деталей.
		var data = await readModel.ReadAsync(construction.Id);

		// Assert: избыточная пометка не попала в таблицу закрывающих записей,
		// а дошла предупреждением — экран показывает его пользователю.
		// Требование: избыточная закрывающая запись предупреждает.
		// Traceability: openspec:ui/screens#scenario-redundant-closing-entry-warned
		Assert.That(data.ClosingEntries, Is.Empty);
		var warning = data.ClosingWarnings.Single();
		Assert.That(warning.Kind, Is.EqualTo(PositionClosingKind.ManualMark));
		Assert.That(warning.Symbol, Is.EqualTo(LinearSymbol));
		Assert.That(warning.ClosedAt, Is.EqualTo(new DateTimeOffset(2023, 12, 30, 10, 0, 0, TimeSpan.Zero)));
		Assert.That(warning.SourceKey, Does.StartWith("manual:"));
	}

	[TestMethod]
	[Description("Перечень позиций любой конструкции содержит опционную позицию, включая закрытые с нулевым остатком")]
	public async Task TryIfEveryConstructionListsOptionPositionIncludingZeroResidual()
	{
		// Arrange: сырьё двух конструкций — затухающей (прикрытие продано, фьючерс
		// +0.5 остался) и живого стреддла дальней доски; пересбор собирает их
		// и привязывает все сделки.
		SeedAssemblyRawStorage();
		var assembly = new ConstructionAssemblyService(
			new JournalSyncStore(CreateOptions()),
			new StubJournalBackupService(),
			CreateOptions(),
			new TransactionJournal.Infrastructure.Chats.ChatStore(
				Path.Combine(Path.GetTempPath(), $"chat-detail-tests-{Guid.NewGuid():N}.db")),
			new FixedTimeProvider(AssemblyNow));
		await assembly.RebuildAsync();

		using (var db = new JournalDbContext(CreateOptions()))
		{
			var assembled = db.Constructions.OrderBy(construction => construction.Id).ToList();
			Assert.That(assembled, Has.Count.EqualTo(2), "Сырьё собрано в две конструкции");
			Assert.That(assembled[0].Status, Is.EqualTo(ConstructionStatus.Open), "Затухающая конструкция с живым фьючерсом открыта");
		}

		var readModel = CreateDetailReadModel(new StubFreshMarkSource(3000m, FetchedAt));

		// Act: пользователь открывает состав позиций каждой конструкции.
		var positionsByConstruction = new List<IReadOnlyList<ConstructionPositionRow>>();
		using (var db = new JournalDbContext(CreateOptions()))
		{
			foreach (var construction in db.Constructions.OrderBy(construction => construction.Id).ToList())
			{
				var data = await readModel.ReadAsync(construction.Id);
				positionsByConstruction.Add(data.Positions);
			}
		}

		// Assert: инвариант опционной основы — у каждой конструкции в перечне
		// позиций присутствует хотя бы одна опционная позиция.
		// Требование: конструкция не существует без опционной основы, перечень
		// позиций всегда содержит опционный инструмент её базового актива.
		// Traceability: change:close-construction-on-all-positions/specs/domain/construction-assembly/spec#scenario-construction-always-has-option-position
		foreach (var positions in positionsByConstruction)
		{
			Assert.That(
				positions.Any(position => OptionSymbolParser.TryParse(position.Symbol, out _)),
				Is.True,
				"Перечень позиций конструкции содержит опционную позицию");
		}

		// Assert: у затухающей конструкции опционные позиции показываются закрытыми
		// с нулевым остатком, живой фьючерс — открытой позицией 0.5; открытый
		// фьючерс по новому порядку строк идёт раньше закрытых опционов.
		// Traceability: openspec:ui/screens#scenario-detail-positions-ordered-by-status-and-type
		var fading = positionsByConstruction[0];
		Assert.That(fading.Select(position => position.Symbol).ToArray(), Is.EqualTo(new[]
		{
			EthPerpSymbol,
			EthCallSep,
			EthPutSep,
		}));
		Assert.That(fading.Single(position => position.Symbol == EthCallSep).Residual, Is.EqualTo(0m), "Проданный колл показывается нулевой позицией");
		Assert.That(fading.Single(position => position.Symbol == EthCallSep).IsOpen, Is.False);
		Assert.That(fading.Single(position => position.Symbol == EthPutSep).Residual, Is.EqualTo(0m), "Проданный пут показывается нулевой позицией");
		Assert.That(fading.Single(position => position.Symbol == EthPutSep).IsOpen, Is.False);
		Assert.That(fading.Single(position => position.Symbol == EthPerpSymbol).Residual, Is.EqualTo(0.5m), "Фьючерсный остаток затухающей конструкции жив");

		// Assert: живой стреддл показывает обе опционные позиции с ненулевым остатком.
		var live = positionsByConstruction[1];
		Assert.That(live.Select(position => position.Symbol).ToArray(), Is.EqualTo(new[]
		{
			EthCallDec27,
			EthPutDec27,
		}));
		Assert.That(live.Single(position => position.Symbol == EthCallDec27).Residual, Is.EqualTo(1m));
		Assert.That(live.Single(position => position.Symbol == EthPutDec27).Residual, Is.EqualTo(1m));
		Assert.That(live.All(position => position.IsOpen), Is.True);
	}

	[TestMethod]
	[Description("Чтение деталей неизвестной конструкции отказывает")]
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public async Task ThrowOnUnknownConstruction()
	{
		// Arrange — Act: детали несуществующей конструкции.
		var readModel = CreateDetailReadModel(new StubFreshMarkSource(44000m, FetchedAt));
		await readModel.ReadAsync(999);
	}

	[TestMethod]
	[Description("Null-опции контекста отклоняются конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullOptions()
	{
		// Arrange — Act — Assert
		new ConstructionDetailReadModel(
			null!,
			new JournalMetricsReadModel(CreateOptions(), new StubFreshMarkSource(44000m, FetchedAt)),
			new PositionReadModel(CreateOptions()));
	}

	[TestMethod]
	[Description("Null-модель метрик отклоняется конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullMetrics()
	{
		// Arrange — Act — Assert
		new ConstructionDetailReadModel(CreateOptions(), null!, new PositionReadModel(CreateOptions()));
	}

	[TestMethod]
	[Description("Null-модель позиций отклоняется конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullPositions()
	{
		// Arrange — Act — Assert
		new ConstructionDetailReadModel(
			CreateOptions(),
			new JournalMetricsReadModel(CreateOptions(), new StubFreshMarkSource(44000m, FetchedAt)),
			null!);
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Создаёт настоящее хранилище консультаций над временной папкой проверки.</summary>
	/// <summary>Собирает read-модель деталей над реальными метриками журнала и позициями.</summary>
	private ConstructionDetailReadModel CreateDetailReadModel(IFreshInstrumentMarkSource freshMarkSource) => new(
		CreateOptions(),
		new JournalMetricsReadModel(CreateOptions(), freshMarkSource),
		new PositionReadModel(CreateOptions()));

	/// <summary>
	/// Собирает read-модель деталей над фиксированными часами: остаток суток до
	/// экспирации и давность снимков истории проверяются без привязки к системному
	/// времени.
	/// </summary>
	private ConstructionDetailReadModel CreateDetailReadModel(IFreshInstrumentMarkSource freshMarkSource, TimeProvider timeProvider) => new(
		CreateOptions(),
		new JournalMetricsReadModel(CreateOptions(), freshMarkSource, timeProvider: timeProvider),
		new PositionReadModel(CreateOptions()));

	/// <summary>Добавляет живой опцион сценария времени жизни в справочник инструментов: доска 25JUN27 08:00 UTC.</summary>
	private async Task SeedFutureCallInstrumentAsync()
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawInstruments.Add(OptionInstrument(FutureCallSymbol, "Call", ExecMs(2027, 6, 25, 8, 0)));
		await db.SaveChangesAsync();
	}

	/// <summary>Пишет строку истории рыночных снимков инструмента с индексной ценой, тетой и моментом получения.</summary>
	private async Task AddMarketSnapshotAsync(string symbol, decimal markPrice, decimal theta, DateTimeOffset receivedAt)
	{
		using var db = new JournalDbContext(CreateOptions());
		db.InstrumentMarketSnapshots.Add(new InstrumentMarketSnapshot
		{
			Symbol = symbol,
			MarkPrice = markPrice,
			UnderlyingPrice = 110000m,
			Theta = theta,
			ReceivedAt = receivedAt,
		});
		await db.SaveChangesAsync();
	}

	/// <summary>Справочник инструментов: опцион BTC с delivery 29DEC23 08:00 UTC и линейный перп BTCUSDT.</summary>
	private static void SeedInstrumentCatalog(JournalDbContext db)
	{
		var optionPayload =
			$$"""{"symbol":"{{CallSymbol}}","baseCoin":"BTC","quoteCoin":"USD","settleCoin":"USDC","status":"Trading","optionsType":"Call","deliveryTime":"{{OptionDeliveryMs}}","deliveryFeeRate":"0.00015"}""";
		var linearPayload =
			"""{"symbol":"BTCUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"BTC","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""";
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = CallSymbol,
			Category = "option",
			PayloadJson = optionPayload,
			FetchedAt = FetchedAt,
		});
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = LinearSymbol,
			Category = "linear",
			PayloadJson = linearPayload,
			FetchedAt = FetchedAt,
		});
		db.SaveChanges();
	}

	/// <summary>Добавляет сырую запись исполнения линейного перпа BTCUSDT; числа биржа шлёт строками.</summary>
	private async Task AddLinearTradeAsync(string execId, string side, string execQty, string execPrice, string execFee, long execTimeMs)
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawExecutions.Add(new RawExecution
		{
			ExecId = execId,
			Category = "linear",
			Symbol = LinearSymbol,
			ExecTimeMs = execTimeMs,
			PayloadJson = $$"""{"symbol":"{{LinearSymbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"{{execFee}}","execId":"{{execId}}","execPrice":"{{execPrice}}","execQty":"{{execQty}}","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":"USDT","isMaker":false}""",
			FetchedAt = FetchedAt,
		});
		await db.SaveChangesAsync();
	}

	/// <summary>Добавляет сырую запись исполнения опциона BTC-29DEC23-45000-C.</summary>
	private async Task AddOptionTradeAsync(string execId, string side, string execQty, string execPrice)
	{
		var execTimeMs = ExecMs(2023, 12, 28, 10, 0);
		using var db = new JournalDbContext(CreateOptions());
		db.RawExecutions.Add(new RawExecution
		{
			ExecId = execId,
			Category = "option",
			Symbol = CallSymbol,
			ExecTimeMs = execTimeMs,
			PayloadJson = $$"""{"symbol":"{{CallSymbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"0","execId":"{{execId}}","execPrice":"{{execPrice}}","execQty":"{{execQty}}","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":"USDC","isMaker":false}""",
			FetchedAt = FetchedAt,
		});
		await db.SaveChangesAsync();
	}

	/// <summary>Delivery-запись колла в форме ответа delivery-record: числа биржа шлёт строками.</summary>
	private static RawDelivery Delivery(string deliveryPrice, string strike, string fee, string deliveryRpl)
	{
		var payload =
			$$"""{"symbol":"{{CallSymbol}}","side":"Buy","deliveryTime":"{{OptionDeliveryMs}}","entryPrice":"100","deliveryPrice":"{{deliveryPrice}}","strike":"{{strike}}","fee":"{{fee}}","position":"0.0001","deliveryRpl":"{{deliveryRpl}}"}""";
		return new RawDelivery
		{
			Symbol = CallSymbol,
			DeliveryTimeMs = OptionDeliveryMs,
			Category = "option",
			PayloadJson = payload,
			FetchedAt = FetchedAt,
		};
	}

	private static long ExecMs(int year, int month, int day, int hour, int minute) =>
		new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	/// <summary>Наполняет сырьё двух конструкций: затухающий стреддл 25SEP26 с фьючерсом и живой стреддл 25DEC27.</summary>
	private void SeedAssemblyRawStorage()
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawInstruments.Add(EthOptionInstrument(EthCallSep, "Call", ExecMs(2026, 9, 25, 8, 0)));
		db.RawInstruments.Add(EthOptionInstrument(EthPutSep, "Put", ExecMs(2026, 9, 25, 8, 0)));
		db.RawInstruments.Add(EthOptionInstrument(EthCallDec27, "Call", ExecMs(2027, 12, 25, 8, 0)));
		db.RawInstruments.Add(EthOptionInstrument(EthPutDec27, "Put", ExecMs(2027, 12, 25, 8, 0)));
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = EthPerpSymbol,
			Category = "linear",
			PayloadJson = """{"symbol":"ETHUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"ETH","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""",
			FetchedAt = FetchedAt,
		});

		db.RawExecutions.Add(EthExecution("f1", EthCallSep, ExecMs(2026, 7, 10, 9, 0), "Buy", "1", "100", "USDC"));
		db.RawExecutions.Add(EthExecution("f2", EthPutSep, ExecMs(2026, 7, 10, 9, 10), "Buy", "1", "80", "USDC"));
		db.RawExecutions.Add(EthExecution("f3", EthPerpSymbol, ExecMs(2026, 7, 12, 10, 0), "Buy", "0.5", "3000", "USDT"));
		db.RawExecutions.Add(EthExecution("f4", EthCallSep, ExecMs(2026, 8, 3, 10, 0), "Sell", "1", "150", "USDC"));
		db.RawExecutions.Add(EthExecution("f5", EthPutSep, ExecMs(2026, 8, 4, 10, 0), "Sell", "1", "40", "USDC"));
		db.RawExecutions.Add(EthExecution("g1", EthCallDec27, ExecMs(2026, 12, 1, 10, 0), "Buy", "1", "300", "USDC"));
		db.RawExecutions.Add(EthExecution("g2", EthPutDec27, ExecMs(2026, 12, 1, 10, 10), "Buy", "1", "250", "USDC"));
		db.SaveChanges();
	}

	/// <summary>Справочник сценария сортировки: доски 01JAN30, 25DEC26, 25SEP26, 29MAY26 и перп ETHUSDT.</summary>
	// Требование: сценарий сортировки опирается на эталонный пример спеки; дальняя
	// доска 2030 года держит открытые ноги, не полагаясь на прошедшие экспирации.
	// Traceability: openspec:ui/screens#scenario-detail-positions-ordered-by-status-and-type
	private void SeedOrderingInstruments()
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawInstruments.Add(OptionInstrument(OpenCallHigh, "Call", ExecMs(2030, 1, 1, 8, 0)));
		db.RawInstruments.Add(OptionInstrument(OpenPutHigh, "Put", ExecMs(2030, 1, 1, 8, 0)));
		db.RawInstruments.Add(OptionInstrument(OpenCallLow, "Call", ExecMs(2030, 1, 1, 8, 0)));
		db.RawInstruments.Add(OptionInstrument(DecCall2200, "Call", ExecMs(2026, 12, 25, 8, 0)));
		db.RawInstruments.Add(OptionInstrument(DecPut2200, "Put", ExecMs(2026, 12, 25, 8, 0)));
		db.RawInstruments.Add(OptionInstrument(DecCall1900, "Call", ExecMs(2026, 12, 25, 8, 0)));
		db.RawInstruments.Add(OptionInstrument(DecPut1900, "Put", ExecMs(2026, 12, 25, 8, 0)));
		db.RawInstruments.Add(OptionInstrument(SepCall2200, "Call", ExecMs(2026, 9, 25, 8, 0)));
		db.RawInstruments.Add(OptionInstrument(SepPut2200, "Put", ExecMs(2026, 9, 25, 8, 0)));
		db.RawInstruments.Add(OptionInstrument(SepCall1900, "Call", ExecMs(2026, 9, 25, 8, 0)));
		db.RawInstruments.Add(OptionInstrument(SepCall1800, "Call", ExecMs(2026, 9, 25, 8, 0)));
		db.RawInstruments.Add(OptionInstrument(SepPut1800, "Put", ExecMs(2026, 9, 25, 8, 0)));
		db.RawInstruments.Add(OptionInstrument(MayCall1900, "Call", ExecMs(2026, 5, 29, 8, 0)));
		db.RawInstruments.Add(OptionInstrument(MayPut1900, "Put", ExecMs(2026, 5, 29, 8, 0)));
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = EthPerpSymbol,
			Category = "linear",
			PayloadJson = """{"symbol":"ETHUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"ETH","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""",
			FetchedAt = FetchedAt,
		});
		db.SaveChanges();
	}

	/// <summary>Спецификация опциона со settleCoin USDT и каноническим временем доставки 08:00 UTC.</summary>
	private static RawInstrument OptionInstrument(string symbol, string optionsType, long deliveryTimeMs) => new()
	{
		Symbol = symbol,
		Category = "option",
		PayloadJson = $$"""{"symbol":"{{symbol}}","baseCoin":"{{symbol.Split('-')[0]}}","quoteCoin":"USDT","settleCoin":"USDT","status":"Trading","optionsType":"{{optionsType}}","deliveryTime":"{{deliveryTimeMs}}","deliveryFeeRate":"0.00015"}""",
		FetchedAt = FetchedAt,
	};

	/// <summary>Добавляет сырую запись исполнения произвольного инструмента; числа биржа шлёт строками.</summary>
	private async Task AddRawExecutionAsync(string execId, string symbol, string category, string side, string execQty, string execPrice, long execTimeMs, string feeCurrency)
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawExecutions.Add(new RawExecution
		{
			ExecId = execId,
			Category = category,
			Symbol = symbol,
			ExecTimeMs = execTimeMs,
			PayloadJson = $$"""{"symbol":"{{symbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"0","execId":"{{execId}}","execPrice":"{{execPrice}}","execQty":"{{execQty}}","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":"{{feeCurrency}}","isMaker":false}""",
			FetchedAt = FetchedAt,
		});
		await db.SaveChangesAsync();
	}

	/// <summary>Добавляет сырую запись исполнения кейса XAUT с указанной комиссией; числа биржа шлёт строками.</summary>
	private async Task AddXautExecutionAsync(string execId, string symbol, string category, string side, string execQty, string execPrice, string execFee, long execTimeMs, string feeCurrency)
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawExecutions.Add(new RawExecution
		{
			ExecId = execId,
			Category = category,
			Symbol = symbol,
			ExecTimeMs = execTimeMs,
			PayloadJson = $$"""{"symbol":"{{symbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"{{execFee}}","execId":"{{execId}}","execPrice":"{{execPrice}}","execQty":"{{execQty}}","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":"{{feeCurrency}}","isMaker":false}""",
			FetchedAt = FetchedAt,
		});
		await db.SaveChangesAsync();
	}

	/// <summary>Справочник кейса XAUT: пут и колл доски 30OCT26 08:00 UTC и линейный перп XAUTUSDT.</summary>
	private void SeedXautInstruments()
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawInstruments.Add(OptionInstrument(XautPut, "Put", ExecMs(2026, 10, 30, 8, 0)));
		db.RawInstruments.Add(OptionInstrument(XautCall, "Call", ExecMs(2026, 10, 30, 8, 0)));
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = XautPerp,
			Category = "linear",
			PayloadJson = """{"symbol":"XAUTUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"XAUT","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""",
			FetchedAt = FetchedAt,
		});
		db.SaveChanges();
	}

	/// <summary>
	/// Наполняет сырьё эталонного кейса XAUT 15.10.2026: стрэддл 0.5 (пут @ 139
	/// fee 1.5, колл @ 12 fee 0.6), связка 15.10 (перп 0.3 @ 4141.91 fee 0.55,
	/// продажа 0.3 колла @ 5.9 fee 0.35) и скальп 0.04 @ 4200 fee 0.09.
	/// </summary>
	private async Task SeedXautCaseAsync()
	{
		await AddXautExecutionAsync("open-put", XautPut, "option", "Buy", "0.5", "139", "1.5", ExecMs(2026, 9, 30, 12, 0), "USDC");
		await AddXautExecutionAsync("open-call", XautCall, "option", "Buy", "0.5", "12", "0.6", ExecMs(2026, 9, 30, 12, 5), "USDC");
		await AddXautExecutionAsync("case-perp", XautPerp, "linear", "Buy", "0.3", "4141.91", "0.55", ExecMs(2026, 10, 15, 13, 0), "USDC");
		await AddXautExecutionAsync("case-call", XautCall, "option", "Sell", "0.3", "5.9", "0.35", ExecMs(2026, 10, 15, 13, 4), "USDC");
		await AddXautExecutionAsync("scalp-perp", XautPerp, "linear", "Buy", "0.04", "4200", "0.09", ExecMs(2026, 10, 15, 15, 0), "USDC");
	}

	/// <summary>Спецификация опциона ETH в справочнике с каноническим временем доставки 08:00 UTC.</summary>
	private static RawInstrument EthOptionInstrument(string symbol, string optionsType, long deliveryTimeMs) => new()
	{
		Symbol = symbol,
		Category = "option",
		PayloadJson = $$"""{"symbol":"{{symbol}}","baseCoin":"ETH","quoteCoin":"USDT","settleCoin":"USDT","status":"Trading","optionsType":"{{optionsType}}","deliveryTime":"{{deliveryTimeMs}}","deliveryFeeRate":"0.00015"}""",
		FetchedAt = FetchedAt,
	};

	/// <summary>Запись исполнения ETH в форме ответа execution-list: числа биржа шлёт строками.</summary>
	private static RawExecution EthExecution(string execId, string symbol, long execTimeMs, string side, string execQty, string execPrice, string feeCurrency) => new()
	{
		ExecId = execId,
		Category = string.Equals(symbol, EthPerpSymbol, StringComparison.Ordinal) ? "linear" : "option",
		Symbol = symbol,
		ExecTimeMs = execTimeMs,
		PayloadJson = $$"""{"symbol":"{{symbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"0.01","execId":"{{execId}}","execPrice":"{{execPrice}}","execQty":"{{execQty}}","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":"{{feeCurrency}}","isMaker":false}""",
		FetchedAt = FetchedAt,
	};

	/// <summary>Поставщик фиксированного времени для детерминированного пересбора.</summary>
	private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => utcNow;
	}

	/// <summary>
	/// Заглушка источника свежих марок: фиксированная марка со временем получения;
	/// nullable-поля индексной цены и теты имитируют расширенный снимок тикера
	/// опциона — биржа отдаёт их только опционам.
	/// </summary>
	private sealed class StubFreshMarkSource(decimal markPrice, DateTimeOffset receivedAt, decimal? underlyingPrice = null, decimal? theta = null) : IFreshInstrumentMarkSource
	{
		public Task<InstrumentMarkSnapshot?> GetFreshMarkAsync(string symbol, CancellationToken cancellationToken = default) =>
			Task.FromResult<InstrumentMarkSnapshot?>(new InstrumentMarkSnapshot(symbol, markPrice, receivedAt, underlyingPrice, theta));
	}

	/// <summary>
	/// Заглушка провайдера марок для сценария сбоя: свежая марка всегда недоступна —
	/// сеть до публичных тикеров не доходит, аналитика деградирует оценкой, а не ошибкой.
	/// </summary>
	private sealed class FailingMarkSource : IFreshInstrumentMarkSource
	{
		public Task<InstrumentMarkSnapshot?> GetFreshMarkAsync(string symbol, CancellationToken cancellationToken = default) =>
			throw new HttpRequestException("публичные тикеры недоступны");
	}

	#endregion
}
