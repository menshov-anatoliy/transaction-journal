using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Application.Sync;
using TransactionJournal.Domain.Bybit;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Sync;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Tests.Infrastructure.Bybit;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Application.Sync;

/// <summary>
/// Интеграционные проверки сосуществования ручных сделок с синхронизацией на
/// реальном SQLite-хранилище и фиктивной бирже: запуск синхронизации не меняет
/// и не удаляет ручные сделки, а дубликат одного исполнения — внесённого
/// вручную и пришедшего синхронизацией — остаётся двумя записями журнала без
/// автодедупликации. Стек оркестратора, виртуальные часы и порядок страниц
/// фиктивной биржи повторяют JournalSyncServiceTests; журнал читается read-моделью
/// «Входящих» — тем же чтением, что показывает записи пользователю.
/// Требование: синхронизация не изменяет и не удаляет ручные сделки, а дубликат
/// исполнения остаётся обеими записями до ручного разрешения пользователем.
/// Traceability: openspec:domain/manual-trades#requirement-manual-sync-coexistence
/// </summary>
[TestClass]
public class ManualTradeSyncCoexistenceTests
{
	private const string LinearSymbol = "BTCUSDT";

	/// <summary>Биржевой идентификатор исполнения, дублирующего ручную сделку.</summary>
	private const string DuplicateExecId = "exec-dup";

	// Виртуальные часы фиксированы на 2026-01-01: окна backfill отсчитываются от этой даты.
	private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
	private static readonly long NowMs = Now.ToUnixTimeMilliseconds();
	private static readonly long DayMs = 86_400_000L;
	private static readonly long WeekMs = ExecutionWindowPass.MaxWindowMs;

	private string _databasePath = null!;
	private JournalSyncStore _store = null!;
	private ScriptedExchangeGateway _gateway = null!;
	private JournalSyncService _service = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-manual-sync-coexistence-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		_store = new JournalSyncStore(CreateOptions(), new ManualTimeProvider());
		_gateway = new ScriptedExchangeGateway();

		// Полный стек оркестратора над общим хранилищем и фиктивной биржей —
		// как в JournalSyncServiceTests: список активов опционной доски отдаёт заглушка.
		var executionEngine = new ExecutionCategorySync(
			new ExecutionWindowPass(_gateway, _store),
			_gateway,
			_store,
			new FakeOptionBaseCoinSource("BTC"),
			new ManualTimeProvider(),
			_store);
		var deliveryEngine = new DeliveryCategorySync(
			new DeliveryWindowPass(_gateway, _store), _gateway, _store, new ManualTimeProvider(), _store);
		_service = new JournalSyncService(
			executionEngine,
			deliveryEngine,
			new InstrumentReferenceSync(_gateway, _store),
			_store,
			_store,
			_store,
			new JournalMaterializer(),
			new ExecutionCategorySyncOptions(),
			new DeliveryCategorySyncOptions(),
			new ManualTimeProvider());
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
	[Description("Запуск синхронизации при наличии ручных сделок не меняет и не удаляет их")]
	// Синхронизация приносит биржевые записи и пополняет справочник, но сырьё
	// ручных сделок остаётся без изменений: атрибуты, привязка и отметки
	// создания/правки сохраняются, второй строки не появляется.
	// Traceability: openspec:domain/manual-trades#scenario-sync-never-touches-manual-trades
	public async Task TryIfSyncNeverTouchesManualTrades()
	{
		// Arrange: ручная сделка во «Входящих» до первого запуска синхронизации.
		var seeded = await SeedManualTradeAsync();

		// Биржа отдаёт чужую новую линейную сделку и спецификацию инструмента:
		// запуск синхронизации реально приносит записи и обновляет справочник.
		_gateway.EnqueueExecution(ExecutionPage(ExchangeExecution("exec-new")));
		_gateway.AddInstrument(LinearInstrument());

		// Act
		var result = await _service.SyncAsync();

		// Assert: запуск успешен и принёс биржевую запись — проверка не пустая.
		Assert.That(result.Run.Status, Is.EqualTo(SyncRunStatus.Succeeded));
		Assert.That(result.Run.NewExecutions, Is.EqualTo(1));

		// Ручная сделка не тронута: одна строка, все атрибуты совпадают с сеяными.
		using var db = new JournalDbContext(CreateOptions());
		var manualTrades = await db.ManualTrades.ToListAsync();
		Assert.That(manualTrades, Has.Count.EqualTo(1));
		var untouched = manualTrades.Single();
		Assert.That(untouched.Id, Is.EqualTo(seeded.Id));
		Assert.That(untouched.ExecutedAt, Is.EqualTo(seeded.ExecutedAt));
		Assert.That(untouched.Symbol, Is.EqualTo(seeded.Symbol));
		Assert.That(untouched.Side, Is.EqualTo(seeded.Side));
		Assert.That(untouched.Quantity, Is.EqualTo(seeded.Quantity));
		Assert.That(untouched.Price, Is.EqualTo(seeded.Price));
		Assert.That(untouched.FeeUsdt, Is.EqualTo(seeded.FeeUsdt));
		Assert.That(untouched.ConstructionId, Is.EqualTo(seeded.ConstructionId));
		Assert.That(untouched.CreatedAt, Is.EqualTo(seeded.CreatedAt));
		Assert.That(untouched.UpdatedAt, Is.EqualTo(seeded.UpdatedAt));
	}

	[TestMethod]
	[Description("Дубликат исполнения — ручная и биржевая записи — остаётся двумя записями журнала")]
	// Одно и то же исполнение, внесённое вручную и пришедшее синхронизацией,
	// остаётся обеими записями: автодедупликация отсутствует, пользователь
	// находит дубликат по бейджу источника и удаляет копию сам.
	// Traceability: openspec:domain/manual-trades#scenario-duplicate-resolved-by-user
	// Traceability: change:manual-journal-management/design#d1
	public async Task TryIfDuplicateExecutionStaysTwoRecords()
	{
		// Arrange: ручная сделка с атрибутами исполнения, которое сейчас придёт синхронизацией.
		var seeded = await SeedManualTradeAsync(execId: DuplicateExecId);

		_gateway.EnqueueExecution(ExecutionPage(ExchangeExecution(DuplicateExecId)));
		_gateway.AddInstrument(LinearInstrument());

		// Act
		var result = await _service.SyncAsync();

		// Assert: исполнение принято биржевым сырьём, ручная строка осталась в своём сырье.
		Assert.That(result.Run.Status, Is.EqualTo(SyncRunStatus.Succeeded));
		Assert.That(result.Run.NewExecutions, Is.EqualTo(1));

		using (var db = new JournalDbContext(CreateOptions()))
		{
			Assert.That(await db.ManualTrades.CountAsync(trade => trade.Id == seeded.Id), Is.EqualTo(1));
			Assert.That(await db.RawExecutions.CountAsync(execution => execution.ExecId == DuplicateExecId), Is.EqualTo(1));
		}

		// Журнал — общий список обоих источников — показывает ровно две записи:
		// биржевую с execId и ручную с идентификатором своего сырья, обе с теми
		// же атрибутами исполнения.
		var inbox = await new InboxReadModel(CreateOptions()).ListAsync();
		Assert.That(inbox, Has.Count.EqualTo(2));

		var exchange = inbox.Single(trade => trade.Source == TradeSource.Exchange);
		Assert.That(exchange.ExecId, Is.EqualTo(DuplicateExecId));
		Assert.That(exchange.Symbol, Is.EqualTo(LinearSymbol));
		Assert.That(exchange.Quantity, Is.EqualTo(0.01m));

		var manual = inbox.Single(trade => trade.Source == TradeSource.Manual);
		Assert.That(manual.ExecId, Is.Null);
		Assert.That(manual.ManualTradeId, Is.EqualTo(seeded.Id));
		Assert.That(manual.Symbol, Is.EqualTo(LinearSymbol));
		Assert.That(manual.ExecutedAt, Is.EqualTo(seeded.ExecutedAt));
		Assert.That(manual.Quantity, Is.EqualTo(0.01m));
		Assert.That(manual.Price, Is.EqualTo(42000m));
	}

	#region Помощники

	/// <summary>Сеет ручную сделку «Входящих» и возвращает сохранённую строку сырья.</summary>
	/// <param name="execId">Биржевой идентификатор, атрибутами которого дублируется сделка; null — сделка без дубликата.</param>
	private async Task<ManualTrade> SeedManualTradeAsync(string? execId = null)
	{
		// Время исполнения дубликата совпадает с исполнением биржи из сценария:
		// даже при полном совпадении атрибутов записи остаются двумя строками.
		var executedAt = execId is null
			? Now.AddHours(-2)
			: DateTimeOffset.FromUnixTimeMilliseconds(NowMs - DayMs);
		var trade = new ManualTrade
		{
			ExecutedAt = executedAt,
			Symbol = LinearSymbol,
			Side = "Buy",
			Quantity = 0.01m,
			Price = 42000m,
			FeeUsdt = 0.5m,
			ConstructionId = null,
			CreatedAt = Now.AddHours(-1),
			UpdatedAt = Now.AddHours(-1),
		};
		using var db = new JournalDbContext(CreateOptions());
		db.ManualTrades.Add(trade);
		await db.SaveChangesAsync();
		return trade;
	}

	/// <summary>Заготовка биржевого исполнения линейного перпа: торговая запись Trade.</summary>
	/// <param name="execId">Биржевой идентификатор исполнения.</param>
	private static BybitExecution ExchangeExecution(string execId) => new()
	{
		Symbol = LinearSymbol,
		ExecId = execId,
		Side = "Buy",
		ExecPrice = 42000m,
		ExecQty = 0.01m,
		ExecFee = 0.0042m,
		FeeCurrency = "USDT",
		// Исполнение фикстуры — торговая запись: сделка материализуется только из Trade.
		ExecType = "Trade",
		ExecTimeMs = NowMs - DayMs,
	};

	/// <summary>Спецификация линейного перпа для пополнения справочника при синхронизации.</summary>
	private static BybitInstrumentInfo LinearInstrument() => new()
	{
		Symbol = LinearSymbol,
		ContractType = "LinearPerpetual",
		Status = "Trading",
		BaseCoin = "BTC",
		QuoteCoin = "USDT",
		SettleCoin = "USDT",
		DeliveryTimeMs = 0L,
	};

	private static BybitPagedResponse<BybitExecution> ExecutionPage(params BybitExecution[] executions) => new()
	{
		List = executions,
	};

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	#endregion

	#region Фиктивная биржа

	/// <summary>
	/// Фиктивный источник базовых активов опционной доски: возвращает заготовленный
	/// список активов без обращений к бирже.
	/// </summary>
	private sealed class FakeOptionBaseCoinSource : IOptionBaseCoinSource
	{
		private readonly IReadOnlyList<string> _baseCoins;

		public FakeOptionBaseCoinSource(params string[] baseCoins)
		{
			_baseCoins = baseCoins;
		}

		public Task<IReadOnlyList<string>> GetBaseCoinsAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult(_baseCoins);
	}

	/// <summary>
	/// Фиктивная биржа: раздаёт заготовленные страницы истории исполнения и спецификации
	/// инструментов по порядку. При исчерпании сценария и для delivery-истории отвечает
	/// пустой страницей без курсора; окна глубже пола первой недели не расходуют сценарий.
	/// </summary>
	private sealed class ScriptedExchangeGateway : IBybitHistoryGateway, IBybitInstrumentSource
	{
		private readonly Queue<BybitPagedResponse<BybitExecution>> _executionResponses = new();
		private readonly Dictionary<string, BybitInstrumentInfo> _instruments = new(StringComparer.Ordinal);

		public void EnqueueExecution(BybitPagedResponse<BybitExecution> page)
		{
			_executionResponses.Enqueue(page);
		}

		public void AddInstrument(BybitInstrumentInfo instrument)
		{
			_instruments[instrument.Symbol] = instrument;
		}

		public Task<BybitPagedResponse<BybitExecution>> GetExecutionListAsync(
			BybitExecutionListQuery query,
			CancellationToken cancellationToken = default)
		{
			// Окно старее первой недели отвечает пустой страницей без расходования
			// сценария: backfill листает окна до пола глубины, содержательно только
			// самое свежее окно.
			if (query.StartTimeMs < NowMs - WeekMs)
			{
				return Task.FromResult(new BybitPagedResponse<BybitExecution>());
			}

			return Task.FromResult(_executionResponses.Count > 0
				? _executionResponses.Dequeue()
				: new BybitPagedResponse<BybitExecution>());
		}

		public Task<BybitPagedResponse<BybitDeliveryRecord>> GetDeliveryRecordAsync(
			BybitDeliveryRecordQuery query,
			CancellationToken cancellationToken = default)
		{
			// Delivery-сценарий пуст: сосуществование ручных сделок с синхронизацией
			// проверяется на истории исполнения.
			return Task.FromResult(new BybitPagedResponse<BybitDeliveryRecord>());
		}

		public Task<BybitPagedResponse<BybitInstrumentInfo>> GetInstrumentInfoAsync(
			BybitInstrumentInfoQuery query,
			CancellationToken cancellationToken = default)
		{
			IReadOnlyList<BybitInstrumentInfo> list = query.Symbol is not null && _instruments.TryGetValue(query.Symbol, out var instrument)
				? [instrument]
				: [];
			return Task.FromResult(new BybitPagedResponse<BybitInstrumentInfo> { List = list });
		}

		public Task<long> GetServerTimeMsAsync(CancellationToken cancellationToken = default)
		{
			// Серверное время биржи совпадает с виртуальными часами оркестратора.
			return Task.FromResult(NowMs);
		}
	}

	#endregion
}
