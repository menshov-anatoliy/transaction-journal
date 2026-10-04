using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Sync;
using TransactionJournal.Application.Sync;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain.ConstructionAssembly;

/// <summary>
/// Интеграционная контрольная сверка сборки конструкций на репрезентативной
/// выгрузке полной истории: фиксированный снимок сырья (1847 записей исполнения,
/// 6 delivery-записей, 10 инструментов) прогоняется через план сборки и пересбор
/// хранилища, результат сверяется с контрольными показателями истории — числом
/// конструкций, привязанных сделок, «Входящих», погашением deliveries и
/// ключевыми группировками (стреддл 1600 отдельно, 1900 C/P в цепочке 29MAY).
/// Живая база не используется: снимок зафиксирован в фикстуре проверки.
/// </summary>
[TestClass]
public class ConstructionAssemblyHistoryReconciliationTests
{
	/// <summary>
	/// Эталонные контрольные числа истории: 10 конструкций, 975 привязанных
	/// сделок, 0 во «Входящих». По новому правилу закрытия одна линейная сделка
	/// (1026de2d…), остававшаяся во «Входящих», привязывается к затухающей
	/// конструкции 24APR26-2200 — её опционное прикрытие погибло при живом
	/// фьючерсном остатке, и сделка робота пришлась на период затухания.
	// Traceability: change:close-construction-on-all-positions/specs/domain/construction-assembly/spec#scenario-fading-construction-absorbs-robot-trades
	/// </summary>
	private const int ExpectedConstructions = 10;

	private const int ExpectedBound = 975;

	private const int ExpectedInbox = 0;

	/// <summary>
	/// Имя конструкции цепочки 29MAY, в которую роллом переходят ноги 1900 C/P:
	/// нейминг v2 выводит имя из конечных живых ног — ненулевыми остались только
	/// 1900 C/P доски 25DEC26 равных размеров, поэтому вид — «стреддл».
	/// </summary>
	private const string Chain29MayName = "ETH стреддл 25DEC26 1900";

	/// <summary>
	/// Имя отдельной конструкции 1600, не слившейся с цепочкой: при полном
	/// обнулении ног сохраняется последнее производное имя — колл закрылся
	/// раньше пута, и последнее живое состояние было одиночной ногой пута.
	/// </summary>
	private const string Straddle1600Name = "ETH направленная PUT 25SEP26 1600";

	private static readonly DateTimeOffset FixedNow = new(2026, 9, 27, 0, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-assembly-history-{Guid.NewGuid():N}.db");
		using var db = new JournalDbContext(CreateOptions());
		db.Database.Migrate();
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Пул соединений SQLite держит файл базы открытым — сбрасываем его перед удалением.
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

		// Временная база и соседние WAL/SHM-файлы удаляются после каждой проверки.
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
	[Description("Сборка полной истории даёт контрольные числа, погашает deliveries и воспроизводит ключевые группы")]
	// Контрольная сверка эталонного алгоритма на фиксированной истории: план
	// детерминирован, deliveries погашены, стреддл 1600 открыт отдельно, ноги
	// 1900 C/P перешли роллом в цепочку 29MAY, пересбор воспроизводит план в базе.
	// Traceability: openspec:domain/construction-assembly#requirement-deterministic-option-assembly
	// Traceability: openspec:domain/construction-assembly#requirement-robot-trade-binding
	public async Task TryIfHistoryAssemblyMatchesReferenceNumbers()
	{
		// Arrange: фиксированный снимок сырья загружен в изолированную базу.
		SeedRawStorageFromFixture();
		var service = CreateService();

		// Act: строим план и выполняем пересбор над тем же сырьём.
		var plan = service.BuildPlan(LoadSnapshot());

		// Повторный прогон над тем же сырьём обязан дать структурно тот же план:
		// состав, имена, статусы, остатки ног, привязки и «Входящие» совпадают.
		// Traceability: openspec:domain/construction-assembly#scenario-rebuild-reproduces-result
		var rerun = service.BuildPlan(LoadSnapshot());
		Assert.That(rerun.Constructions.Select(construction => (construction.Id, construction.Name, construction.Status)),
			Is.EqualTo(plan.Constructions.Select(construction => (construction.Id, construction.Name, construction.Status))),
			"Повторный прогон воспроизводит состав, имена и статусы конструкций");
		Assert.That(rerun.Constructions.Select(construction => construction.Legs.Select(leg => (leg.Symbol, leg.Quantity))),
			Is.EqualTo(plan.Constructions.Select(construction => construction.Legs.Select(leg => (leg.Symbol, leg.Quantity)))),
			"Повторный прогон воспроизводит остатки ног");
		Assert.That(rerun.Bindings, Is.EqualTo(plan.Bindings), "Повторный прогон воспроизводит привязки");
		Assert.That(rerun.InboxCount, Is.EqualTo(plan.InboxCount), "Повторный прогон воспроизводит «Входящие»");

		var result = await service.RebuildAsync();

		// Assert: контрольные числа истории и согласованность плана с пересбором.
		Assert.That(plan.Constructions, Has.Count.EqualTo(ExpectedConstructions), "Число конструкций истории");
		Assert.That(plan.Bindings, Has.Count.EqualTo(ExpectedBound), "Число привязанных сделок истории");
		Assert.That(plan.InboxCount, Is.EqualTo(ExpectedInbox), "Число сделок во «Входящих»");
		Assert.That(result.ConstructionsCount, Is.EqualTo(plan.Constructions.Count), "Пересбор создал конструкции плана");
		Assert.That(result.BoundCount, Is.EqualTo(plan.Bindings.Count), "Пересбор создал привязки плана");
		Assert.That(result.TradesInInbox, Is.EqualTo(plan.InboxCount), "«Входящие» пересбора совпадают с планом");

		// Assert: все delivery-записи истории погашены — по каждому символу экспирации
		// суммарный остаток ног всех конструкций нулевой.
		var residualBySymbol = plan.Constructions
			.SelectMany(construction => construction.Legs)
			.GroupBy(leg => leg.Symbol, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.Sum(leg => leg.Quantity), StringComparer.Ordinal);
		foreach (var delivery in LoadSnapshot().Deliveries)
		{
			Assert.That(residualBySymbol.TryGetValue(delivery.Symbol, out var residual), Is.True, $"Нога {delivery.Symbol} есть в плане");
			Assert.That(residual, Is.EqualTo(0m), $"Delivery {delivery.Symbol} погашена");
		}

		// Assert: стреддл 1600 — отдельная конструкция с двумя обнуленными ногами.
		var straddle1600 = plan.Constructions.Single(construction => construction.Name == Straddle1600Name);
		Assert.That(straddle1600.Legs.Select(leg => (leg.Symbol, leg.Quantity)), Is.EqualTo(new[]
		{
			("ETH-25SEP26-1600-C-USDT", 0m),
			("ETH-25SEP26-1600-P-USDT", 0m),
		}), "Стреддл 1600 собран отдельно и закрыт");

		// Assert: ноги 1900 C/P доски 25DEC26 принадлежат цепочке 29MAY.
		var chain29May = plan.Constructions.Single(construction => construction.Name == Chain29MayName);
		Assert.That(chain29May.Legs.Where(leg => leg.Symbol.StartsWith("ETH-25DEC26-1900", StringComparison.Ordinal))
			.Select(leg => leg.Quantity), Is.EquivalentTo(new[] { 0.5m, 0.5m }), "Ноги 1900 C/P живут в цепочке 29MAY");

		// Assert: база воспроизводит план — имена конструкций в порядке плана,
		// привязки сделок указывают на те же конструкции.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var constructions = db.Constructions.OrderBy(construction => construction.Id).ToList();
			Assert.That(constructions.Select(construction => construction.Name),
				Is.EqualTo(plan.Constructions.Select(construction => construction.Name)), "Состав и порядок конструкций базы совпадают с планом");

			var nameByPlannedId = plan.Constructions.ToDictionary(construction => construction.Id, construction => construction.Name);
			var bindings = db.TradeUserdata
				.ToDictionary(userdata => userdata.ExecId, userdata => constructions.Single(c => c.Id == userdata.ConstructionId).Name);
			Assert.That(bindings, Has.Count.EqualTo(ExpectedBound), "Число привязок в базе совпадает с планом");
			foreach (var pair in plan.Bindings)
			{
				Assert.That(bindings.TryGetValue(pair.Key, out var databaseName), Is.True, $"Сделка {pair.Key} привязана");
				Assert.That(databaseName, Is.EqualTo(nameByPlannedId[pair.Value]), $"Привязка {pair.Key} указывает на конструкцию плана");
			}
		}
	}

	#region Помощники

	/// <summary>Команда пересбора над настоящим адаптером сырого хранилища с фиксированным временем.</summary>
	private ConstructionAssemblyService CreateService() => new(
		new JournalSyncStore(CreateOptions()),
		new StubJournalBackupService(),
		CreateOptions(),
		new FixedTimeProvider(FixedNow));

	/// <summary>Создаёт опции контекста журнала над временной SQLite-базой проверки.</summary>
	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Загружает фиксированный снимок истории из fixture проверки.</summary>
	private static JournalRawSnapshot LoadSnapshot()
	{
		var fixturePath = Path.Combine(AppContext.BaseDirectory, "Domain", "ConstructionAssembly", "Fixtures", "history-snapshot-20260926.json");
		using var document = JsonDocument.Parse(File.ReadAllText(fixturePath));
		var instruments = document.RootElement.GetProperty("instruments").EnumerateArray()
			.Select(item => new RawInstrument
			{
				Symbol = item.GetProperty("symbol").GetString()!,
				Category = item.GetProperty("category").GetString()!,
				PayloadJson = item.GetProperty("payloadJson").GetString()!,
				FetchedAt = DateTimeOffset.Parse(item.GetProperty("fetchedAt").GetString()!),
			})
			.ToList();
		var executions = document.RootElement.GetProperty("executions").EnumerateArray()
			.Select(item => new RawExecution
			{
				ExecId = item.GetProperty("execId").GetString()!,
				Category = item.GetProperty("category").GetString()!,
				Symbol = item.GetProperty("symbol").GetString()!,
				ExecTimeMs = item.GetProperty("execTimeMs").GetInt64(),
				PayloadJson = item.GetProperty("payloadJson").GetString()!,
				FetchedAt = DateTimeOffset.Parse(item.GetProperty("fetchedAt").GetString()!),
			})
			.ToList();
		var deliveries = document.RootElement.GetProperty("deliveries").EnumerateArray()
			.Select(item => new RawDelivery
			{
				Symbol = item.GetProperty("symbol").GetString()!,
				DeliveryTimeMs = item.GetProperty("deliveryTimeMs").GetInt64(),
				Category = item.GetProperty("category").GetString()!,
				PayloadJson = item.GetProperty("payloadJson").GetString()!,
				FetchedAt = DateTimeOffset.Parse(item.GetProperty("fetchedAt").GetString()!),
			})
			.ToList();
		return new JournalRawSnapshot
		{
			Instruments = instruments,
			Executions = executions,
			Deliveries = deliveries,
		};
	}

	/// <summary>Наполняет сырьё изолированной базы фиксированным снимком истории.</summary>
	private void SeedRawStorageFromFixture()
	{
		var snapshot = LoadSnapshot();
		using var db = new JournalDbContext(CreateOptions());
		db.RawInstruments.AddRange(snapshot.Instruments);
		db.RawExecutions.AddRange(snapshot.Executions);
		db.RawDeliveries.AddRange(snapshot.Deliveries);
		db.SaveChanges();
	}

	/// <summary>Поставщик фиксированного времени для детерминированных проверок.</summary>
	private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => utcNow;
	}

	#endregion
}
