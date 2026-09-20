using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Analytics;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using TransactionJournal.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Analytics;

/// <summary>
/// Интеграционные проверки производности аналитики на стыке с хранилищем журнала:
/// метрики конструкций вычисляются композицией читающего слоя поверх текущих данных —
/// сырых записей синхронизации, привязок, ручных пометок, корректировок и капитала, —
/// поэтому правка любого входа отражается очередным чтением без следов прежнего
/// расчёта, а повторные чтения возвращают те же значения, что и пересчёт.
/// </summary>
[TestClass]
public class AnalyticsOnReadTests
{
	private const string CallSymbol = "BTC-29DEC23-45000-C";

	private const string LinearSymbol = "BTCUSDT";

	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	/// <summary>Каноническое время delivery инструмента опциона из справочника: 29DEC23 08:00 UTC.</summary>
	private static readonly DateTimeOffset OptionDelivery = new(2023, 12, 29, 8, 0, 0, TimeSpan.Zero);

	private static readonly long OptionDeliveryMs = OptionDelivery.ToUnixTimeMilliseconds();

	/// <summary>Текущий момент чтений: 1 января 2026 года 12:00 UTC — граница длительностей открытых позиций.</summary>
	private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;
	private ConstructionService _constructionService = null!;
	private TradeBindingService _bindingService = null!;
	private PnLAdjustmentService _adjustmentService = null!;
	private ManualCloseMarkService _markService = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-analytics-on-read-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
			SeedInstrumentCatalog(db);
		}

		_constructionService = new ConstructionService(CreateOptions());
		_bindingService = new TradeBindingService(CreateOptions());
		_adjustmentService = new PnLAdjustmentService(CreateOptions());
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
	[Description("Правка привязок, корректировок, пометок и капитала отражается при следующем чтении")]
	public async Task TryIfInputEditsAreReflectedByNextRead()
	{
		// Arrange: конструкция с капиталом 1000, закрытая линейная позиция (покупка
		// и продажа 0.1 с комиссиями 1+1), опционная позиция, закрытая ручной пометкой
		// по 90, и корректировка робота +20.
		var construction = await _constructionService.CreateAsync("Скальп BTC", 1000m);
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await AddLinearTradeAsync("exec-sell", "Sell", "0.1", "43000", "1", ExecMs(2023, 12, 28, 11, 0));
		await AddOptionTradeAsync("exec-option", "Buy", "0.0001", "100", "0.02");
		await _bindingService.BindBatchAsync(construction.Id,
			new[] { "exec-buy", "exec-sell", "exec-option" });
		var mark = await _markService.AddAsync(
			construction.Id, CallSymbol, At(2023, 12, 28, 12, 0), price: 90m);
		var adjustment = await _adjustmentService.AddAsync(
			construction.Id, At(2023, 12, 28, 13, 0), PnLAdjustmentSource.Robot, 20m);

		// Act — первое чтение.
		var first = await ReadMetricsAsync();

		// Assert: линейная позиция 100 − 2 комиссии = 98; опционная (90 − 100) × 0.0001 − 0.02
		// комиссии USDC по паритету = −0.021; итог 98 − 0.021 + 20 = 117.979 с процентами
		// от 1000. Хранимых результатов нет — значения выведены из текущих записей.
		// Требование: метрики вычисляются из текущего набора записей при каждом чтении.
		// Traceability: openspec:analytics/performance#requirement-analytics-computed-on-read
		// Traceability: openspec:analytics/performance#scenario-no-stale-results
		var linear = first.Positions.Single(position => position.Symbol == LinearSymbol);
		Assert.That(linear.RealizedPnL, Is.EqualTo(98m));
		Assert.That(linear.AccumulatedFees, Is.EqualTo(2m));
		Assert.That(linear.Residual, Is.EqualTo(0m));
		Assert.That(linear.UnrealizedPnL, Is.EqualTo(0m));
		Assert.That(linear.ClosedAt, Is.EqualTo(At(2023, 12, 28, 11, 0)));
		var option = first.Positions.Single(position => position.Symbol == CallSymbol);
		Assert.That(option.RealizedPnL, Is.EqualTo(-0.021m));
		Assert.That(option.AccumulatedFees, Is.EqualTo(0.02m));
		Assert.That(option.Residual, Is.EqualTo(0m));
		var firstMetrics = first.Constructions.Single(metrics => metrics.ConstructionId == construction.Id);
		Assert.That(firstMetrics.RealizedPnL, Is.EqualTo(97.979m));
		Assert.That(firstMetrics.UnrealizedPnL, Is.EqualTo(0m));
		Assert.That(firstMetrics.AdjustmentsPnL, Is.EqualTo(20m));
		Assert.That(firstMetrics.TotalPnL, Is.EqualTo(117.979m));
		Assert.That(firstMetrics.RealizedPnLPercent, Is.EqualTo(9.7979m));
		Assert.That(firstMetrics.AdjustmentsPnLPercent, Is.EqualTo(2m));
		Assert.That(firstMetrics.TotalPnLPercent, Is.EqualTo(11.7979m));
		Assert.That(firstMetrics.OpenedAt, Is.EqualTo(At(2023, 12, 28, 10, 0)));
		Assert.That(firstMetrics.ClosedAt, Is.EqualTo(At(2023, 12, 28, 12, 0)));
		Assert.That(firstMetrics.Duration, Is.EqualTo(TimeSpan.FromHours(2)));

		// Act — правка пометки: цена закрытия опциона 90 → 2000.
		await _markService.EditAsync(mark.Id, CallSymbol, At(2023, 12, 28, 12, 0), 2000m);
		var afterMarkEdit = await ReadMetricsAsync();

		// Assert: опционная позиция пересчиталась — (2000 − 100) × 0.0001 − 0.02 = 0.17;
		// линейная не изменилась, итог подхватил новое слагаемое.
		// Требование: правка пометки отражается очередным чтением.
		// Traceability: openspec:analytics/performance#scenario-no-stale-results
		var afterMarkEditMetrics = afterMarkEdit.Constructions.Single(metrics => metrics.ConstructionId == construction.Id);
		Assert.That(afterMarkEditMetrics.RealizedPnL, Is.EqualTo(98.17m));
		Assert.That(afterMarkEditMetrics.TotalPnL, Is.EqualTo(118.17m));
		Assert.That(afterMarkEdit.Positions.Single(position => position.Symbol == LinearSymbol).RealizedPnL, Is.EqualTo(98m));

		// Act — правка корректировки: сумма робота +20 → −10.
		await _adjustmentService.EditAsync(
			adjustment.Id, At(2023, 12, 28, 13, 0), PnLAdjustmentSource.Robot, -10m, null);
		var afterAdjustmentEdit = await ReadMetricsAsync();

		// Assert: корректировка вошла новым значением, реализованный результат не тронут.
		// Требование: правка корректировки отражается очередным чтением.
		// Traceability: openspec:analytics/performance#scenario-no-stale-results
		var afterAdjustmentEditMetrics = afterAdjustmentEdit.Constructions.Single(metrics => metrics.ConstructionId == construction.Id);
		Assert.That(afterAdjustmentEditMetrics.AdjustmentsPnL, Is.EqualTo(-10m));
		Assert.That(afterAdjustmentEditMetrics.TotalPnL, Is.EqualTo(88.17m));
		Assert.That(afterAdjustmentEditMetrics.RealizedPnL, Is.EqualTo(98.17m));

		// Act — правка капитала: 1000 → 2000.
		await _constructionService.UpdateAllocatedCapitalAsync(construction.Id, 2000m);
		var afterCapitalEdit = await ReadMetricsAsync();

		// Assert: капитал меняет только проценты, абсолютные величины не тронуты.
		// Требование: правка капитала отражается очередным чтением.
		// Traceability: openspec:analytics/performance#scenario-no-stale-results
		// Traceability: openspec:analytics/performance#scenario-percent-from-current-capital
		var afterCapitalEditMetrics = afterCapitalEdit.Constructions.Single(metrics => metrics.ConstructionId == construction.Id);
		Assert.That(afterCapitalEditMetrics.AllocatedCapitalUsdt, Is.EqualTo(2000m));
		Assert.That(afterCapitalEditMetrics.RealizedPnL, Is.EqualTo(98.17m));
		Assert.That(afterCapitalEditMetrics.TotalPnL, Is.EqualTo(88.17m));
		Assert.That(afterCapitalEditMetrics.RealizedPnLPercent, Is.EqualTo(4.9085m));
		Assert.That(afterCapitalEditMetrics.AdjustmentsPnLPercent, Is.EqualTo(-0.5m));
		Assert.That(afterCapitalEditMetrics.TotalPnLPercent, Is.EqualTo(4.4085m));

		// Act — правка привязки: продажа возвращается во «Входящие».
		await _bindingService.UnbindAsync("exec-sell");
		var afterUnbind = await ReadMetricsAsync();

		// Assert: линейная позиция открыта покупкой — реализованный остаток −1 (комиссия),
		// нереализованная часть и итог деградируют в null, даты закрытия нет; опционная
		// позиция и корректировки не затронуты, устаревшего итога 88.17 не осталось.
		// Требование: правка привязки отражается очередным чтением.
		// Traceability: openspec:analytics/performance#scenario-no-stale-results
		var afterUnbindMetrics = afterUnbind.Constructions.Single(metrics => metrics.ConstructionId == construction.Id);
		Assert.That(afterUnbindMetrics.RealizedPnL, Is.EqualTo(-0.83m));
		Assert.That(afterUnbindMetrics.UnrealizedPnL, Is.Null);
		Assert.That(afterUnbindMetrics.TotalPnL, Is.Null);
		Assert.That(afterUnbindMetrics.TotalPnLPercent, Is.Null);
		Assert.That(afterUnbindMetrics.AdjustmentsPnL, Is.EqualTo(-10m));
		Assert.That(afterUnbindMetrics.ClosedAt, Is.Null);
		Assert.That(afterUnbindMetrics.Duration, Is.EqualTo(Now - At(2023, 12, 28, 10, 0)));
		var reopenedLinear = afterUnbind.Positions.Single(position => position.Symbol == LinearSymbol);
		Assert.That(reopenedLinear.Residual, Is.EqualTo(0.1m));
		Assert.That(reopenedLinear.AverageOpenPrice, Is.EqualTo(42000m));
		Assert.That(reopenedLinear.ClosedAt, Is.Null);

		// Act — возврат продажи в конструкцию.
		await _bindingService.BindAsync(construction.Id, "exec-sell");
		var afterRebind = await ReadMetricsAsync();

		// Assert: чтение вернулось к значениям до снятия привязки — следов прежнего
		// расчёта не осталось, значения выводятся из текущего набора записей.
		var afterRebindMetrics = afterRebind.Constructions.Single(metrics => metrics.ConstructionId == construction.Id);
		Assert.That(afterRebindMetrics.RealizedPnL, Is.EqualTo(98.17m));
		Assert.That(afterRebindMetrics.UnrealizedPnL, Is.EqualTo(0m));
		Assert.That(afterRebindMetrics.TotalPnL, Is.EqualTo(88.17m));
		Assert.That(afterRebindMetrics.ClosedAt, Is.EqualTo(At(2023, 12, 28, 12, 0)));
		Assert.That(afterRebindMetrics.Duration, Is.EqualTo(TimeSpan.FromHours(2)));
	}

	[TestMethod]
	[Description("Повторные чтения возвращают значения, равные пересчитанным из текущих данных")]
	public async Task TryIfRepeatedReadsMatchRecomputedValues()
	{
		// Arrange: конструкция с закрытой линейной позицией и корректировкой.
		var construction = await _constructionService.CreateAsync("Скальп BTC", 1000m);
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await AddLinearTradeAsync("exec-sell", "Sell", "0.1", "43000", "1", ExecMs(2023, 12, 28, 11, 0));
		await _bindingService.BindBatchAsync(construction.Id, new[] { "exec-buy", "exec-sell" });
		await _adjustmentService.AddAsync(
			construction.Id, At(2023, 12, 28, 12, 0), PnLAdjustmentSource.Manual, 20m);

		// Act: два независимых чтения подряд — свежие контексты, свежие калькуляторы.
		var first = await ReadMetricsAsync();
		var second = await ReadMetricsAsync();

		// Assert: оба чтения побитово совпадают — между чтениями нет ни хранимых
		// результатов, ни промежуточного состояния, поэтому любые возвращённые
		// значения равны пересчитанным из текущих данных.
		// Требование: промежуточный кэш вычислений прозрачен — возвращаемые значения
		// не отличаются от пересчитанных.
		// Traceability: openspec:analytics/performance#scenario-cache-transparent
		// Traceability: openspec:analytics/performance#requirement-analytics-computed-on-read
		Assert.That(second.Constructions.Count, Is.EqualTo(first.Constructions.Count));
		Assert.That(second.Positions.Count, Is.EqualTo(first.Positions.Count));
		for (var index = 0; index < first.Constructions.Count; index++)
		{
			Assert.That(second.Constructions[index], Is.EqualTo(first.Constructions[index]));
		}

		for (var index = 0; index < first.Positions.Count; index++)
		{
			Assert.That(second.Positions[index], Is.EqualTo(first.Positions[index]));
		}

		// Здравый смысл композиции: итог первого чтения — 98 реализованных + 20
		// корректировки, оба чтения это подтверждают.
		var metrics = second.Constructions.Single(candidate => candidate.ConstructionId == construction.Id);
		Assert.That(metrics.RealizedPnL, Is.EqualTo(98m));
		Assert.That(metrics.TotalPnL, Is.EqualTo(118m));
	}

	[TestMethod]
	[Description("Перенос сделки пересчитывает результат обеих конструкций")]
	public async Task TryIfTradeRebindingRecomputesBothConstructions()
	{
		// Arrange: две конструкции — первая с закрытой линейной позицией (98),
		// вторая с открытой покупкой 0.2 по 41000 (реализованный остаток −2 комиссии).
		var first = await _constructionService.CreateAsync("Первая", 1000m);
		var second = await _constructionService.CreateAsync("Вторая", 500m);
		await AddLinearTradeAsync("exec-buy-a", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await AddLinearTradeAsync("exec-sell-a", "Sell", "0.1", "43000", "1", ExecMs(2023, 12, 28, 11, 0));
		await AddLinearTradeAsync("exec-buy-b", "Buy", "0.2", "41000", "2", ExecMs(2023, 12, 28, 10, 30));
		await _bindingService.BindAsync(first.Id, "exec-buy-a");
		await _bindingService.BindAsync(first.Id, "exec-sell-a");
		await _bindingService.BindAsync(second.Id, "exec-buy-b");

		// Act — первое чтение.
		var before = await ReadMetricsAsync();

		// Assert: первая закрыта с результатом 98 и итогом 98; вторая открыта —
		// нереализованная часть не оценена, итог null.
		var firstBefore = before.Constructions.Single(metrics => metrics.ConstructionId == first.Id);
		var secondBefore = before.Constructions.Single(metrics => metrics.ConstructionId == second.Id);
		Assert.That(firstBefore.RealizedPnL, Is.EqualTo(98m));
		Assert.That(firstBefore.TotalPnL, Is.EqualTo(98m));
		Assert.That(secondBefore.RealizedPnL, Is.EqualTo(-2m));
		Assert.That(secondBefore.UnrealizedPnL, Is.Null);
		Assert.That(secondBefore.TotalPnL, Is.Null);

		// Act — перенос продажи из первой конструкции во вторую.
		await _bindingService.BindAsync(second.Id, "exec-sell-a");
		var after = await ReadMetricsAsync();

		// Assert: обе конструкции пересчитаны новым составом записей. Первая открыта
		// покупкой 0.1 — реализованный остаток −1, итог деградировал в null, следа
		// прежних 98 нет. Вторая: продажа 0.1 закрыла старейшую часть покупки по FIFO —
		// (43000 − 41000) × 0.1 − 3 комиссии = 197 при открытом остатке 0.1 по 41000.
		// Требование: перенос сделки отражается в результате обеих конструкций
		// очередным чтением, следов прежнего расчёта не остаётся.
		// Traceability: openspec:analytics/performance#scenario-rebinding-recomputes-both
		var firstAfter = after.Constructions.Single(metrics => metrics.ConstructionId == first.Id);
		var secondAfter = after.Constructions.Single(metrics => metrics.ConstructionId == second.Id);
		Assert.That(firstAfter.RealizedPnL, Is.EqualTo(-1m));
		Assert.That(firstAfter.UnrealizedPnL, Is.Null);
		Assert.That(firstAfter.TotalPnL, Is.Null);
		Assert.That(firstAfter.ClosedAt, Is.Null);
		Assert.That(secondAfter.RealizedPnL, Is.EqualTo(197m));
		Assert.That(secondAfter.UnrealizedPnL, Is.Null);
		Assert.That(secondAfter.TotalPnL, Is.Null);
		var firstPosition = after.Positions.Single(position => position.ConstructionId == first.Id);
		var secondPosition = after.Positions.Single(position => position.ConstructionId == second.Id);
		Assert.That(firstPosition.Residual, Is.EqualTo(0.1m));
		Assert.That(firstPosition.AverageOpenPrice, Is.EqualTo(42000m));
		Assert.That(secondPosition.Residual, Is.EqualTo(0.1m));
		Assert.That(secondPosition.AverageOpenPrice, Is.EqualTo(41000m));
		Assert.That(secondPosition.AccumulatedFees, Is.EqualTo(3m));
	}

	#region Композиция читающего слоя

	/// <summary>
	/// Читает метрики всех конструкций из текущих данных журнала: сырые записи
	/// синхронизации материализуются в сделки и закрывающие записи экспираций,
	/// привязки собирают записи в потоки позиций «конструкция × инструмент»,
	/// ручные пометки вливаются в те же потоки, и каждый запрос собирает
	/// конвейер заново — без хранимых результатов и промежуточного состояния.
	/// Композиция повторяет правила read-модели позиций: хронология по моменту,
	/// рангу вида и ключу источника, избыточные закрывающие записи в поток
	/// не входят, пометка закрывает остаток на её момент.
	/// </summary>
	private async Task<ReadingResult> ReadMetricsAsync()
	{
		using var db = new JournalDbContext(CreateOptions());

		var constructions = await db.Constructions
			.AsNoTracking()
			.OrderBy(construction => construction.Id)
			.ToListAsync();
		var rawExecutions = await db.RawExecutions
			.OrderBy(execution => execution.Id)
			.ToListAsync();
		var rawInstruments = await db.RawInstruments
			.OrderBy(instrument => instrument.Id)
			.ToListAsync();
		var rawDeliveries = await db.RawDeliveries
			.OrderBy(delivery => delivery.Id)
			.ToListAsync();
		var boundUserdata = await db.TradeUserdata
			.AsNoTracking()
			.Where(userdata => userdata.ConstructionId != null)
			.ToListAsync();
		var marks = await db.ManualCloseMarks
			.AsNoTracking()
			.OrderBy(mark => mark.Id)
			.ToListAsync();

		// Конвейер проекции строится заново при каждом чтении: сделки и закрывающие
		// записи экспираций выводятся из одного и того же сырья детерминированно.
		var resolver = new InstrumentResolver(new InstrumentCatalog(rawInstruments));
		var tradeMaterializer = new TradeMaterializer(resolver);
		var trades = tradeMaterializer.Materialize(rawExecutions);

		// Привязки передаются материализатору экспираций в строковой форме его контракта;
		// непривязанные сделки образуют остаток «Входящих» и в позициях не участвуют.
		var constructionIdByExecId = boundUserdata.ToDictionary(
			userdata => userdata.ExecId,
			userdata => userdata.ConstructionId!.Value);
		var assignments = constructionIdByExecId.ToDictionary(
			pair => pair.Key,
			pair => (string?)pair.Value.ToString(CultureInfo.InvariantCulture));
		var expiry = new ExpiryMaterializer(tradeMaterializer, resolver)
			.Materialize(rawExecutions, rawDeliveries, assignments, Now);

		var streams = new Dictionary<(long ConstructionId, string Symbol), List<StreamEvent>>();
		foreach (var trade in trades)
		{
			if (constructionIdByExecId.TryGetValue(trade.ExecId, out var constructionId) == false)
			{
				continue;
			}

			Append(streams, constructionId, trade.Symbol, new StreamEvent(
				trade.ExecutedAt, PositionFifoEntryKind.Trade, trade.ExecId, trade.Quantity, trade.Price, trade.Fee));
		}

		foreach (var entry in expiry.ClosingEntries)
		{
			if (entry.ConstructionId is null
				|| long.TryParse(entry.ConstructionId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryConstructionId) == false)
			{
				continue;
			}

			Append(streams, entryConstructionId, entry.Symbol, new StreamEvent(
				entry.ClosedAt, PositionFifoEntryKind.ExpiryClosing, entry.SourceKey, entry.Quantity, entry.EffectivePrice, entry.Fee));
		}

		foreach (var mark in marks)
		{
			// Пометки проверок задают цену пользователя явно; пометка без цены
			// разрешается последней маркой провайдера в слоях марок.
			Append(streams, mark.ConstructionId, mark.Symbol, new StreamEvent(
				mark.MarkedAt, PositionFifoEntryKind.ManualMark, $"manual:{mark.Id}", null, mark.Price ?? 0m, 0m));
		}

		var positionCalculator = new PositionMetricsCalculator();
		var positions = new List<PositionMetrics>();
		var orderedKeys = streams.Keys
			.OrderBy(key => key.ConstructionId)
			.ThenBy(key => key.Symbol, StringComparer.Ordinal);
		foreach (var key in orderedKeys)
		{
			var entries = Replay(key, streams[key]);
			if (entries.Count > 0)
			{
				positions.Add(positionCalculator.Calculate(key.ConstructionId, key.Symbol, entries));
			}
		}

		var constructionCalculator = new ConstructionMetricsCalculator();
		var constructionMetrics = new List<ConstructionMetrics>();
		foreach (var construction in constructions)
		{
			var adjustments = await db.PnLAdjustments
				.AsNoTracking()
				.Where(adjustment => adjustment.ConstructionId == construction.Id)
				.OrderBy(adjustment => adjustment.Id)
				.ToListAsync();
			constructionMetrics.Add(constructionCalculator.Calculate(
				construction.Id,
				construction.AllocatedCapitalUsdt,
				positions.Where(position => position.ConstructionId == construction.Id),
				adjustments.Select(adjustment => new ConstructionPnLAdjustment
				{
					Date = adjustment.Date,
					AmountUsdt = adjustment.AmountUsdt,
				}),
				Now));
		}

		return new ReadingResult(constructionMetrics, positions);
	}

	/// <summary>Применяет события потока в хронологии read-модели: избыточные закрывающие записи пропускаются, пометка закрывает остаток на её момент.</summary>
	private static List<PositionFifoEntry> Replay((long ConstructionId, string Symbol) key, List<StreamEvent> events)
	{
		var ordered = events
			.OrderBy(streamEvent => streamEvent.At)
			.ThenBy(streamEvent => (int)streamEvent.Kind)
			.ThenBy(streamEvent => streamEvent.SourceKey, StringComparer.Ordinal);

		var entries = new List<PositionFifoEntry>();
		var residual = 0m;
		foreach (var streamEvent in ordered)
		{
			switch (streamEvent.Kind)
			{
				case PositionFifoEntryKind.Trade:
					residual += streamEvent.Quantity!.Value;
					entries.Add(ToFifoEntry(streamEvent, streamEvent.Quantity!.Value));
					break;

				case PositionFifoEntryKind.ExpiryClosing:
					// Закрывающая запись к нулевому остатку избыточна: в поток позиции
					// она не входит, превращаясь в предупреждение read-модели.
					if (residual == 0m)
					{
						continue;
					}

					residual += streamEvent.Quantity!.Value;
					entries.Add(ToFifoEntry(streamEvent, streamEvent.Quantity!.Value));
					break;

				case PositionFifoEntryKind.ManualMark:
					// Пометка закрывает ровно тот остаток, что накопился к её моменту.
					if (residual == 0m)
					{
						continue;
					}

					entries.Add(ToFifoEntry(streamEvent, -residual));
					residual = 0m;
					break;

				default:
					throw new ArgumentOutOfRangeException(nameof(events), streamEvent.Kind, "Неизвестный вид записи потока позиции.");
			}
		}

		return entries;
	}

	private static PositionFifoEntry ToFifoEntry(StreamEvent streamEvent, decimal quantity) => new()
	{
		At = streamEvent.At,
		Kind = streamEvent.Kind,
		SourceKey = streamEvent.SourceKey,
		Quantity = quantity,
		Price = streamEvent.Price,
		Fee = streamEvent.Fee,
	};

	/// <summary>Добавляет событие в поток пары «конструкция × инструмент», создавая список при первом событии.</summary>
	private static void Append(
		Dictionary<(long ConstructionId, string Symbol), List<StreamEvent>> streams,
		long constructionId,
		string symbol,
		StreamEvent streamEvent)
	{
		var key = (constructionId, symbol);
		if (streams.TryGetValue(key, out var events) == false)
		{
			events = [];
			streams.Add(key, events);
		}

		events.Add(streamEvent);
	}

	/// <summary>Результат чтения метрик: метрики конструкций и метрики их позиций.</summary>
	private sealed record ReadingResult(
		IReadOnlyList<ConstructionMetrics> Constructions,
		IReadOnlyList<PositionMetrics> Positions);

	/// <summary>Событие единого потока позиции до применения: количество ручной пометки выводится применением потока.</summary>
	private sealed record StreamEvent(
		DateTimeOffset At,
		PositionFifoEntryKind Kind,
		string SourceKey,
		decimal? Quantity,
		decimal Price,
		decimal Fee);

	#endregion

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

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

	/// <summary>Добавляет сырую запись исполнения линейного перпа BTCUSDT с комиссией в USDT.</summary>
	private async Task AddLinearTradeAsync(string execId, string side, string execQty, string execPrice, string execFee, long execTimeMs)
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawExecutions.Add(Raw(
			execId, "linear", LinearSymbol, execTimeMs,
			ExecutionPayload(execId, LinearSymbol, side, execPrice, execQty, execFee, "USDT", execTimeMs)));
		await db.SaveChangesAsync();
	}

	/// <summary>Добавляет сырую запись исполнения опциона BTC-29DEC23-45000-C с комиссией в USDC.</summary>
	private async Task AddOptionTradeAsync(string execId, string side, string execQty, string execPrice, string execFee)
	{
		var execTimeMs = ExecMs(2023, 12, 28, 10, 0);
		using var db = new JournalDbContext(CreateOptions());
		db.RawExecutions.Add(Raw(
			execId, "option", CallSymbol, execTimeMs,
			ExecutionPayload(execId, CallSymbol, side, execPrice, execQty, execFee, "USDC", execTimeMs)));
		await db.SaveChangesAsync();
	}

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

	private static DateTimeOffset At(int year, int month, int day, int hour, int minute) =>
		new(year, month, day, hour, minute, 0, TimeSpan.Zero);

	private static long ExecMs(int year, int month, int day, int hour, int minute) =>
		new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	#endregion
}
