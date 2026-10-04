namespace TransactionJournal.Tests.Hints.Engine;

using NUnit.Framework;
using TransactionJournal.Hints.Ports;
using TransactionJournal.Tests.Hints.Corpus;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// Проверки структурных триггеров конструкции парой «сработало / не
/// сработало»: непокрытая продажа волатильности (ак-03), превышение доли
/// проданных краёв (ак-18), опцион в деньгах (ак-43). Все три структурные:
/// порог либо не участвует в условии, либо читается из thresholds карточки.
/// </summary>
[TestClass]
public class ConstructionStructuralTriggerE2ETests
{
	[TestMethod]
	[Description("Проданный пут без купленной ноги того же актива даёт подсказку непокрытой продажи")]
	// Структура «голый пут»: противоноги в конструкции нет — срабатывание
	// структурное, текст шаблона статичен.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfSoldPutHasNoCounterLeg_FiresUncoveredSaleHint()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-03.yaml", CorpusYaml.Card(
				"ac-03",
				implementation: "uncovered-sale-margin",
				hintTemplate: "В конструкции есть непокрытая продажа — закладывай обеспечение до ×10 от ГО покупки.",
				thresholds: [("nakedSaleMarginMultiplier", "10", "ratio")]));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(positions:
					[
						HintPassHarness.Position("BTC-27NOV26-40000-P", -5m),
					]),
				],
			};

			var (result, added) = await HintPassHarness.RunAsync(dir, snapshot);

			Assert.That(result.CreatedHints, Is.EqualTo(1));
			Assert.That(added[0].Facts, Is.Empty);
			Assert.That(added[0].Text, Is.EqualTo("В конструкции есть непокрытая продажа — закладывай обеспечение до ×10 от ГО покупки."));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Проданный пут с купленным колом того же актива покрыт — подсказки нет")]
	// Противонога того же базового актива закрывает структуру: продажа
	// покрыта, правило не срабатывает.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfSoldPutCoveredByBoughtCall_DoesNotFire()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-03.yaml", CorpusYaml.Card(
				"ac-03",
				implementation: "uncovered-sale-margin",
				hintTemplate: "В конструкции есть непокрытая продажа — закладывай обеспечение до ×10 от ГО покупки.",
				thresholds: [("nakedSaleMarginMultiplier", "10", "ratio")]));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(positions:
					[
						HintPassHarness.Position("BTC-27NOV26-40000-P", -5m),
						HintPassHarness.Position("BTC-27NOV26-44000-C", 5m),
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
	[Description("Проданные края 6 из 16 опционов превышают долю 1/3 — подсказка лимита краёв")]
	// Доля продаж 0.375 больше порога карточки 1/3: закрытый риск нарушен.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfSoldEdgeShareExceedsCap_FiresEdgeSaleHint()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-18.yaml", CorpusYaml.Card(
				"ac-18",
				implementation: "edge-sale-cap",
				hintTemplate: "Объём продаваемых краёв превышает 1/3 опционов конструкции — риск выхода из закрытого риска.",
				thresholds: [("edgeVolumeCap", "1/3", "share-of-options")]));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(positions:
					[
						HintPassHarness.Position("BTC-16OCT26-50000-C", 10m),
						HintPassHarness.Position("BTC-16OCT26-60000-P", -6m),
					]),
				],
			};

			var (result, added) = await HintPassHarness.RunAsync(dir, snapshot);

			Assert.That(result.CreatedHints, Is.EqualTo(1));
			Assert.That(added[0].Facts, Is.Empty);
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Проданные края ровно 1/3 опционов порог не превышают — подсказки нет")]
	// Граница порога не включена: ровно треть остаётся в рамках правила.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfSoldEdgeShareEqualsCap_DoesNotFire()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-18.yaml", CorpusYaml.Card(
				"ac-18",
				implementation: "edge-sale-cap",
				hintTemplate: "Объём продаваемых краёв превышает 1/3 опционов конструкции — риск выхода из закрытого риска.",
				thresholds: [("edgeVolumeCap", "1/3", "share-of-options")]));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(positions:
					[
						HintPassHarness.Position("BTC-16OCT26-50000-C", 10m),
						HintPassHarness.Position("BTC-16OCT26-60000-P", -5m),
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
	[Description("Купленный кол в деньгах при споте выше страйка даёт подсказку синтетического закрытия")]
	// Спот 51000 выше страйка 50000: кол ITM, приём синтетического закрытия
	// применим.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfBoughtCallIsInTheMoney_FiresSyntheticCloseHint()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-43.yaml", CorpusYaml.Card(
				"ac-43",
				implementation: "synthetic-close-itm",
				hintTemplate: "Опцион в деньгах — синтетическое закрытие: FUT + встречный опцион того же страйка и экспирации."));
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
				new Dictionary<string, decimal> { ["BTCUSDT"] = HintPassHarness.BtcSpot + 1000m });

			Assert.That(result.CreatedHints, Is.EqualTo(1));
			Assert.That(added[0].Facts, Is.Empty);
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Кол вне денег при споте ниже страйка не даёт подсказки синтетического закрытия")]
	// Спот 49000 ниже страйка 50000: кол OTM, приём неприменим.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfBoughtCallIsOutOfTheMoney_DoesNotFire()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-43.yaml", CorpusYaml.Card(
				"ac-43",
				implementation: "synthetic-close-itm",
				hintTemplate: "Опцион в деньгах — синтетическое закрытие: FUT + встречный опцион того же страйка и экспирации."));
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
				new Dictionary<string, decimal> { ["BTCUSDT"] = HintPassHarness.BtcSpot - 1000m });

			Assert.That(result.CreatedHints, Is.EqualTo(0));
			Assert.That(added, Is.Empty);
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}
}
