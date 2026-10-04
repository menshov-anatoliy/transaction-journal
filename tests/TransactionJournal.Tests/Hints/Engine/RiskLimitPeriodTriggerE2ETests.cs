namespace TransactionJournal.Tests.Hints.Engine;

using NUnit.Framework;
using TransactionJournal.Hints.Ports;
using TransactionJournal.Tests.Hints.Corpus;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// Проверки журнального триггера лимитов риска парой «сработало / не
/// сработало»: убыток периода от совокупного капитала достиг недельного лимита
/// — подсказка с фактами периода; прибыльный период — подсказок нет. Субъект
/// правила — журнал целиком, не конструкции.
/// </summary>
[TestClass]
public class RiskLimitPeriodTriggerE2ETests
{
	private const string Template = "Убыток за {period}: {lossPct}% при лимите {limitPct}% — наращивание риска остановить.";

	/// <summary>Карточка ак-01 с порогами курса: неделя 1%, месяц 5%, квартал 10%.</summary>
	private static IReadOnlyList<(string Name, string Value, string Unit)> Limits =>
	[
		("weeklyRiskLimit", "1", "percent"),
		("monthlyRiskLimit", "5", "percent"),
		("quarterlyRiskLimit", "10", "percent"),
	];

	[TestMethod]
	[Description("Убыток недели 2% при лимите 1% даёт подсказку недели с фактами и ключом окна")]
	// FIFO-пара покупка 100/продажа 80 теряет 20 при капитале 1000 — 2%
	// превышают недельный лимит 1% первым из периодов.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfWeeklyLossCrossesLimit_FiresSmallestPeriodWithFacts()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-01.yaml", CorpusYaml.Card(
				"ac-01",
				implementation: "risk-limit-period",
				scope: "portfolio",
				hintTemplate: Template,
				thresholds: Limits));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(
						capital: 1000m,
						trades:
						[
							HintPassHarness.Trade("e1", "ETHUSDT", 1m, 100m, 0m),
							HintPassHarness.Trade("e2", "ETHUSDT", -1m, 80m, 0m),
						]),
				],
			};

			var (result, added) = await HintPassHarness.RunAsync(dir, snapshot);

			Assert.That(result.CreatedHints, Is.EqualTo(1));
			Assert.That(added, Has.Count.EqualTo(1));
			Assert.That(added[0].Subject.Kind, Is.EqualTo(HintSubjectKind.Journal));
			Assert.That(added[0].WindowPeriodKey, Is.EqualTo("2026-W41"));
			Assert.That(added[0].Facts, Is.EqualTo(new Dictionary<string, string>
			{
				["period"] = "неделя",
				["lossPct"] = "2",
				["limitPct"] = "1",
			}));
			Assert.That(added[0].Text, Is.EqualTo("Убыток за неделя: 2% при лимите 1% — наращивание риска остановить."));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Прибыльный период не даёт подсказки лимитов риска")]
	// Плюсовая пара сделок убытка не образует: условие не выполнено.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public async Task TryIfPeriodIsProfitable_DoesNotFire()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-01.yaml", CorpusYaml.Card(
				"ac-01",
				implementation: "risk-limit-period",
				scope: "portfolio",
				hintTemplate: Template,
				thresholds: Limits));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(
						capital: 1000m,
						trades:
						[
							HintPassHarness.Trade("e1", "ETHUSDT", 1m, 100m, 0m),
							HintPassHarness.Trade("e2", "ETHUSDT", -1m, 120m, 0m),
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
