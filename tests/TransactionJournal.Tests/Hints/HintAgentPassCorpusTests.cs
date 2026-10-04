namespace TransactionJournal.Tests.Hints;

using Moq;
using NUnit.Framework;
using TransactionJournal.Hints;
using TransactionJournal.Hints.Corpus;
using TransactionJournal.Hints.Ports;
using TransactionJournal.Tests.Hints.Corpus;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// Проверки прохода агента на предусловии корпуса: невалидный корпус возвращает
/// исход CorpusInvalid с полной диагностикой до любых чтений журнала и рынка;
/// активные правила без машинной реализации (неизвестный ключ или null) не
/// ломают проход — он продолжается, а идентификаторы уходят в чек-лист.
/// </summary>
[TestClass]
public class HintAgentPassCorpusTests
{
	private static readonly DateTimeOffset FixedNow = new(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);

	/// <summary>Собирает проход над стабами портов и загрузчиком корпуса из каталога.</summary>
	private static HintAgentPass BuildPass(string corpusDir, Mock<IJournalSnapshotReader> journalReader, Mock<IMarkSource> markSource)
	{
		var hintStore = new Mock<IHintStore>();
		var clock = new Mock<IClock>();
		clock.SetupGet(clock => clock.UtcNow).Returns(FixedNow);
		return new HintAgentPass(
			new RulesCorpusLoader(corpusDir),
			journalReader.Object,
			markSource.Object,
			hintStore.Object,
			clock.Object);
	}

	/// <summary>Настраивает стаб читателя журнал-снапшота с пустым журналом.</summary>
	private static void SetupEmptyJournal(Mock<IJournalSnapshotReader> journalReader)
		=> journalReader
			.Setup(reader => reader.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(new JournalSnapshot { Constructions = [] });

	/// <summary>Настраивает стаб доступного источника марок.</summary>
	private static void SetupAvailableMarks(Mock<IMarkSource> markSource)
		=> markSource
			.Setup(source => source.GetMarksAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new MarkBatch { Marks = new Dictionary<string, decimal>(), FailureReason = null });

	[TestMethod]
	[Description("Невалидный корпус возвращает CorpusInvalid с диагностикой, журнал и рынок не читаются")]
	// Битая карточка ломает проход до построения снимка и любых чтений: ни
	// подсказки, ни записи не производятся; хост продолжает работать.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-validity-precondition
	// Traceability: openspec:hints/rules-corpus#scenario-corpus-broken-card-fails-pass
	public async Task RunAsync_WithBrokenCorpus_FailsFastWithoutJournalOrMarketReads()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "broken.yaml", "title: карточка без обязательных полей");
			var journalReader = new Mock<IJournalSnapshotReader>();
			var markSource = new Mock<IMarkSource>();
			var pass = BuildPass(dir, journalReader, markSource);

			var result = await pass.RunAsync();

			Assert.That(result.Outcome, Is.EqualTo(HintPassOutcome.CorpusInvalid));
			Assert.That(result.AsOf, Is.EqualTo(FixedNow));
			Assert.That(result.Diagnostics, Is.Not.Null);
			Assert.That(result.Diagnostics!, Has.Some.Contains("broken.yaml"));
			journalReader.Verify(reader => reader.ReadAsync(It.IsAny<CancellationToken>()), Times.Never);
			markSource.Verify(source => source.GetMarksAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Отсутствующий каталог корпуса возвращает CorpusInvalid, проход не читает журнал и рынок")]
	// Отсутствующий корпус равен битому: та же агрегированная ошибка, приложение
	// продолжает работать.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-validity-precondition
	// Traceability: openspec:hints/rules-corpus#scenario-corpus-missing-empty-fails-pass
	public async Task RunAsync_WithMissingCorpusDirectory_SkipsAllReads()
	{
		var missingDir = Path.Combine(Path.GetTempPath(), "hints-corpus-tests", "missing-" + Guid.NewGuid().ToString("N"));
		var journalReader = new Mock<IJournalSnapshotReader>();
		var markSource = new Mock<IMarkSource>();
		var pass = BuildPass(missingDir, journalReader, markSource);

		var result = await pass.RunAsync();

		Assert.That(result.Outcome, Is.EqualTo(HintPassOutcome.CorpusInvalid));
		Assert.That(result.Diagnostics, Is.Not.Null);
		journalReader.Verify(reader => reader.ReadAsync(It.IsAny<CancellationToken>()), Times.Never);
		markSource.Verify(source => source.GetMarksAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[TestMethod]
	[Description("Проход с неизвестным ключом триггера продолжается, идентификатор уходит в чек-лист")]
	// Неизвестный ключ — не битость: проход доходит до чтений журнала и рынка,
	// карточка попадает в лог «непокрытых кодом» через итог прохода.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-unimplemented-trigger-checklist
	// Traceability: openspec:hints/rules-corpus#scenario-corpus-unknown-key-continues
	public async Task RunAsync_WithUnknownTriggerKey_ContinuesPassAndReportsChecklist()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-unknown.yaml", CorpusYaml.Card("tc-unknown", implementation: "yet-unimplemented-key"));
			var journalReader = new Mock<IJournalSnapshotReader>();
			SetupEmptyJournal(journalReader);
			var markSource = new Mock<IMarkSource>();
			SetupAvailableMarks(markSource);
			var pass = BuildPass(dir, journalReader, markSource);

			var result = await pass.RunAsync();

			Assert.That(result.Outcome, Is.EqualTo(HintPassOutcome.Completed));
			Assert.That(result.UnimplementedRuleIds, Is.EqualTo(new[] { "tc-unknown" }));
			journalReader.Verify(reader => reader.ReadAsync(It.IsAny<CancellationToken>()), Times.Once);
			markSource.Verify(source => source.GetMarksAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()), Times.Once);
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Проход с implementation: null продолжается — карточка в чек-листе, подсказок нет")]
	// Нулевая реализация не ломает проход: правило живёт в чек-листе сводки.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-unimplemented-trigger-checklist
	// Traceability: openspec:hints/rules-corpus#scenario-corpus-null-implementation-checklist
	public async Task RunAsync_WithNullImplementation_ContinuesPassAndReportsChecklist()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-null.yaml", CorpusYaml.Card("tc-null"));
			var journalReader = new Mock<IJournalSnapshotReader>();
			SetupEmptyJournal(journalReader);
			var markSource = new Mock<IMarkSource>();
			SetupAvailableMarks(markSource);
			var pass = BuildPass(dir, journalReader, markSource);

			var result = await pass.RunAsync();

			Assert.That(result.Outcome, Is.EqualTo(HintPassOutcome.Completed));
			Assert.That(result.UnimplementedRuleIds, Is.EqualTo(new[] { "tc-null" }));
			Assert.That(result.CreatedHints, Is.EqualTo(0));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}
}
