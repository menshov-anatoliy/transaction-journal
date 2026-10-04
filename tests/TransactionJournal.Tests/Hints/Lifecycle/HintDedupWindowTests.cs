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
/// Проверки дедуп-окна «правило × субъект (+ период)» сквозным прогоном над
/// реальным хранилищем SQLite: непрерывное условие держит одну живую запись,
/// отклонённая запись подавляет повтор в окне, смена периода периодного
/// правила открывает новое окно, ушедшее и вернувшееся условие гасит и
/// порождает записи заново. Агент пишет только New и Expired: applied и
/// dismissed в тестах ставит человек — тот же вызов хранилища, что и UI-команда.
/// </summary>
[TestClass]
public class HintDedupWindowTests
{
	private string _databasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"hint-dedup-tests-{Guid.NewGuid():N}.db");
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
	[Description("Непрерывное условие на нескольких проходах держит одну живую запись без дублей")]
	// Запись текущего окна подавляет повторные срабатывания того же ключа:
	// три прохода подряд дают ровно одну живую подсказку.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-dedup-rule-subject-window
	// Traceability: openspec:hints/hint-lifecycle#scenario-hint-dedup-suppresses-in-window
	public async Task TryIfConditionHoldsOnRepeatedPasses_SingleLiveRecordWithoutDuplicates()
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

			var (first, _) = await HintPassHarness.RunAsync(dir, snapshot, store);
			var (second, secondAdded) = await HintPassHarness.RunAsync(dir, snapshot, store);
			var (third, thirdAdded) = await HintPassHarness.RunAsync(dir, snapshot, store);
			var records = await store.ListAllAsync();

			Assert.That(first.CreatedHints, Is.EqualTo(1));
			Assert.That(second.CreatedHints, Is.EqualTo(0));
			Assert.That(secondAdded, Is.Empty);
			Assert.That(third.CreatedHints, Is.EqualTo(0));
			Assert.That(thirdAdded, Is.Empty);
			Assert.That(first.ExpiredHints, Is.EqualTo(0));
			Assert.That(second.ExpiredHints, Is.EqualTo(0));
			Assert.That(records, Has.Count.EqualTo(1));
			Assert.That(records[0].Status, Is.EqualTo(HintStatus.New));
			Assert.That(records[0].WindowPeriodKey, Is.Null);
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Отклонённая подсказка подавляет повтор в окне и агентом не гасится")]
	// Человек отклонил живую подсказку (UI-команда к хранилищу): пока условие
	// продолжается, новая подсказка не создаётся, а dismissed остаётся
	// терминальным — переходы для него запрещены.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-dedup-rule-subject-window
	// Traceability: openspec:hints/hint-lifecycle#scenario-hint-dismissed-suppresses-window
	public async Task TryIfDismissedInWindow_NoNewHintAndRecordStaysDismissed()
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

			// Человек отклоняет подсказку в UI — единственный источник статуса dismissed.
			Assert.That(await store.TryTransitionAsync(record.Id, HintStatus.Dismissed), Is.True);

			var (second, secondAdded) = await HintPassHarness.RunAsync(dir, snapshot, store);
			var (third, thirdAdded) = await HintPassHarness.RunAsync(dir, snapshot, store);
			var records = await store.ListAllAsync();

			Assert.That(second.CreatedHints, Is.EqualTo(0));
			Assert.That(secondAdded, Is.Empty);
			Assert.That(third.CreatedHints, Is.EqualTo(0));
			Assert.That(thirdAdded, Is.Empty);
			Assert.That(second.ExpiredHints, Is.EqualTo(0));
			Assert.That(records, Has.Count.EqualTo(1));
			Assert.That(records[0].Status, Is.EqualTo(HintStatus.Dismissed));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Смена периода периодного правила открывает новое окно — отклонённая запись прошлого окна не мешает")]
	// Убыток недели 41 (5% при лимите 4%) отклонён человеком; на неделе 42
	// условие продолжается (6%), ключ окна меняется на 2026-W42 — создаётся
	// новая подсказка, отклонённая запись прошлого периода остаётся историей.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-dedup-rule-subject-window
	// Traceability: openspec:hints/hint-lifecycle#scenario-hint-period-change-new-window
	public async Task TryIfPeriodChanges_DismissedOldWindow_DoesNotSuppressNewWindow()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "ac-01.yaml", CorpusYaml.Card(
				"ac-01",
				implementation: "risk-limit-period",
				hintTemplate: "Убыток за {period}: {lossPct}% при лимите {limitPct}%.",
				scope: "portfolio",
				thresholds:
				[
					("weeklyRiskLimit", "4", "percent"),
					("monthlyRiskLimit", "5", "percent"),
					("quarterlyRiskLimit", "10", "percent"),
				]));
			var store = new HintStore(CreateOptions());
			var week41 = HintPassHarness.Construction(
				id: 1,
				capital: 1000m,
				trades:
				[
					// Неделя 41: покупка 1 BTC по 100 и продажа по 50 — убыток 5%.
					HintPassHarness.Trade("w41-buy", "BTCUSDT", 1m, 100m, 0m, dayOffset: -2),
					HintPassHarness.Trade("w41-sell", "BTCUSDT", -1m, 50m, 0m, dayOffset: -1),
				]);
			var week42 = week41 with
			{
				Trades =
				[
					..week41.Trades,
					// Неделя 42: покупка 1 BTC по 100 и продажа по 40 — убыток 6%.
					HintPassHarness.Trade("w42-buy", "BTCUSDT", 1m, 100m, 0m, dayOffset: 5),
					HintPassHarness.Trade("w42-sell", "BTCUSDT", -1m, 40m, 0m, dayOffset: 6),
				],
			};

			var (first, _) = await HintPassHarness.RunAsync(dir, new JournalSnapshot { Constructions = [week41] }, store);
			var record = (await store.ListAllAsync()).Single();
			Assert.That(record.WindowPeriodKey, Is.EqualTo("2026-W41"));
			Assert.That(record.Facts["period"], Is.EqualTo("неделя"));

			// Человек отклоняет подсказку недели 41 в UI.
			Assert.That(await store.TryTransitionAsync(record.Id, HintStatus.Dismissed), Is.True);

			// as-of недели 42: условие продолжается, ключ периода сменился.
			var (second, secondAdded) = await HintPassHarness.RunAsync(
				dir,
				new JournalSnapshot { Constructions = [week42] },
				store,
				now: new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero));
			var records = await store.ListAllAsync();

			Assert.That(first.CreatedHints, Is.EqualTo(1));
			Assert.That(second.CreatedHints, Is.EqualTo(1));
			Assert.That(secondAdded, Has.Count.EqualTo(1));
			Assert.That(second.ExpiredHints, Is.EqualTo(0));
			Assert.That(records, Has.Count.EqualTo(2));
			var oldRecord = records.Single(item => item.WindowPeriodKey == "2026-W41");
			var newRecord = records.Single(item => item.WindowPeriodKey == "2026-W42");
			Assert.That(oldRecord.Status, Is.EqualTo(HintStatus.Dismissed));
			Assert.That(newRecord.Status, Is.EqualTo(HintStatus.New));
			Assert.That(newRecord.Subject.Kind, Is.EqualTo(HintSubjectKind.Journal));
			Assert.That(newRecord.Facts["lossPct"], Is.EqualTo("6"));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Ушедшее условие гасит запись в expired, вернувшееся — порождает новую подсказку")]
	// Условие выполнилось и погасло: агент переводит запись в expired; новое
	// выполнение того же ключа создаёт новую запись, expired окно не подавляет.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-dedup-rule-subject-window
	// Traceability: openspec:hints/hint-lifecycle#scenario-hint-condition-returns-new-hint
	public async Task TryIfConditionLeftThenReturns_NewHintAfterExpired()
	{
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-target.yaml", CorpusYaml.Card(
				"tc-target",
				implementation: "profit-target-reached",
				hintTemplate: "Результат {pnlPct}% — плановая прибыль достигнута."));
			var store = new HintStore(CreateOptions());
			var firing = new JournalSnapshot
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
			var quiet = firing with
			{
				Constructions =
				[
					HintPassHarness.Construction(
						id: 7,
						capital: 1000m,
						profitValue: 10m,
						profitUnit: TargetUnit.Percent,
						totalPnL: 90m),
				],
			};

			await HintPassHarness.RunAsync(dir, firing, store);
			var (second, secondAdded) = await HintPassHarness.RunAsync(dir, quiet, store);
			var (third, thirdAdded) = await HintPassHarness.RunAsync(dir, firing, store);
			var records = await store.ListAllAsync();

			Assert.That(second.CreatedHints, Is.EqualTo(0));
			Assert.That(secondAdded, Is.Empty);
			Assert.That(second.ExpiredHints, Is.EqualTo(1));
			Assert.That(third.CreatedHints, Is.EqualTo(1));
			Assert.That(thirdAdded, Has.Count.EqualTo(1));
			Assert.That(third.ExpiredHints, Is.EqualTo(0));
			Assert.That(records, Has.Count.EqualTo(2));
			Assert.That(records[0].Status, Is.EqualTo(HintStatus.Expired));
			Assert.That(records[1].Status, Is.EqualTo(HintStatus.New));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}
}
