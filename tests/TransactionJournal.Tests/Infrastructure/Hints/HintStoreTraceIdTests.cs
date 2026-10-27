namespace TransactionJournal.Tests.Infrastructure.Hints;

using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TransactionJournal.Domain.Data;
using TransactionJournal.Hints;
using TransactionJournal.Hints.Ports;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Infrastructure.Hints;
using TransactionJournal.Tests.Hints.Corpus;
using TransactionJournal.Tests.Hints.Engine;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// Корреляция записей подсказок с трассой прохода на реальном SQLite-хранилище:
/// все записи прохода несут один trace id корневого hint_pass, гашение в
/// expired идентификатор не получает, записи вне трассы и до внедрения
/// остаются null.
// Traceability: openspec:hints/engine-pass#requirement-engine-trace-id-on-created-hints
/// </summary>
[TestClass]
public class HintStoreTraceIdTests
{
	/// <summary>Имя источника спанов прохода — контракт sink-подписки.</summary>
	private const string HintsSourceName = "TransactionJournal.Hints";

	/// <summary>Имя тестового источника — создаёт окружение вне трассы прохода.</summary>
	private const string AmbientSourceName = "TransactionJournal.Tests.HintsAmbient";

	/// <summary>Тестовый источник активности для ambient-сценариев вне прохода.</summary>
	private static readonly ActivitySource AmbientSource = new(AmbientSourceName);

	private string _databasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"hint-trace-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Пул соединений SQLite держит файл базы открытым — сбрасываем его перед удалением.
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
		foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
		{
			var file = _databasePath + suffix;
			if (File.Exists(file))
			{
				File.Delete(file);
			}
		}
	}

	[TestMethod]
	[Description("Все созданные записи одного прохода несут один trace id корневого спана hint_pass")]
	// Проход по двум конструкциям создаёт две записи: сохранение идёт через
	// реальное хранилище внутри трассы прохода, ambient-идентификатор у обеих
	// записей один — trace id корневого спана, и в базе он тот же.
	// Traceability: openspec:hints/engine-pass#scenario-engine-pass-single-trace-id
	public async Task TryIfPassCreatesSeveralHints_AllCarrySameTraceId()
	{
		var dir = CorpusYaml.TempDir();
		using var listener = new HintTracingListener(HintsSourceName);
		try
		{
			// Arrange: одно правило цели прибыли, срабатывающее на каждой конструкции.
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
					HintPassHarness.Construction(
						id: 8,
						capital: 1000m,
						profitValue: 10m,
						profitUnit: TargetUnit.Percent,
						totalPnL: 130m),
				],
			};
			var store = new HintStore(CreateOptions());

			// Act: проход над реальным хранилищем внутри трассы прохода.
			var (result, added) = await HintPassHarness.RunAsync(dir, snapshot, store);

			// Assert: обе записи несут trace id корневого спана прохода.
			Assert.That(result.Outcome, Is.EqualTo(HintPassOutcome.Completed));
			Assert.That(added, Has.Count.EqualTo(2));
			Assert.That(added.Select(record => record.TraceId).Distinct().ToList(), Has.Count.EqualTo(1));
			Assert.That(added[0].TraceId, Is.EqualTo(result.TraceId));

			// Идентификатор записан в базу: чтение возвращает тот же trace id.
			var stored = await store.ListAllAsync();
			Assert.That(stored.Select(record => record.TraceId).Distinct().Single(), Is.EqualTo(result.TraceId));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Гашение записи в expired не меняет и не заполняет её trace id")]
	// Гашение не создание: переход в expired выполняется внутри чужой трассы,
	// но trace id созданной в трассе записи остаётся прежним, а у записи вне
	// трассы остаётся null.
	// Traceability: openspec:hints/engine-pass#scenario-engine-expiry-no-trace-id
	public async Task TryIfTransitionToExpired_TraceIdStaysUntouched()
	{
		var store = new HintStore(CreateOptions());
		string createdTraceId;
		HintRecord created;
		using (new HintTracingListener(AmbientSourceName))
		{
			// Создание записи внутри трассы: trace id снимается с окружения.
			using var ambient = AmbientSource.StartActivity("pass-ambient");
			Assert.That(ambient, Is.Not.Null);
			created = await store.AddAsync(NewRecord());
			createdTraceId = ambient!.TraceId.ToHexString();
		}

		// Запись вне трассы не получает идентификатор вовсе.
		var outside = await store.AddAsync(NewRecord());
		Assert.That(outside.TraceId, Is.Null);
		Assert.That(created.TraceId, Is.EqualTo(createdTraceId));

		using (new HintTracingListener(AmbientSourceName))
		{
			// Гашение в новой трассе: идентификатор записи не перезаписывается.
			using var other = AmbientSource.StartActivity("expire-ambient");
			Assert.That(other, Is.Not.Null);
			Assert.That(await store.TryTransitionAsync(created.Id, HintStatus.Expired), Is.True);
			Assert.That(await store.TryTransitionAsync(outside.Id, HintStatus.Expired), Is.True);
		}

		var records = (await store.ListAllAsync()).ToDictionary(record => record.Id);
		Assert.That(records[created.Id].Status, Is.EqualTo(HintStatus.Expired));
		Assert.That(records[created.Id].TraceId, Is.EqualTo(createdTraceId));
		Assert.That(records[outside.Id].Status, Is.EqualTo(HintStatus.Expired));
		Assert.That(records[outside.Id].TraceId, Is.Null);
	}

	[TestMethod]
	[Description("Запись, добавленная вне активности, сохраняется и читается без trace id")]
	// Ambient-заполнение читает текущую активность: без трассы её нет —
	// колонка остаётся null и при сохранении, и при чтении из базы.
	// Traceability: openspec:hints/engine-pass#requirement-engine-trace-id-on-created-hints
	public async Task TryIfAddOutsideActivity_TraceIdStaysNull()
	{
		// Guard: между тестами активных активностей нет — иначе сценарий не тот.
		Assert.That(Activity.Current, Is.Null);
		var store = new HintStore(CreateOptions());

		// Act.
		var record = await store.AddAsync(NewRecord());

		// Assert.
		Assert.That(record.TraceId, Is.Null);
		var stored = (await store.ListAllAsync()).Single();
		Assert.That(stored.TraceId, Is.Null);
	}

	[TestMethod]
	[Description("Колонка TraceId в развёрнутой схеме nullable, а запись до внедрения читается с null")]
	// Записи до внедрения трассировки не имеют trace id: колонка развернулась
	// миграцией без ограничения NOT NULL, копия строки без TraceId читается
	// записью с null, созданная в трассе запись — со своим идентификатором.
	// Traceability: openspec:hints/engine-pass#scenario-engine-hints-before-adoption-null
	public async Task TryIfLegacyRecordWithoutTraceId_ReadsAsNull()
	{
		var store = new HintStore(CreateOptions());
		string createdTraceId;
		using (new HintTracingListener(AmbientSourceName))
		{
			// Создание записи в трассе — для контраста со старой записью.
			using var ambient = AmbientSource.StartActivity("pass-ambient");
			Assert.That(ambient, Is.Not.Null);
			await store.AddAsync(NewRecord());
			createdTraceId = ambient!.TraceId.ToHexString();
		}

		using (var db = new JournalDbContext(CreateOptions()))
		{
			// Схема миграции: колонка развернулась и допускает null.
			var ddl = (await db.Database
				.SqlQueryRaw<string>("SELECT sql AS Value FROM sqlite_master WHERE type = 'table' AND name = 'Hints'")
				.SingleAsync()).ToString();
			Assert.That(ddl, Does.Contain("\"TraceId\" TEXT"));
			Assert.That(ddl.Contains("\"TraceId\" TEXT NOT NULL", StringComparison.Ordinal), Is.False);

			// Старая запись: копия строки без колонки TraceId — так её писала
			// прежняя версия хранилища, идентификатора у строки нет.
			await db.Database.ExecuteSqlRawAsync(
				"INSERT INTO Hints (RuleId, SubjectKind, SubjectConstructionId, Character, Clarity, SourcesJson, Text, FactsJson, AsOf, Status, FirstSeenAt, WindowPeriodKey) "
				+ "SELECT RuleId, SubjectKind, SubjectConstructionId, Character, Clarity, SourcesJson, Text, FactsJson, AsOf, Status, FirstSeenAt, WindowPeriodKey FROM Hints");
		}

		var records = await store.ListAllAsync();
		Assert.That(records, Has.Count.EqualTo(2));
		Assert.That(records.Where(record => record.TraceId == createdTraceId).ToList(), Has.Count.EqualTo(1));
		Assert.That(records.Where(record => record.TraceId is null).ToList(), Has.Count.EqualTo(1));
	}

	/// <summary>Опции контекста журнала над временной базой проверки.</summary>
	private DbContextOptions<JournalDbContext> CreateOptions()
		=> new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Создаёт новую запись подсказки конструкции 7.</summary>
	private static HintRecord NewRecord(string ruleId = "tc-rule") => new()
	{
		RuleId = ruleId,
		Subject = HintSubject.ForConstruction(7),
		Character = "risk-mode",
		Clarity = "crisp",
		Sources = [],
		Text = "Текст подсказки.",
		Facts = new Dictionary<string, string> { ["pnlPct"] = "12" },
		AsOf = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero),
		Status = HintStatus.New,
	};
}
