namespace TransactionJournal.Tests.Hints.Corpus;

using NUnit.Framework;
using TransactionJournal.Hints.Corpus;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// Проверки валидации объявленных конфликтных пар: объявление conflicts_with
/// трактуется симметрично — достаточно записи с одной из сторон; две активные
/// карточки пары делают корпус невалидным агрегированной ошибкой с именами обеих
/// карточек до построения снимка; пара с retired-карточкой валидна и остаётся
/// документацией известного напряжения источников.
/// </summary>
[TestClass]
public class RulesCorpusConflictValidationTests
{
	[TestMethod]
	[Description("Две активные карточки объявленной пары останавливают загрузку ошибкой с именами обеих карточек")]
	// Объявление записано только с одной стороны — симметричная трактовка
	// ловит пару; проход не начинается: снимка и чтений нет.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-declared-conflicts-validated
	// Traceability: openspec:hints/rules-corpus#scenario-corpus-conflict-pair-active-fails-pass
	public void Load_WithBothActivePair_FailsNamingBothCards()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-a.yaml", CorpusYaml.Card("tc-a", conflictsWith: ["tc-b"]));
			CorpusYaml.Write(dir, "tc-b.yaml", CorpusYaml.Card("tc-b"));

			var exception = Assert.Throws<CorpusInvalidException>(() => new RulesCorpusLoader(dir).Load());

			Assert.That(exception.Problems, Has.Some.Matches<string>(problem =>
				problem.Contains("tc-a") && problem.Contains("tc-b")));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Симметричная трактовка: пара ловится и когда объявление записано с другой стороны")]
	// Запись с одной из сторон достаточна независимо от того, какая карточка
	// держит объявление.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-declared-conflicts-validated
	public void Load_WithReverseSidedDeclaration_FailsTheSame()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-a.yaml", CorpusYaml.Card("tc-a"));
			CorpusYaml.Write(dir, "tc-b.yaml", CorpusYaml.Card("tc-b", conflictsWith: ["tc-a"]));

			var exception = Assert.Throws<CorpusInvalidException>(() => new RulesCorpusLoader(dir).Load());

			Assert.That(exception.Problems, Has.Some.Matches<string>(problem =>
				problem.Contains("tc-a") && problem.Contains("tc-b")));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Пара с retired-членом валидна: retired не исполняется, но остаётся в снимке для гашения")]
	// Известное напряжение источников документируется объявлением, а не ломает
	// корпус: активная карточка пары исполняется, retired гасит живые записи.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-declared-conflicts-validated
	// Traceability: openspec:hints/rules-corpus#scenario-corpus-conflict-pair-retired-valid
	public void Load_WithRetiredMemberInPair_IsValid()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-a.yaml", CorpusYaml.Card("tc-a", implementation: "risk-limit-period", conflictsWith: ["tc-r"]));
			CorpusYaml.Write(dir, "tc-r.yaml", CorpusYaml.Card("tc-r", status: "retired", retiredReason: "superseded"));

			var snapshot = new RulesCorpusLoader(dir).Load();

			Assert.That(snapshot.ExecutableCards.Select(card => card.Id), Is.EqualTo(new[] { "tc-a" }));
			Assert.That(snapshot.RetiredCards.Select(card => card.Id), Is.EqualTo(new[] { "tc-r" }));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Несколько нарушенных пар собираются в одну агрегированную ошибку без дубликатов")]
	// Валидация собирает все конфликтные пары за одно чтение; взаимные
	// объявления одной пары не удваивают проблему.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-declared-conflicts-validated
	public void Load_WithMultipleActivePairs_CollectsAllOnce()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-a.yaml", CorpusYaml.Card("tc-a", conflictsWith: ["tc-b", "tc-c"]));
			CorpusYaml.Write(dir, "tc-b.yaml", CorpusYaml.Card("tc-b", conflictsWith: ["tc-a"]));
			CorpusYaml.Write(dir, "tc-c.yaml", CorpusYaml.Card("tc-c"));

			var exception = Assert.Throws<CorpusInvalidException>(() => new RulesCorpusLoader(dir).Load());

			var pairProblems = exception.Problems
				.Where(problem => problem.Contains("конфликтн", StringComparison.OrdinalIgnoreCase))
				.ToArray();
			Assert.That(pairProblems, Has.Length.EqualTo(2));
			Assert.That(pairProblems, Has.Some.Matches<string>(problem => problem.Contains("tc-a") && problem.Contains("tc-b")));
			Assert.That(pairProblems, Has.Some.Matches<string>(problem => problem.Contains("tc-a") && problem.Contains("tc-c")));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}
}
