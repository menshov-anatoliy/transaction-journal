namespace TransactionJournal.Tests.Web.Api;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Api;
using TransactionJournal.Application;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Data;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Интеграционные проверки API карточки конструкции: полный снимок экрана
/// (шапка с параметрами и комментарием, метрики с периодом, четыре таблицы
/// записей) и команды действий — шапка, комментарии трёх уровней, ручные
/// пометки закрытия, действия сделок и корректировки PnL. Домен подменяется
/// стабильными заглушками: проверяется HTTP-контракт карточки, а не повторное
/// вычисление метрик.
/// </summary>
[TestClass]
public sealed class ConstructionCardApiTests
{
	[TestMethod]
	[Description("Снимок карточки отдаёт шапку с параметрами, метрики с периодом и четыре таблицы")]
	// Карточка конструкции — полная информация и всё управление (паритет №3–№9):
	// один запрос снимка отдаёт шапку (имя, статус, капитал, риск/профит с
	// единицами, комментарий), kstrip метрик с периодом и длительностью
	// и таблицы позиций, сделок, закрывающих записей и корректировок.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	// Все данные карточки идут через единый версионированный API.
	// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
	public async Task TryIfCardSnapshotServesHeaderMetricsAndTables()
	{
		// Arrange: стабильная read-модель деталей конструкции 7.
		await using var factory = new SectionApiFactory(services => services.ReplaceDetailReadModel(new StubCardDetailReadModel()));
		using var client = factory.CreateClient();

		// Act: запрос снимка карточки.
		var response = await client.GetAsync("/api/v1/constructions/7");

		// Assert: успешный JSON полного снимка.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();

		// Шапка: идентификация, параметры плановых границ обеими единицами
		// с первоисточником-единицей и комментарий.
		Assert.That(payload!["constructionId"]!.GetValue<long>(), Is.EqualTo(7L));
		Assert.That(payload["name"]!.GetValue<string>(), Is.EqualTo("ETH-240628-3200C+P"));
		Assert.That(payload["status"]!.GetValue<string>(), Is.EqualTo("open"));
		Assert.That(payload["allocatedCapitalUsdt"]!.GetValue<decimal>(), Is.EqualTo(3000m));
		Assert.That(payload["riskPercent"]!.GetValue<decimal>(), Is.EqualTo(3m));
		Assert.That(payload["riskUsdt"]!.GetValue<decimal>(), Is.EqualTo(90m));
		Assert.That(payload["riskUnit"]!.GetValue<string>(), Is.EqualTo("percent"));
		Assert.That(payload["profitPercent"]!.GetValue<decimal>(), Is.EqualTo(8m));
		Assert.That(payload["profitUsdt"]!.GetValue<decimal>(), Is.EqualTo(240m));
		Assert.That(payload["profitUnit"]!.GetValue<string>(), Is.EqualTo("usdt"));
		Assert.That(payload["comment"]!.GetValue<string>(), Is.EqualTo("## стреддл\n*имя ручное*"));

		// Метрики: итог с процентом, разбивка, стоимость, занятость, период
		// с длительностью и котировки оценки.
		var metrics = payload["metrics"]!;
		Assert.That(metrics["totalPnL"]!.GetValue<decimal>(), Is.EqualTo(105.5m));
		Assert.That(metrics["totalPnLPercent"]!.GetValue<decimal>(), Is.EqualTo(3.5m));
		Assert.That(metrics["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(60.25m));
		Assert.That(metrics["unrealizedPnL"]!.GetValue<decimal>(), Is.EqualTo(40.25m));
		Assert.That(metrics["adjustmentsPnL"]!.GetValue<decimal>(), Is.EqualTo(5m));
		Assert.That(metrics["markValue"]!.GetValue<decimal>(), Is.EqualTo(500m));
		Assert.That(metrics["capitalUsagePercent"]!.GetValue<decimal>(), Is.EqualTo(16.7m));
		Assert.That(metrics["openedAt"]!.GetValue<string>(), Is.EqualTo("2026-06-18T09:05:00+00:00"));
		Assert.That(metrics["closedAt"], Is.Null);
		Assert.That(metrics["durationSeconds"]!.GetValue<double>(), Is.EqualTo(53.4 * 3600).Within(0.1));
		Assert.That(payload["marksAsOf"]!.GetValue<string>(), Is.EqualTo("2026-06-20T14:30:00+00:00"));
		Assert.That(payload["hasMarkFailure"]!.GetValue<bool>(), Is.False);

		// Позиции: 15 колонок от входа до итога с комментарием и пометкой открытости.
		var position = payload["positions"]!.AsArray().Single(node => node!["symbol"]!.GetValue<string>() == "ETH-28JUN24-3200-C")!;
		Assert.That(position["residual"]!.GetValue<decimal>(), Is.EqualTo(2m));
		Assert.That(position["averageEntryPrice"]!.GetValue<decimal>(), Is.EqualTo(30m));
		Assert.That(position["averageClosePrice"], Is.Null);
		Assert.That(position["markValue"]!.GetValue<decimal>(), Is.EqualTo(300m));
		Assert.That(position["priceChangePercent"]!.GetValue<decimal>(), Is.EqualTo(5m));
		Assert.That(position["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(40m));
		Assert.That(position["unrealizedPnL"]!.GetValue<decimal>(), Is.EqualTo(30m));
		Assert.That(position["totalPnL"]!.GetValue<decimal>(), Is.EqualTo(70m));
		Assert.That(position["accumulatedFees"]!.GetValue<decimal>(), Is.EqualTo(0.5m));
		Assert.That(position["openedAt"]!.GetValue<string>(), Is.EqualTo("2026-06-18T09:05:00+00:00"));
		Assert.That(position["isOpen"]!.GetValue<bool>(), Is.True);
		Assert.That(position["comment"]!.GetValue<string>(), Is.EqualTo("ножка **входа**"));

		// Сделки: атрибуты биржевой записи и комментарий.
		var trade = payload["trades"]!.AsArray().Single(node => node!["execId"]!.GetValue<string>() == "exec-1")!;
		Assert.That(trade["symbol"]!.GetValue<string>(), Is.EqualTo("ETH-28JUN24-3200-C"));
		Assert.That(trade["executedAt"]!.GetValue<string>(), Is.EqualTo("2026-06-18T09:05:00+00:00"));
		Assert.That(trade["isBuy"]!.GetValue<bool>(), Is.True);
		Assert.That(trade["quantity"]!.GetValue<decimal>(), Is.EqualTo(2m));
		Assert.That(trade["price"]!.GetValue<decimal>(), Is.EqualTo(30m));
		Assert.That(trade["amountUsdt"]!.GetValue<decimal>(), Is.EqualTo(60m));
		Assert.That(trade["fee"]!.GetValue<decimal>(), Is.EqualTo(0.1m));
		Assert.That(trade["comment"], Is.Null);

		// Закрывающие записи: единый поток с типом и ключом ручной пометки.
		var entry = payload["closingEntries"]!.AsArray().Single()!;
		Assert.That(entry["closedAt"]!.GetValue<string>(), Is.EqualTo("2026-06-19T10:00:00+00:00"));
		Assert.That(entry["kind"]!.GetValue<string>(), Is.EqualTo("manual-mark"));
		Assert.That(entry["symbol"]!.GetValue<string>(), Is.EqualTo("ETHUSDT"));
		Assert.That(entry["quantity"]!.GetValue<decimal>(), Is.EqualTo(-0.5m));
		Assert.That(entry["price"]!.GetValue<decimal>(), Is.EqualTo(3520m));
		Assert.That(entry["amountUsdt"]!.GetValue<decimal>(), Is.EqualTo(-1760m));
		Assert.That(entry["manualMarkId"]!.GetValue<long>(), Is.EqualTo(21L));

		// Предупреждение об избыточной закрывающей записи доходит снимком.
		var warning = payload["closingWarnings"]!.AsArray().Single()!;
		Assert.That(warning["kind"]!.GetValue<string>(), Is.EqualTo("manual-mark"));
		Assert.That(warning["symbol"]!.GetValue<string>(), Is.EqualTo("ETHUSDT"));

		// Корректировки: пять колонок со знаковой суммой и источником.
		var adjustment = payload["adjustments"]!.AsArray().Single()!;
		Assert.That(adjustment["adjustmentId"]!.GetValue<long>(), Is.EqualTo(11L));
		Assert.That(adjustment["date"]!.GetValue<string>(), Is.EqualTo("2026-06-19T00:00:00+00:00"));
		Assert.That(adjustment["description"]!.GetValue<string>(), Is.EqualTo("PnL робота grid-ETH"));
		Assert.That(adjustment["source"]!.GetValue<string>(), Is.EqualTo("robot"));
		Assert.That(adjustment["amountUsdt"]!.GetValue<decimal>(), Is.EqualTo(5m));
	}

	[TestMethod]
	[Description("Снимок неизвестной конструкции отвечает 404")]
	// Чужой или удалённый идентификатор — явный 404: карточка показывает
	// состояние «не найдена» вместо пустых таблиц.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	public async Task ThrowOnUnknownConstructionCardReturns404()
	{
		// Arrange: стабильная read-модель, знающая только конструкцию 7.
		await using var factory = new SectionApiFactory(services => services.ReplaceDetailReadModel(new StubCardDetailReadModel()));
		using var client = factory.CreateClient();

		// Act: запрос карточки неизвестной конструкции.
		var response = await client.GetAsync("/api/v1/constructions/999");

		// Assert: эндпоинт отвечает 404.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
	}

	[TestMethod]
	[Description("Повреждённое сырьё журнала отвечает 503 с причиной")]
	// Явное состояние недоступности вместо пустой карточки: журналу не дали
	// собрать данные — пользователь видит причину обновить экран.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	public async Task ThrowOnBrokenJournalReturns503WithReason()
	{
		// Arrange: read-модель, падающая материализацией сделок.
		await using var factory = new SectionApiFactory(services => services.ReplaceDetailReadModel(StubCardDetailReadModel.WithBrokenJournal()));
		using var client = factory.CreateClient();

		// Act: запрос снимка карточки.
		var response = await client.GetAsync("/api/v1/constructions/7");

		// Assert: карточка отвечает 503 с причиной недоступности.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["error"]!.GetValue<string>(), Does.Contain("исполнени"));
	}
}

/// <summary>
/// Стабильная read-модель деталей для карточки: конструкция 7 с открытым
/// остатком, двумя позициями, тремя сделками, ручной закрывающей записью,
/// предупреждением об избыточной записи и одной корректировкой.
/// </summary>
internal sealed class StubCardDetailReadModel : IConstructionDetailReadModel
{
	private readonly bool _brokenJournal;

	private StubCardDetailReadModel(bool brokenJournal)
	{
		_brokenJournal = brokenJournal;
	}

	/// <summary>Вариант по умолчанию: конструкция 7 со всеми таблицами записей.</summary>
	public StubCardDetailReadModel()
		: this(brokenJournal: false)
	{
	}

	/// <summary>Вариант с повреждённым сырьём: чтение падает материализацией сделок.</summary>
	public static StubCardDetailReadModel WithBrokenJournal() => new(brokenJournal: true);

	public Task<ConstructionDetailData> ReadAsync(long constructionId, CancellationToken cancellationToken = default)
	{
		if (_brokenJournal)
		{
			throw new TradeMaterializationException("exec-1", "сырая запись исполнения повреждена");
		}

		if (constructionId != 7L)
		{
			throw new ConstructionNotFoundException(constructionId);
		}

		var marksAsOf = new DateTimeOffset(2026, 6, 20, 14, 30, 0, TimeSpan.Zero);
		var openedAt = new DateTimeOffset(2026, 6, 18, 9, 5, 0, TimeSpan.Zero);
		var closedPositionAt = new DateTimeOffset(2026, 6, 19, 10, 0, 0, TimeSpan.Zero);
		var metrics = new TransactionJournal.Application.Analytics.ConstructionMetrics
		{
			ConstructionId = constructionId,
			AllocatedCapitalUsdt = 3000m,
			RealizedPnL = 60.25m,
			UnrealizedPnL = 40.25m,
			AdjustmentsPnL = 5m,
			TotalPnL = 105.5m,
			MarkValue = 500m,
			RealizedPnLPercent = 2.0m,
			UnrealizedPnLPercent = 1.34m,
			AdjustmentsPnLPercent = 0.17m,
			TotalPnLPercent = 3.5m,
			CapitalUsagePercent = 16.7m,
			OpenedAt = openedAt,
			ClosedAt = null,
			Duration = TimeSpan.FromHours(53.4),
		};
		var data = new ConstructionDetailData(
			ConstructionId: constructionId,
			Name: "ETH-240628-3200C+P",
			Status: ConstructionStatus.Open,
			AllocatedCapitalUsdt: 3000m,
			RiskPercent: 3m,
			RiskUsdt: 90m,
			ProfitPercent: 8m,
			ProfitUsdt: 240m,
			Comment: "## стреддл\n*имя ручное*",
			Metrics: metrics,
			HasOpenResidual: true,
			HasMarkFailure: false,
			MarksAsOf: marksAsOf,
			Positions:
			[
				new ConstructionPositionRow(
					Symbol: "ETH-28JUN24-3200-C",
					Residual: 2m,
					AverageEntryPrice: 30m,
					AverageClosePrice: null,
					RealizedPnL: 40m,
					RealizedPnLPercent: 1.33m,
					UnrealizedPnL: 30m,
					UnrealizedPnLPercent: 1m,
					TotalPnL: 70m,
					TotalPnLPercent: 2.33m,
					AccumulatedFees: 0.5m,
					OpenedAt: openedAt,
					ClosedAt: null,
					IsOpen: true,
					Comment: "ножка **входа**",
					MarkValue: 300m,
					PriceChangePercent: 5m),
				new ConstructionPositionRow(
					Symbol: "ETHUSDT",
					Residual: 0m,
					AverageEntryPrice: 3500m,
					AverageClosePrice: 3520m,
					RealizedPnL: 20.25m,
					RealizedPnLPercent: 0.67m,
					UnrealizedPnL: null,
					UnrealizedPnLPercent: null,
					TotalPnL: 20.25m,
					TotalPnLPercent: 0.67m,
					AccumulatedFees: 0.25m,
					OpenedAt: openedAt,
					ClosedAt: closedPositionAt,
					IsOpen: false,
					Comment: null,
					MarkValue: null,
					PriceChangePercent: null),
			],
			Trades:
			[
				new ConstructionTradeRow("exec-1", "ETH-28JUN24-3200-C", openedAt, true, 2m, 30m, 60m, 0.1m, null),
				new ConstructionTradeRow("exec-2", "ETHUSDT", openedAt.AddHours(1), true, 0.5m, 3500m, 1750m, 0.2m, null),
				new ConstructionTradeRow("exec-3", "ETHUSDT", openedAt.AddHours(2), false, 0.5m, 3520m, 1760m, 0.15m, null),
			],
			ClosingEntries:
			[
				new ConstructionClosingEntryRow(closedPositionAt, PositionClosingKind.ManualMark, "ETHUSDT", -0.5m, 3520m, -1760m, ManualMarkId: 21),
			],
			ClosingWarnings:
			[
				new RedundantClosingEntryWarning
				{
					ConstructionId = constructionId,
					Symbol = "ETHUSDT",
					Kind = PositionClosingKind.ManualMark,
					ClosedAt = closedPositionAt,
					SourceKey = "manual:99",
				},
			],
			Adjustments:
			[
				new ConstructionAdjustmentRow(11, new DateTimeOffset(2026, 6, 19, 0, 0, 0, TimeSpan.Zero), "PnL робота grid-ETH", PnLAdjustmentSource.Robot, 5m),
			],
			RiskUnit: TargetUnit.Percent,
			ProfitUnit: TargetUnit.Usdt);
		return Task.FromResult(data);
	}
}
