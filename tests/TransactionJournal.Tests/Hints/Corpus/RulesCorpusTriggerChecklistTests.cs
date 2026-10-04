namespace TransactionJournal.Tests.Hints.Corpus;

using NUnit.Framework;
using TransactionJournal.Hints.Corpus;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// Проверки разделения неизвестного и битого ключа триггера: неизвестный
/// движку ключ trigger.implementation и implementation: null — карточки
/// валидны, подсказок не порождают, попадают в чек-лист корпуса; загрузка
/// и проход продолжаются.
/// </summary>
[TestClass]
public class RulesCorpusTriggerChecklistTests
{
	[TestMethod]
	[Description("Неизвестный движку ключ триггера валиден и уходит в чек-лист, загрузка продолжается")]
	// Ключ вне набора машинных триггеров схеме соответствует: это не битость,
	// а правило, не покрытое кодом движка.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-unimplemented-trigger-checklist
	// Traceability: openspec:hints/rules-corpus#scenario-corpus-unknown-key-continues
	public void Load_WithUnknownTriggerKey_ClassifiesAsUnimplemented()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-unknown.yaml", CorpusYaml.Card("tc-unknown", implementation: "yet-unimplemented-key"));

			var snapshot = new RulesCorpusLoader(dir).Load();

			Assert.That(snapshot.UnimplementedCards.Select(card => card.Id), Is.EqualTo(new[] { "tc-unknown" }));
			Assert.That(snapshot.ExecutableCards, Is.Empty);
			Assert.That(snapshot.RetiredCards, Is.Empty);
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("implementation: null валиден — правило без машинной реализации живёт в чек-листе")]
	// Нулевая реализация — штатная форма правил-ориентиров, не порождающих
	// подсказок: они не исполняются движком и не ломают проход.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-unimplemented-trigger-checklist
	// Traceability: openspec:hints/rules-corpus#scenario-corpus-null-implementation-checklist
	public void Load_WithNullImplementation_ClassifiesAsUnimplemented()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-null.yaml", CorpusYaml.Card("tc-null"));

			var snapshot = new RulesCorpusLoader(dir).Load();

			Assert.That(snapshot.UnimplementedCards.Select(card => card.Id), Is.EqualTo(new[] { "tc-null" }));
			Assert.That(snapshot.ExecutableCards, Is.Empty);
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Отсутствие поля trigger.implementation — несоответствие схеме, отличие от явного null")]
	// Битость и нулевая реализация не смешиваются: поле схемы должно быть
	// объявлено, явный null допустим; пропуск поля делает карточку битой.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-validity-precondition
	public void Load_WithoutImplementationField_FailsSchema()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			var yaml = string.Join(Environment.NewLine, CorpusYaml.Card("tc-absent")
				.Split(Environment.NewLine)
				.Where(line => !line.TrimStart().StartsWith("implementation:", StringComparison.Ordinal)));
			CorpusYaml.Write(dir, "tc-absent.yaml", yaml);

			var exception = Assert.Throws<CorpusInvalidException>(() => new RulesCorpusLoader(dir).Load());

			Assert.That(exception.Problems, Has.Some.Contains("implementation"));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}
}
