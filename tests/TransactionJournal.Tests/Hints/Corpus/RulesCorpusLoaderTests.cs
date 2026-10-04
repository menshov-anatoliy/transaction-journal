namespace TransactionJournal.Tests.Hints.Corpus;

using NUnit.Framework;
using TransactionJournal.Hints.Corpus;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// Проверки предусловия валидности корпуса: битая карточка (непарсируемый YAML,
/// несоответствие схеме), отсутствующий и пустой каталог ломают загрузку одной
/// агрегированной ошибкой со списком всех проблем; валидный корпус читается в
/// неизменяемый снимок по настраиваемому пути, замены действуют со следующего
/// чтения; retired-карточки попадают в снимок для гашения живых записей.
/// </summary>
[TestClass]
public class RulesCorpusLoaderTests
{
	[TestMethod]
	[Description("Битая карточка и несоответствие схеме останавливают загрузку агрегированной ошибкой по всем карточкам")]
	// Одна ошибка перечисляет проблемы всех битых карточек, а не только первой;
	// валидная карточка снимка в этой загрузке не порождает.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-validity-precondition
	// Traceability: openspec:hints/rules-corpus#scenario-corpus-broken-card-fails-pass
	public void Load_WithBrokenCards_FailsWithAggregatedError()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "broken-yaml.yaml", "id: [не, закрывается");
			CorpusYaml.Write(dir, "broken-schema.yaml", "title: карточка без обязательных полей");
			CorpusYaml.Write(dir, "good.yaml", CorpusYaml.Card("good"));

			var loader = new RulesCorpusLoader(dir);

			var exception = Assert.Throws<CorpusInvalidException>(() => loader.Load());
			Assert.That(exception.Problems, Has.Some.Contains("broken-yaml.yaml"));
			Assert.That(exception.Problems, Has.Some.Contains("broken-schema.yaml"));
			Assert.That(exception.Message, Does.Contain("broken-yaml.yaml"));
			Assert.That(exception.Message, Does.Contain("broken-schema.yaml"));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Отсутствующий каталог корпуса равен битому корпусу — та же агрегированная ошибка")]
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-validity-precondition
	// Traceability: openspec:hints/rules-corpus#scenario-corpus-missing-empty-fails-pass
	public void Load_WithMissingDirectory_ThrowsCorpusInvalid()
	{
		var loader = new RulesCorpusLoader(Path.Combine(Path.GetTempPath(), "hints-corpus-tests", "missing-" + Guid.NewGuid().ToString("N")));

		Assert.Throws<CorpusInvalidException>(() => loader.Load());
	}

	[TestMethod]
	[Description("Пустой каталог корпуса (без карточек) останавливает загрузку агрегированной ошибкой")]
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-validity-precondition
	// Traceability: openspec:hints/rules-corpus#scenario-corpus-missing-empty-fails-pass
	public void Load_WithEmptyDirectory_ThrowsCorpusInvalid()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			var loader = new RulesCorpusLoader(dir);

			var exception = Assert.Throws<CorpusInvalidException>(() => loader.Load());
			Assert.That(exception.Problems, Has.Count.EqualTo(1));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Несоответствие схеме: недопустимое значение закрытого набора и расхождение id с именем файла собираются в одну ошибку")]
	// Схема проверяет закрытые наборы character/clarity/scope/status и правило
	// «id равен имени файла»; все проблемы собираются за одно чтение.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-validity-precondition
	public void Load_WithSchemaViolations_CollectsAllProblems()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			var badClarity = CorpusYaml.Card("bad-clarity").Replace("clarity: crisp", "clarity: очевидно");
			CorpusYaml.Write(dir, "bad-clarity.yaml", badClarity);
			var mismatchedId = CorpusYaml.Card("not-file-name");
			CorpusYaml.Write(dir, "other-name.yaml", mismatchedId);

			var loader = new RulesCorpusLoader(dir);

			var exception = Assert.Throws<CorpusInvalidException>(() => loader.Load());
			Assert.That(exception.Problems, Has.Some.Contains("clarity"));
			Assert.That(exception.Problems, Has.Some.Contains("'not-file-name'"));
			Assert.That(exception.Problems, Has.Some.Contains("other-name.yaml"));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Валидный корпус читается в снимок с разбором карточек по назначению и retired-блоком")]
	// Снимок содержит машинные карточки для триггеров, чек-лист без реализации
	// и retired-карточки для гашения живых записей с причиной и заместителем.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-snapshot-per-pass
	public void Load_WithValidCorpus_BuildsClassifiedSnapshot()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-exec.yaml", CorpusYaml.Card("tc-exec", implementation: "risk-limit-period"));
			CorpusYaml.Write(dir, "tc-check.yaml", CorpusYaml.Card("tc-check"));
			CorpusYaml.Write(dir, "tc-retired.yaml", CorpusYaml.Card("tc-retired", status: "retired", retiredReason: "superseded"));

			var snapshot = new RulesCorpusLoader(dir).Load();

			Assert.That(snapshot.ExecutableCards.Select(card => card.Id), Is.EqualTo(new[] { "tc-exec" }));
			Assert.That(snapshot.UnimplementedCards.Select(card => card.Id), Is.EqualTo(new[] { "tc-check" }));
			Assert.That(snapshot.RetiredCards, Has.Count.EqualTo(1));
			var retiredCard = snapshot.RetiredCards.Single();
			Assert.That(retiredCard.RetiredReason, Is.EqualTo("superseded"));
			Assert.That(retiredCard.Status, Is.EqualTo(RuleCardStatus.Retired));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Retired-карточка без retired.reason ломает схему агрегированной ошибкой")]
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-validity-precondition
	public void Load_WithRetiredCardWithoutReason_FailsValidation()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-retired.yaml", CorpusYaml.Card("tc-retired", status: "retired"));

			var exception = Assert.Throws<CorpusInvalidException>(() => new RulesCorpusLoader(dir).Load());

			Assert.That(exception.Problems, Has.Some.Contains("retired"));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Замена файлов корпуса действует со следующего чтения без пересборки")]
	// Каждое чтение строит снимок заново: правки (git pull или копирование)
	// видны следующему проходу агента.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-snapshot-per-pass
	// Traceability: openspec:hints/rules-corpus#scenario-corpus-reload-next-pass
	public void Load_AfterFilesReplaced_RebuildsSnapshot()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-01.yaml", CorpusYaml.Card("tc-01", implementation: "risk-limit-period"));

			var first = new RulesCorpusLoader(dir).Load();
			Assert.That(first.ExecutableCards.Select(card => card.Id), Is.EqualTo(new[] { "tc-01" }));

			CorpusYaml.Write(dir, "tc-01.yaml", CorpusYaml.Card("tc-01", implementation: "unknown-key"));

			var second = new RulesCorpusLoader(dir).Load();
			Assert.That(second.ExecutableCards, Is.Empty);
			Assert.That(second.UnimplementedCards.Select(card => card.Id), Is.EqualTo(new[] { "tc-01" }));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Путь каталога корпуса переопределяется настройкой загрузчика")]
	// Загрузчик читает карточки по заданному пути, а не по фиксированному месту.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-snapshot-per-pass
	// Traceability: openspec:hints/rules-corpus#scenario-corpus-path-configurable
	public void Load_ReadsConfiguredPath()
	{
		var defaultDir = CorpusYaml.TempDir();
		var configuredDir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(defaultDir, "tc-default.yaml", CorpusYaml.Card("tc-default"));
			CorpusYaml.Write(configuredDir, "tc-configured.yaml", CorpusYaml.Card("tc-configured"));

			var snapshot = new RulesCorpusLoader(configuredDir).Load();

			Assert.That(snapshot.UnimplementedCards.Select(card => card.Id), Is.EqualTo(new[] { "tc-configured" }));
		}
		finally
		{
			CorpusYaml.DeleteDir(defaultDir);
			CorpusYaml.DeleteDir(configuredDir);
		}
	}
}
