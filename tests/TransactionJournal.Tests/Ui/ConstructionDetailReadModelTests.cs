using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Analytics;
using TransactionJournal.Components.Pages;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ui;

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

		// Assert: позиция несёт остаток, среднюю, марку и нереализованную оценку
		// с комментарием позиции; сделки перечислены хронологически с атрибутами
		// биржевой записи и комментарием своей сделки.
		var position = data.Positions.Single();
		Assert.That(position.Symbol, Is.EqualTo(LinearSymbol));
		Assert.That(position.Residual, Is.EqualTo(0.06m));
		Assert.That(position.AverageOpenPrice, Is.EqualTo(42000m));
		Assert.That(position.MarkPrice, Is.EqualTo(44000m));
		Assert.That(position.UnrealizedPnL, Is.EqualTo(120m));
		Assert.That(position.IsOpen, Is.True);
		Assert.That(position.Comment, Is.EqualTo("частичный выход"));

		Assert.That(data.Trades.Select(trade => trade.ExecId).ToArray(), Is.EqualTo(new[] { "exec-buy", "exec-sell" }));
		var first = data.Trades[0];
		Assert.That(first.Symbol, Is.EqualTo(LinearSymbol));
		Assert.That(first.IsBuy, Is.True);
		Assert.That(first.Quantity, Is.EqualTo(0.1m));
		Assert.That(first.Price, Is.EqualTo(42000m));
		Assert.That(first.AmountUsdt, Is.EqualTo(4200m));
		Assert.That(first.Fee, Is.EqualTo(1m));
		Assert.That(first.Comment, Is.EqualTo("вход в календарь"));
		var second = data.Trades[1];
		Assert.That(second.IsBuy, Is.False);
		Assert.That(second.AmountUsdt, Is.EqualTo(1700m));
		Assert.That(second.Comment, Is.Null);
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
		// (−1 комиссия) виден; строка позиции без марки и оценки.
		// Требование: нереализованные величины сопровождаются отметкой времени
		// марок, сбой показывается признаком.
		// Traceability: openspec:ui/screens#scenario-detail-summary-metrics-period
		// Traceability: change:add-ui-screens/design#d4
		Assert.That(data.HasMarkFailure, Is.True);
		Assert.That(data.HasOpenResidual, Is.True);
		Assert.That(data.MarksAsOf, Is.Null);
		Assert.That(data.Metrics.UnrealizedPnL, Is.Null);
		Assert.That(data.Metrics.TotalPnL, Is.Null);
		Assert.That(data.Metrics.RealizedPnL, Is.EqualTo(-1m));
		var position = data.Positions.Single();
		Assert.That(position.MarkPrice, Is.Null);
		Assert.That(position.UnrealizedPnL, Is.Null);
		Assert.That(position.Residual, Is.EqualTo(0.1m));
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

	/// <summary>Собирает read-модель деталей над реальными метриками журнала и позициями.</summary>
	private ConstructionDetailReadModel CreateDetailReadModel(IFreshInstrumentMarkSource freshMarkSource) => new(
		CreateOptions(),
		new JournalMetricsReadModel(CreateOptions(), freshMarkSource),
		new PositionReadModel(CreateOptions()));

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

	/// <summary>Заглушка источника свежих марок: фиксированная марка со временем получения.</summary>
	private sealed class StubFreshMarkSource(decimal markPrice, DateTimeOffset receivedAt) : IFreshInstrumentMarkSource
	{
		public Task<InstrumentMarkSnapshot?> GetFreshMarkAsync(string symbol, CancellationToken cancellationToken = default) =>
			Task.FromResult<InstrumentMarkSnapshot?>(new InstrumentMarkSnapshot(symbol, markPrice, receivedAt));
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
