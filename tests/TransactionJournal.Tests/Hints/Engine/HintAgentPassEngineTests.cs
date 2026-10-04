namespace TransactionJournal.Tests.Hints.Engine;

using NUnit.Framework;
using TransactionJournal.Domain.Data;
using TransactionJournal.Hints;
using TransactionJournal.Hints.Ports;
using TransactionJournal.Tests.Hints.Corpus;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// Проверки каркаса чистой функции триггера на фикстуре одного правила: проход
/// превращает сработавший триггер в запись подсказки с фактами и субъектом,
/// несработавший не создаёт записей; пороги триггер берёт только из карточки.
/// </summary>
[TestClass]
public class HintAgentPassEngineTests
{
	[TestMethod]
	[Description("Сработавший триггер фикстуры даёт одну запись с фактами, субъектом и денормализованными полями")]
	// Цель профита фикстуры — 10% капитала 1000 = 100; итог 120 достиг цели,
	// запись фиксирует процент, чёткость, источники и as-of на момент прохода.
	// Traceability: openspec:hints/engine-pass#requirement-engine-deterministic-selection
	// Traceability: openspec:hints/engine-pass#requirement-engine-self-describing-record
	public async Task TryIfTriggerFires_PassStoresSingleHintWithFactsAndSubject()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-target.yaml", CorpusYaml.Card(
				"tc-target",
				implementation: "profit-target-reached",
				clarity: "fuzzy",
				hintTemplate: "Результат конструкции {pnlPct}% — плановая прибыль достигнута.",
				character: "profit-target"));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(
						id: 7,
						capital: 1000m,
						profitValue: 10m,
						profitUnit: TargetUnit.Percent,
						totalPnL: 120m),
				],
			};

			var (result, added) = await HintPassHarness.RunAsync(dir, snapshot);

			Assert.That(result.Outcome, Is.EqualTo(HintPassOutcome.Completed));
			Assert.That(result.CreatedHints, Is.EqualTo(1));
			Assert.That(added, Has.Count.EqualTo(1));
			var record = added[0];
			Assert.That(record.RuleId, Is.EqualTo("tc-target"));
			Assert.That(record.Subject.Kind, Is.EqualTo(HintSubjectKind.Construction));
			Assert.That(record.Subject.ConstructionId, Is.EqualTo(7L));
			Assert.That(record.Character, Is.EqualTo("profit-target"));
			Assert.That(record.Clarity, Is.EqualTo("fuzzy"));
			Assert.That(record.Status, Is.EqualTo(HintStatus.New));
			Assert.That(record.AsOf, Is.EqualTo(HintPassHarness.FixedNow));
			Assert.That(record.WindowPeriodKey, Is.Null);
			Assert.That(record.Facts, Is.EqualTo(new Dictionary<string, string> { ["pnlPct"] = "12" }));
			Assert.That(record.Text, Is.EqualTo("[решение] Результат конструкции 12% — плановая прибыль достигнута."));
			Assert.That(record.Sources, Has.Count.EqualTo(1));
			Assert.That(record.Sources[0].Tag, Is.EqualTo("ТЕСТ"));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Несработавший триггер фикстуры не создаёт записей — проход завершён без подсказок")]
	// Итог 90 не достиг цели 100: условие не выполнено, хранилище не пишется.
	// Traceability: openspec:hints/engine-pass#requirement-engine-deterministic-selection
	public async Task TryIfTriggerDoesNotFire_PassStoresNothing()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-target.yaml", CorpusYaml.Card(
				"tc-target",
				implementation: "profit-target-reached",
				clarity: "fuzzy",
				hintTemplate: "Результат конструкции {pnlPct}% — плановая прибыль достигнута."));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(capital: 1000m, profitValue: 10m, profitUnit: TargetUnit.Percent, totalPnL: 90m),
				],
			};

			var (result, added) = await HintPassHarness.RunAsync(dir, snapshot);

			Assert.That(result.Outcome, Is.EqualTo(HintPassOutcome.Completed));
			Assert.That(result.CreatedHints, Is.EqualTo(0));
			Assert.That(added, Is.Empty);
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}
}
