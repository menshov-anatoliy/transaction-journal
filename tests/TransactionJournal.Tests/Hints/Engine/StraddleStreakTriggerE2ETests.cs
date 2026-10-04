namespace TransactionJournal.Tests.Hints.Engine;

using NUnit.Framework;
using TransactionJournal.Hints.Ports;
using TransactionJournal.Tests.Hints.Corpus;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// Проверки триггеров объёмов и результата парой «сработало / не сработало»:
/// минимальный размер стреддла (ак-35), серия плюсовых сделок во флэте (ак-38),
/// разморозка залипшей части (ак-40). Серии и прибыль считаются поверх FIFO
/// движка Domain.
/// </summary>
[TestClass]
public class StraddleStreakTriggerE2ETests
{
	[TestMethod]
	[Description("Стреддл 20 опционов и 5 фьючерсов ниже минимума 30:15 даёт подсказку с фактическими объёмами")]
	// Стреддл купленных кола и пута есть, объёмы ниже минимума карточки.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfStraddleBelowMinimum_FiresWithActualQuantities()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-35.yaml", CorpusYaml.Card(
				"ac-35",
				implementation: "min-straddle-size",
				hintTemplate: "Объём конструкции {opts} опц : {fut} фьюч — ниже минимума 30:15 для интрадея.",
				thresholds:
				[
					("minStraddleOptions", "30", "contracts"),
					("minStraddleFutures", "15", "contracts"),
				]));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(positions:
					[
						HintPassHarness.Position("BTC-16OCT26-50000-C", 10m),
						HintPassHarness.Position("BTC-16OCT26-50000-P", 10m),
						HintPassHarness.Position("BTCUSDT", 5m),
					]),
				],
			};

			var (result, added) = await HintPassHarness.RunAsync(dir, snapshot);

			Assert.That(result.CreatedHints, Is.EqualTo(1));
			Assert.That(added[0].Facts, Is.EqualTo(new Dictionary<string, string>
			{
				["opts"] = "20",
				["fut"] = "5",
			}));
			Assert.That(added[0].Text, Is.EqualTo("Объём конструкции 20 опц : 5 фьюч — ниже минимума 30:15 для интрадея."));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Стреддл 40 опционов и 15 фьючерсов достигает минимума — подсказки нет")]
	// Оба объёма не ниже порогов 30:15: правило интрадея не срабатывает.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfStraddleMeetsMinimum_DoesNotFire()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-35.yaml", CorpusYaml.Card(
				"ac-35",
				implementation: "min-straddle-size",
				hintTemplate: "Объём конструкции {opts} опц : {fut} фьюч — ниже минимума 30:15 для интрадея.",
				thresholds:
				[
					("minStraddleOptions", "30", "contracts"),
					("minStraddleFutures", "15", "contracts"),
				]));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(positions:
					[
						HintPassHarness.Position("BTC-16OCT26-50000-C", 20m),
						HintPassHarness.Position("BTC-16OCT26-50000-P", 20m),
						HintPassHarness.Position("BTCUSDT", 15m),
					]),
				],
			};

			var (result, added) = await HintPassHarness.RunAsync(dir, snapshot);

			Assert.That(result.CreatedHints, Is.EqualTo(0));
			Assert.That(added, Is.Empty);
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Четыре подряд плюсовых фьючерсных сделки дают подсказку серии со фактом wins")]
	// Четыре раунд-трипа ETHUSDT с растущим выходом: открывающие ноги с нулевым
	// вкладом серию не рвут, серия закрывающих сделок 4 равна порогу.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfTrailingWinStreakReachesThreshold_FiresWithWinsFact()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-38.yaml", CorpusYaml.Card(
				"ac-38",
				implementation: "flat-win-streak",
				clarity: "fuzzy",
				hintTemplate: "{wins} плюсовых сделок во флэте — можно добавить рабочую часть.",
				thresholds: [("partsUnlockWins", "4", "trades")]));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(trades:
					[
						HintPassHarness.Trade("e1", "ETHUSDT", 1m, 100m, 0m),
						HintPassHarness.Trade("e2", "ETHUSDT", -1m, 110m, 0m),
						HintPassHarness.Trade("e3", "ETHUSDT", 1m, 110m, 0m),
						HintPassHarness.Trade("e4", "ETHUSDT", -1m, 120m, 0m),
						HintPassHarness.Trade("e5", "ETHUSDT", 1m, 120m, 0m),
						HintPassHarness.Trade("e6", "ETHUSDT", -1m, 130m, 0m),
						HintPassHarness.Trade("e7", "ETHUSDT", 1m, 130m, 0m),
						HintPassHarness.Trade("e8", "ETHUSDT", -1m, 140m, 0m),
					]),
				],
			};

			var (result, added) = await HintPassHarness.RunAsync(dir, snapshot);

			Assert.That(result.CreatedHints, Is.EqualTo(1));
			Assert.That(added[0].Facts, Is.EqualTo(new Dictionary<string, string> { ["wins"] = "4" }));
			Assert.That(added[0].Text, Is.EqualTo("[решение] 4 плюсовых сделок во флэте — можно добавить рабочую часть."));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Три плюсовые сделки при пороге 4 не дают подсказки серии")]
	// Серия 3 меньше порога карточки: условие не выполнено.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfWinStreakBelowThreshold_DoesNotFire()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-38.yaml", CorpusYaml.Card(
				"ac-38",
				implementation: "flat-win-streak",
				clarity: "fuzzy",
				hintTemplate: "{wins} плюсовых сделок во флэте — можно добавить рабочую часть.",
				thresholds: [("partsUnlockWins", "4", "trades")]));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(trades:
					[
						HintPassHarness.Trade("e1", "ETHUSDT", 1m, 100m, 0m),
						HintPassHarness.Trade("e2", "ETHUSDT", -1m, 110m, 0m),
						HintPassHarness.Trade("e3", "ETHUSDT", 1m, 110m, 0m),
						HintPassHarness.Trade("e4", "ETHUSDT", -1m, 120m, 0m),
						HintPassHarness.Trade("e5", "ETHUSDT", 1m, 120m, 0m),
						HintPassHarness.Trade("e6", "ETHUSDT", -1m, 130m, 0m),
					]),
				],
			};

			var (result, added) = await HintPassHarness.RunAsync(dir, snapshot);

			Assert.That(result.CreatedHints, Is.EqualTo(0));
			Assert.That(added, Is.Empty);
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Прибыль 40 при худшем убытке 10 покрывает кратность 2× — подсказка разморозки")]
	// Итог в плюсе, накопленная прибыль не меньше двойного худшего
	// нереализованного убытка открытой позиции.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfProfitCoversLossRatio_FiresUnfreezeHint()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-40.yaml", CorpusYaml.Card(
				"ac-40",
				implementation: "unfreeze-profit-ratio",
				clarity: "fuzzy",
				hintTemplate: "Накопленная прибыль {profit} ≥ 2× убытка разморозки {loss} — залипшую часть можно разморозить.",
				thresholds: [("unfreezeProfitRatio", "2", "ratio")]));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(
						totalPnL: 40m,
						positions:
						[
							HintPassHarness.Position("BTC-16OCT26-50000-C", 10m, unrealizedPnL: -10m),
							HintPassHarness.Position("BTC-16OCT26-50000-P", 10m, unrealizedPnL: 50m),
						]),
				],
			};

			var (result, added) = await HintPassHarness.RunAsync(dir, snapshot);

			Assert.That(result.CreatedHints, Is.EqualTo(1));
			Assert.That(added[0].Facts, Is.EqualTo(new Dictionary<string, string>
			{
				["profit"] = "40",
				["loss"] = "10",
			}));
			Assert.That(added[0].Text, Is.EqualTo("[решение] Накопленная прибыль 40 ≥ 2× убытка разморозки 10 — залипшую часть можно разморозить."));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Прибыль 15 при убытке 10 не покрывает кратность 2× — подсказки нет")]
	// 15 < 2×10: разморозка съела бы больше прибыли, чем допускает правило.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfProfitBelowLossRatio_DoesNotFire()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-40.yaml", CorpusYaml.Card(
				"ac-40",
				implementation: "unfreeze-profit-ratio",
				clarity: "fuzzy",
				hintTemplate: "Накопленная прибыль {profit} ≥ 2× убытка разморозки {loss} — залипшую часть можно разморозить.",
				thresholds: [("unfreezeProfitRatio", "2", "ratio")]));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(
						totalPnL: 15m,
						positions:
						[
							HintPassHarness.Position("BTC-16OCT26-50000-C", 10m, unrealizedPnL: -10m),
							HintPassHarness.Position("BTC-16OCT26-50000-P", 10m, unrealizedPnL: 25m),
						]),
				],
			};

			var (result, added) = await HintPassHarness.RunAsync(dir, snapshot);

			Assert.That(result.CreatedHints, Is.EqualTo(0));
			Assert.That(added, Is.Empty);
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}
}
