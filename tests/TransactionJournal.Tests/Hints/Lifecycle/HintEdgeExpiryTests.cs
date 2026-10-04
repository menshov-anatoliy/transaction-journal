namespace TransactionJournal.Tests.Hints.Lifecycle;

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
/// Граничные гашения прохода сквозным прогоном над реальным хранилищем
/// SQLite: закрытие конструкции-субъекта гасит её живые подсказки, вывод
/// правила из корпуса (retired или удаление файла) гасит подсказки правила.
/// Агент выполняет гашение самостоятельно — человек в сценарии не участвует.
/// </summary>
[TestClass]
public class HintEdgeExpiryTests
{
	private string _databasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"hint-edge-tests-{Guid.NewGuid():N}.db");
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

	/// <summary>Опции контекста журнала над временной базой проверки.</summary>
	private DbContextOptions<JournalDbContext> CreateOptions()
		=> new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	[TestMethod]
	[Description("Закрытие конструкции гасит её живую подсказку следующим проходом")]
	// Пока конструкция открыта — подсказка живёт; после закрытия конструкции
	// агент первым же проходом гасит её подсказку в expired.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-expiry-on-subject-close-and-retirement
	// Traceability: openspec:hints/hint-lifecycle#scenario-hint-expired-on-construction-close
	public async Task TryIfConstructionClosed_LiveHintExpiredOnNextPass()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-target.yaml", CorpusYaml.Card(
				"tc-target",
				implementation: "profit-target-reached",
				hintTemplate: "Результат {pnlPct}% — плановая прибыль достигнута."));
			var store = new HintStore(CreateOptions());
			var open = new JournalSnapshot
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
			var closed = open with
			{
				Constructions =
				[
					HintPassHarness.Construction(
						id: 7,
						isOpen: false,
						capital: 1000m,
						profitValue: 10m,
						profitUnit: TargetUnit.Percent,
						totalPnL: 120m),
				],
			};

			var (first, _) = await HintPassHarness.RunAsync(dir, open, store);
			var record = (await store.ListAllAsync()).Single();
			Assert.That(first.CreatedHints, Is.EqualTo(1));
			Assert.That(record.Status, Is.EqualTo(HintStatus.New));
			Assert.That(record.Subject.Kind, Is.EqualTo(HintSubjectKind.Construction));
			Assert.That(record.Subject.ConstructionId, Is.EqualTo(7));

			var (second, secondAdded) = await HintPassHarness.RunAsync(dir, closed, store);
			var records = await store.ListAllAsync();

			Assert.That(second.Outcome, Is.EqualTo(HintPassOutcome.Completed));
			Assert.That(second.CreatedHints, Is.EqualTo(0));
			Assert.That(secondAdded, Is.Empty);
			Assert.That(second.ExpiredHints, Is.EqualTo(1));
			Assert.That(records, Has.Count.EqualTo(1));
			Assert.That(records[0].Status, Is.EqualTo(HintStatus.Expired));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Вывод правила из корпуса в retired гасит его живую подсказку")]
	// Правило снимают с сопровождения: файл корпуса остаётся, но статус
	// retired — агент гасит его живые подсказки в expired.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-expiry-on-subject-close-and-retirement
	// Traceability: openspec:hints/hint-lifecycle#scenario-hint-expired-on-rule-retirement
	public async Task TryIfRuleRetired_LiveHintExpiredOnNextPass()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-target.yaml", CorpusYaml.Card(
				"tc-target",
				implementation: "profit-target-reached",
				hintTemplate: "Результат {pnlPct}% — плановая прибыль достигнута."));
			var store = new HintStore(CreateOptions());
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

			await HintPassHarness.RunAsync(dir, snapshot, store);
			var record = (await store.ListAllAsync()).Single();
			Assert.That(record.Status, Is.EqualTo(HintStatus.New));

			// Правило выводят из корпуса: карточка остаётся, но со статусом retired.
			CorpusYaml.Write(dir, "tc-target.yaml", CorpusYaml.Card(
				"tc-target",
				implementation: "profit-target-reached",
				status: "retired",
				retiredReason: "Правило снято с сопровождения"));
			var (second, secondAdded) = await HintPassHarness.RunAsync(dir, snapshot, store);
			var records = await store.ListAllAsync();

			Assert.That(second.Outcome, Is.EqualTo(HintPassOutcome.Completed));
			Assert.That(second.CreatedHints, Is.EqualTo(0));
			Assert.That(secondAdded, Is.Empty);
			Assert.That(second.ExpiredHints, Is.EqualTo(1));
			Assert.That(records, Has.Count.EqualTo(1));
			Assert.That(records[0].Status, Is.EqualTo(HintStatus.Expired));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Удаление карточки правила из корпуса гасит его живую подсказку")]
	// Правило удаляют из корпуса вместе с карточкой: на следующем проходе его
	// нет среди активных правил, и агент гасит живую подсказку. Корпус при
	// этом остаётся непустым — вторая неисполняемая карточка удерживает
	// валидность каталога.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-expiry-on-subject-close-and-retirement
	// Traceability: openspec:hints/hint-lifecycle#scenario-hint-expired-on-rule-retirement
	public async Task TryIfRuleCardDeleted_LiveHintExpiredOnNextPass()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			// Вторая карточка без implementation не исполняется, но удерживает корпус непустым.
			CorpusYaml.Write(dir, "tc-other.yaml", CorpusYaml.Card(
				"tc-other",
				hintTemplate: "Пока не исполняется."));
			CorpusYaml.Write(dir, "tc-target.yaml", CorpusYaml.Card(
				"tc-target",
				implementation: "profit-target-reached",
				hintTemplate: "Результат {pnlPct}% — плановая прибыль достигнута."));
			var store = new HintStore(CreateOptions());
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

			await HintPassHarness.RunAsync(dir, snapshot, store);
			var record = (await store.ListAllAsync()).Single();
			Assert.That(record.Status, Is.EqualTo(HintStatus.New));
			Assert.That(record.RuleId, Is.EqualTo("tc-target"));

			File.Delete(Path.Combine(dir, "tc-target.yaml"));
			var (second, secondAdded) = await HintPassHarness.RunAsync(dir, snapshot, store);
			var records = await store.ListAllAsync();

			Assert.That(second.Outcome, Is.EqualTo(HintPassOutcome.Completed));
			Assert.That(second.CreatedHints, Is.EqualTo(0));
			Assert.That(secondAdded, Is.Empty);
			Assert.That(second.ExpiredHints, Is.EqualTo(1));
			Assert.That(records, Has.Count.EqualTo(1));
			Assert.That(records[0].Status, Is.EqualTo(HintStatus.Expired));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}
}
