namespace TransactionJournal.Tests.Hints.Engine;

using NUnit.Framework;
using TransactionJournal.Hints.Ports;
using TransactionJournal.Tests.Hints.Corpus;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// Проверки триггеров «цена × страйк × дни до экспирации» парой «сработало /
/// не сработало»: окно ролла (ак-26), порог неблагоприятного смещения (ак-27),
/// окно тета-распада у центра (ак-31). Все три требуют марку спота базового
/// актива и опционные ноги конструкции.
/// </summary>
[TestClass]
public class RollDecayTriggerE2ETests
{
	[TestMethod]
	[Description("8 дней до экспирации и спот в страйковом диапазоне дают подсказку окна ролла")]
	// Экспирация 16.10 при as-of 08.10 — 8 дней внутри окна 7–10 карточки,
	// спот 50000 внутри страйков [50000, 50000].
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfDaysLeftInWindowAndSpotAtStrikes_FiresRollWindowHint()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-26.yaml", CorpusYaml.Card(
				"ac-26",
				implementation: "roll-time-window",
				hintTemplate: "До экспирации {daysLeft} дн., цена у центра — окно ролла (7–10 дн.).",
				thresholds: [("rollWindowDays", "7-10", "days")]));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(positions:
					[
						HintPassHarness.Position("BTC-16OCT26-50000-C", 10m),
						HintPassHarness.Position("BTC-16OCT26-50000-P", 10m),
					]),
				],
			};

			var (result, added) = await HintPassHarness.RunAsync(
				dir,
				snapshot,
				new Dictionary<string, decimal> { ["BTCUSDT"] = HintPassHarness.BtcSpot });

			Assert.That(result.CreatedHints, Is.EqualTo(1));
			Assert.That(added[0].Facts, Is.EqualTo(new Dictionary<string, string> { ["daysLeft"] = "8" }));
			Assert.That(added[0].Text, Is.EqualTo("До экспирации 8 дн., цена у центра — окно ролла (7–10 дн.)."));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Спот вне страйкового диапазона в окне дней не даёт подсказки ролла")]
	// Цена ушла от центра — окно ролла не открыто даже внутри дней.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfSpotOutsideStrikeSpan_DoesNotFireRollWindow()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-26.yaml", CorpusYaml.Card(
				"ac-26",
				implementation: "roll-time-window",
				hintTemplate: "До экспирации {daysLeft} дн., цена у центра — окно ролла (7–10 дн.).",
				thresholds: [("rollWindowDays", "7-10", "days")]));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(positions:
					[
						HintPassHarness.Position("BTC-16OCT26-50000-C", 10m),
						HintPassHarness.Position("BTC-16OCT26-50000-P", 10m),
					]),
				],
			};

			var (result, added) = await HintPassHarness.RunAsync(
				dir,
				snapshot,
				new Dictionary<string, decimal> { ["BTCUSDT"] = HintPassHarness.BtcSpot + 5000m });

			Assert.That(result.CreatedHints, Is.EqualTo(0));
			Assert.That(added, Is.Empty);
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Спот ниже страйка кола на 4% превышает порог 3% — подсказка ролла со фактом смещения")]
	// Неблагоприятное смещение купленного кола: (50000−48000)/50000 = 4%.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfAdverseShiftExceedsThreshold_FiresRollThresholdHint()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-27.yaml", CorpusYaml.Card(
				"ac-27",
				implementation: "roll-threshold",
				hintTemplate: "Цена ушла от страйка на {shiftPct}% (порог 3%) — кандидат на ролл.",
				thresholds: [("rollThreshold", "3", "percent")]));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(positions:
					[
						HintPassHarness.Position("BTC-31OCT26-50000-C", 10m),
					]),
				],
			};

			var (result, added) = await HintPassHarness.RunAsync(
				dir,
				snapshot,
				new Dictionary<string, decimal> { ["BTCUSDT"] = HintPassHarness.BtcSpot - 2000m });

			Assert.That(result.CreatedHints, Is.EqualTo(1));
			Assert.That(added[0].Facts, Is.EqualTo(new Dictionary<string, string> { ["shiftPct"] = "4" }));
			Assert.That(added[0].Text, Is.EqualTo("Цена ушла от страйка на 4% (порог 3%) — кандидат на ролл."));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Смещение кола 1% ниже порога 3% не даёт подсказки ролла")]
	// Спот 49500 сместил страйк лишь на 1%: условие не выполнено.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfAdverseShiftBelowThreshold_DoesNotFire()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-27.yaml", CorpusYaml.Card(
				"ac-27",
				implementation: "roll-threshold",
				hintTemplate: "Цена ушла от страйка на {shiftPct}% (порог 3%) — кандидат на ролл.",
				thresholds: [("rollThreshold", "3", "percent")]));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(positions:
					[
						HintPassHarness.Position("BTC-31OCT26-50000-C", 10m),
					]),
				],
			};

			var (result, added) = await HintPassHarness.RunAsync(
				dir,
				snapshot,
				new Dictionary<string, decimal> { ["BTCUSDT"] = HintPassHarness.BtcSpot - 500m });

			Assert.That(result.CreatedHints, Is.EqualTo(0));
			Assert.That(added, Is.Empty);
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("12 дней до экспирации и спот у страйка дают подсказку тета-распада")]
	// Экспирация 20.10 — 12 дней не больше порога 14 и не меньше одного:
	// последний отрезок жизни стреддла у центра.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfWithinDecayWindowAndSpotAtStrikes_FiresDecayHint()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-31.yaml", CorpusYaml.Card(
				"ac-31",
				implementation: "atm-decay-window",
				hintTemplate: "До экспирации {daysLeft} дн., цена в зоне страйка — риск тета-распада, кандидат на досрочный выход.",
				thresholds: [("decayWindowDays", "14", "days")]));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(positions:
					[
						HintPassHarness.Position("BTC-20OCT26-50000-C", 10m),
						HintPassHarness.Position("BTC-20OCT26-50000-P", 10m),
					]),
				],
			};

			var (result, added) = await HintPassHarness.RunAsync(
				dir,
				snapshot,
				new Dictionary<string, decimal> { ["BTCUSDT"] = HintPassHarness.BtcSpot });

			Assert.That(result.CreatedHints, Is.EqualTo(1));
			Assert.That(added[0].Facts, Is.EqualTo(new Dictionary<string, string> { ["daysLeft"] = "12" }));
			Assert.That(added[0].Text, Is.EqualTo("До экспирации 12 дн., цена в зоне страйка — риск тета-распада, кандидат на досрочный выход."));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("22 дня до экспирации вне окна распада не дают подсказки")]
	// Экспирация 30.10 далека: тета-распад у центра ещё не актуален.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfBeyondDecayWindow_DoesNotFire()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-31.yaml", CorpusYaml.Card(
				"ac-31",
				implementation: "atm-decay-window",
				hintTemplate: "До экспирации {daysLeft} дн., цена в зоне страйка — риск тета-распада, кандидат на досрочный выход.",
				thresholds: [("decayWindowDays", "14", "days")]));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(positions:
					[
						HintPassHarness.Position("BTC-30OCT26-50000-C", 10m),
						HintPassHarness.Position("BTC-30OCT26-50000-P", 10m),
					]),
				],
			};

			var (result, added) = await HintPassHarness.RunAsync(
				dir,
				snapshot,
				new Dictionary<string, decimal> { ["BTCUSDT"] = HintPassHarness.BtcSpot });

			Assert.That(result.CreatedHints, Is.EqualTo(0));
			Assert.That(added, Is.Empty);
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}
}
