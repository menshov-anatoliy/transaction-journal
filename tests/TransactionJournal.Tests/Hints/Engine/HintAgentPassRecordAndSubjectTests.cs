namespace TransactionJournal.Tests.Hints.Engine;

using Moq;
using NUnit.Framework;
using TransactionJournal.Hints;
using TransactionJournal.Hints.Corpus;
using TransactionJournal.Domain.Data;
using TransactionJournal.Hints.Ports;
using TransactionJournal.Tests.Hints.Corpus;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// Проверки самоописательности записи и определения субъекта: правка карточки
/// между проходами не искажает уже созданную запись; журнальное правило даёт
/// субъект «журнал», правило конструкции — субъект со ссылкой на конструкцию;
/// недоступность марок пропускает проход без записей.
/// </summary>
[TestClass]
public class HintAgentPassRecordAndSubjectTests
{
	[TestMethod]
	[Description("Правка шаблона карточки между проходами не меняет запись первого прохода")]
	// Запись денормализована на момент генерации: второй проход рендерит новый
	// текст в новую запись, история первой остаётся как была.
	// Traceability: openspec:hints/engine-pass#requirement-engine-self-describing-record
	public async Task TryIfCardEditedBetweenPasses_FirstRecordStaysUnchanged()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-target.yaml", CorpusYaml.Card(
				"tc-target",
				implementation: "profit-target-reached",
				hintTemplate: "Старая формулировка {pnlPct}%."));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(capital: 1000m, profitValue: 10m, profitUnit: TargetUnit.Percent, totalPnL: 120m),
				],
			};

			var (firstResult, firstAdded) = await HintPassHarness.RunAsync(dir, snapshot);
			CorpusYaml.Write(dir, "tc-target.yaml", CorpusYaml.Card(
				"tc-target",
				implementation: "profit-target-reached",
				hintTemplate: "Новая формулировка {pnlPct}%."));
			var (secondResult, secondAdded) = await HintPassHarness.RunAsync(dir, snapshot);

			Assert.That(firstResult.CreatedHints, Is.EqualTo(1));
			Assert.That(secondResult.CreatedHints, Is.EqualTo(1));
			Assert.That(firstAdded[0].Text, Is.EqualTo("Старая формулировка 12%."));
			Assert.That(secondAdded[0].Text, Is.EqualTo("Новая формулировка 12%."));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Недоступность источника марок пропускает проход без записей и с диагностикой")]
	// Подсказки на неполных данных не выпускаются: хранилище не трогается,
	// причина уходит в диагностику прохода.
	// Traceability: openspec:hints/engine-pass#requirement-engine-market-unavailable-skips-pass
	public async Task TryIfMarketUnavailable_PassSkippedWithoutRecords()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-14.yaml", CorpusYaml.Card(
				"ac-14",
				implementation: "profit-target-reached",
				hintTemplate: "Результат {pnlPct}% — кандидат на разборку."));
			var journalReader = new Mock<IJournalSnapshotReader>();
			journalReader
				.Setup(reader => reader.ReadAsync(It.IsAny<CancellationToken>()))
				.ReturnsAsync(new JournalSnapshot
				{
					Constructions =
					[
						HintPassHarness.Construction(capital: 1000m, profitValue: 10m, profitUnit: TargetUnit.Percent, totalPnL: 120m),
					],
				});
			var markSource = new Mock<IMarkSource>();
			markSource
				.Setup(source => source.GetMarksAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync(MarkBatch.Unavailable("Источник рыночных марок недоступен."));
			var added = new List<HintRecord>();
			var hintStore = new Mock<IHintStore>();
			hintStore
				.Setup(store => store.AddAsync(It.IsAny<HintRecord>(), It.IsAny<CancellationToken>()))
				.Callback<HintRecord, CancellationToken>((record, _) => added.Add(record))
				.ReturnsAsync((HintRecord record, CancellationToken _) => record);
			var clock = new Mock<IClock>();
			clock.SetupGet(clock => clock.UtcNow).Returns(HintPassHarness.FixedNow);
			var pass = new HintAgentPass(
				new RulesCorpusLoader(dir),
				journalReader.Object,
				markSource.Object,
				hintStore.Object,
				clock.Object);

			var result = await pass.RunAsync();

			Assert.That(result.Outcome, Is.EqualTo(HintPassOutcome.SkippedMarketUnavailable));
			Assert.That(result.Diagnostics, Is.Not.Null);
			Assert.That(result.Diagnostics!, Has.Some.Contains("недоступ"));
			Assert.That(added, Is.Empty);
			hintStore.Verify(store => store.AddAsync(It.IsAny<HintRecord>(), It.IsAny<CancellationToken>()), Times.Never);
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Закрытая конструкция не становится субъектом подсказки конструкции")]
	// Субъект v1 — только открытые конструкции: закрытая не оценивается
	// конструкционным триггером вовсе.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-subject-v1-closed-set
	public async Task TryIfConstructionIsClosed_PassSkipsConstructionSubject()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-target.yaml", CorpusYaml.Card(
				"tc-target",
				implementation: "profit-target-reached",
				hintTemplate: "Результат {pnlPct}% — плановая прибыль достигнута."));
			var snapshot = new JournalSnapshot
			{
				Constructions =
				[
					HintPassHarness.Construction(id: 5, isOpen: false, capital: 1000m, profitValue: 10m, profitUnit: TargetUnit.Percent, totalPnL: 120m),
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
