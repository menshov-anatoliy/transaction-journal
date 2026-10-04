namespace TransactionJournal.Tests.Infrastructure.Hints;

using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using TransactionJournal.Hints;
using TransactionJournal.Hints.Ports;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Infrastructure.Hints;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// Адаптер хранилища подсказок на SQLite: переходы статусов, терминальность,
/// однократная фиксация first-seen и разделение живых записей и истории.
/// </summary>
[TestClass]
public class HintStoreLifecycleTests
{
	private string _databasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"hint-store-tests-{Guid.NewGuid():N}.db");
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

	[TestMethod]
	[Description("Переходы из new в applied, dismissed и expired выполняются и сохраняются")]
	// Хранилище принимает переходы из new: человек в UI ставит applied,
	// агент гасит в expired; dismissed — тот же человеко-переход хранилища.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-lifecycle-transitions
	// Traceability: openspec:hints/hint-lifecycle#scenario-hint-applied-by-human-in-ui
	// Traceability: openspec:hints/hint-lifecycle#scenario-hint-expired-by-agent
	public async Task TryIfTransitionFromNew_ToEachTerminalStatus_Succeeds()
	{
		var store = new HintStore(CreateOptions());
		var applied = await store.AddAsync(NewRecord());
		var dismissed = await store.AddAsync(NewRecord());
		var expired = await store.AddAsync(NewRecord());

		Assert.That(await store.TryTransitionAsync(applied.Id, HintStatus.Applied), Is.True);
		Assert.That(await store.TryTransitionAsync(dismissed.Id, HintStatus.Dismissed), Is.True);
		Assert.That(await store.TryTransitionAsync(expired.Id, HintStatus.Expired), Is.True);

		var records = (await store.ListAllAsync()).ToDictionary(record => record.Id);
		Assert.That(records[applied.Id].Status, Is.EqualTo(HintStatus.Applied));
		Assert.That(records[dismissed.Id].Status, Is.EqualTo(HintStatus.Dismissed));
		Assert.That(records[expired.Id].Status, Is.EqualTo(HintStatus.Expired));
	}

	[TestMethod]
	[Description("Из терминальных статусов переходы запрещены и статус сохраняется")]
	// Терминальность: applied, dismissed и expired нельзя перевести ни в какой
	// другой статус — повторные и обратные переходы отвергаются.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-lifecycle-transitions
	// Traceability: openspec:hints/hint-lifecycle#scenario-hint-applied-by-human-in-ui
	public async Task TryIfTransitionFromTerminalStatus_ForbiddenAndStatusStays()
	{
		var store = new HintStore(CreateOptions());
		var applied = await store.AddAsync(NewRecord());
		var dismissed = await store.AddAsync(NewRecord());
		var expired = await store.AddAsync(NewRecord());
		Assert.That(await store.TryTransitionAsync(applied.Id, HintStatus.Applied), Is.True);
		Assert.That(await store.TryTransitionAsync(dismissed.Id, HintStatus.Dismissed), Is.True);
		Assert.That(await store.TryTransitionAsync(expired.Id, HintStatus.Expired), Is.True);

		Assert.That(await store.TryTransitionAsync(applied.Id, HintStatus.Dismissed), Is.False);
		Assert.That(await store.TryTransitionAsync(applied.Id, HintStatus.Expired), Is.False);
		Assert.That(await store.TryTransitionAsync(dismissed.Id, HintStatus.Applied), Is.False);
		Assert.That(await store.TryTransitionAsync(dismissed.Id, HintStatus.Expired), Is.False);
		Assert.That(await store.TryTransitionAsync(expired.Id, HintStatus.Applied), Is.False);
		Assert.That(await store.TryTransitionAsync(expired.Id, HintStatus.Dismissed), Is.False);

		var records = (await store.ListAllAsync()).ToDictionary(record => record.Id);
		Assert.That(records[applied.Id].Status, Is.EqualTo(HintStatus.Applied));
		Assert.That(records[dismissed.Id].Status, Is.EqualTo(HintStatus.Dismissed));
		Assert.That(records[expired.Id].Status, Is.EqualTo(HintStatus.Expired));
	}

	[TestMethod]
	[Description("Переход в new и для несуществующей записи отвергается")]
	// Статус new — только начальное состояние записи; несуществующий
	// идентификатор тоже не даёт перехода.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-lifecycle-transitions
	public async Task TryIfTransitionToNewOrUnknownId_ReturnsFalse()
	{
		var store = new HintStore(CreateOptions());
		var record = await store.AddAsync(NewRecord());

		Assert.That(await store.TryTransitionAsync(record.Id, HintStatus.New), Is.False);
		Assert.That(await store.TryTransitionAsync(999, HintStatus.Expired), Is.False);
		Assert.That((await store.ListAllAsync())[0].Status, Is.EqualTo(HintStatus.New));
	}

	[TestMethod]
	[Description("first-seen фиксируется однократно при первом запросе")]
	// Повторная установка не двигает отметку: первый раз видел — однажды.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-lifecycle-transitions
	// Traceability: openspec:hints/hint-lifecycle#scenario-hint-first-seen-once
	public async Task TryIfMarkFirstSeen_SetsOnce()
	{
		var store = new HintStore(CreateOptions());
		var record = await store.AddAsync(NewRecord());
		var first = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
		var second = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

		await store.MarkFirstSeenAsync(record.Id, first);
		await store.MarkFirstSeenAsync(record.Id, second);

		var stored = (await store.ListAllAsync()).Single();
		Assert.That(stored.FirstSeenAt, Is.EqualTo(first));
	}

	[TestMethod]
	[Description("Живые записи отделены от истории: терминальные остаются в list-all, но уходят из list-live")]
	// История переходов сохраняется целиком: терминальные записи остаются
	// в полном списке, живой список содержит только new.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-lifecycle-transitions
	// Traceability: openspec:hints/hint-lifecycle#scenario-hint-full-history-retained
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-dedup-rule-subject-window
	public async Task TryIfTerminalRecords_ListAllKeepsHistory_ListLiveExcludes()
	{
		var store = new HintStore(CreateOptions());
		var staying = await store.AddAsync(NewRecord());
		var applied = await store.AddAsync(NewRecord());
		var dismissed = await store.AddAsync(NewRecord());
		var expired = await store.AddAsync(NewRecord());
		Assert.That(await store.TryTransitionAsync(applied.Id, HintStatus.Applied), Is.True);
		Assert.That(await store.TryTransitionAsync(dismissed.Id, HintStatus.Dismissed), Is.True);
		Assert.That(await store.TryTransitionAsync(expired.Id, HintStatus.Expired), Is.True);

		var all = await store.ListAllAsync();
		var live = await store.ListLiveAsync();

		Assert.That(all, Has.Count.EqualTo(4));
		Assert.That(live.Select(record => record.Id), Is.EquivalentTo(new[] { staying.Id }));
	}
}
