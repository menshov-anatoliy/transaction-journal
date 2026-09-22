using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Bybit;
using TransactionJournal.Sync;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Sync;

/// <summary>
/// Проверки прохода 7-дневного окна истории исполнения на фиктивном шлюзе
/// с многостраничными ответами: курсорная пагинация до исчерпания, ранняя
/// остановка на целиком известной странице и выключение остановки для backfill.
/// </summary>
[TestClass]
public class ExecutionWindowPassTests
{
	private static readonly long WindowStartMs = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
	private static readonly long WindowEndMs = WindowStartMs + ExecutionWindowPass.MaxWindowMs;

	private ScriptedGateway _gateway = null!;
	private FakeKnownIdProbe _knownIdProbe = null!;
	private ExecutionWindowPass _pass = null!;

	[TestInitialize]
	public void Initialize()
	{
		_gateway = new ScriptedGateway();
		_knownIdProbe = new FakeKnownIdProbe();
		_pass = new ExecutionWindowPass(_gateway, _knownIdProbe);
	}

	[TestMethod]
	[Description("Проход листает окно курсорной пагинацией до исчерпания страниц биржи")]
	public async Task TryIfWindowPassPagesWithCursorUntilExhaustion()
	{
		// Arrange: три страницы — две с записями и курсорами, третья пустая без курсора.
		// Требование: история читается окнами с курсорной пагинацией по каждой категории.
		// Traceability: openspec:sync/bybit-history#requirement-backfill-full-history
		// Traceability: change:add-bybit-sync/design#d4
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>
		{
			List = [Execution("exec-1", WindowEndMs - 1), Execution("exec-2", WindowEndMs - 2)],
			NextPageCursor = "cursor-2",
		});
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>
		{
			List = [Execution("exec-3", WindowStartMs + 1)],
			NextPageCursor = "cursor-3",
		});
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());

		// Act
		var result = await _pass.RunAsync(Window());

		// Assert: проход запросил ровно три страницы, перенося курсор ответа в следующий запрос.
		Assert.That(_gateway.Queries, Has.Count.EqualTo(3));
		Assert.That(_gateway.Queries[0].Cursor, Is.Null);
		Assert.That(_gateway.Queries[1].Cursor, Is.EqualTo("cursor-2"));
		Assert.That(_gateway.Queries[2].Cursor, Is.EqualTo("cursor-3"));

		// Assert: каждый запрос несёт категорию, границы окна и размер страницы.
		Assert.That(_gateway.Queries[0].Category, Is.EqualTo("linear"));
		Assert.That(_gateway.Queries[0].StartTimeMs, Is.EqualTo(WindowStartMs));
		Assert.That(_gateway.Queries[0].EndTimeMs, Is.EqualTo(WindowEndMs));
		Assert.That(_gateway.Queries[0].Limit, Is.EqualTo(100));

		// Assert: все записи окна новые и возвращены в порядке выдачи; исчерпание — без ранней остановки.
		Assert.That(result.NewExecutions.Select(execution => execution.ExecId),
			Is.EqualTo(new[] { "exec-1", "exec-2", "exec-3" }));
		Assert.That(result.PagesFetched, Is.EqualTo(3));
		Assert.That(result.EarlyStopped, Is.False);
		Assert.That(result.Exhausted, Is.True);

		// Assert: известность спрашивалась пачками только по непустым страницам.
		Assert.That(_knownIdProbe.AskedBatches, Has.Count.EqualTo(2));
	}

	[TestMethod]
	[Description("Проход останавливается раньше, когда страница целиком из известных execId")]
	public async Task TryIfWindowPassStopsEarlyOnEntirelyKnownPage()
	{
		// Arrange: первая страница смешанная (одна новая и одна известная запись),
		// вторая — целиком из известных; курсор третьей страницы не должен понадобиться.
		// Требование: перекрытие окна не создаёт дублей — известные записи пропускаются.
		// Traceability: openspec:sync/bybit-history#scenario-window-overlap-no-duplicates
		// Traceability: change:add-bybit-sync/design#d4
		_knownIdProbe.Know("known-1", "known-2");
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>
		{
			List = [Execution("fresh-1", WindowEndMs - 1), Execution("known-1", WindowEndMs - 2)],
			NextPageCursor = "cursor-2",
		});
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>
		{
			List = [Execution("known-1", WindowStartMs + 2), Execution("known-2", WindowStartMs + 1)],
			NextPageCursor = "cursor-3",
		});

		// Act
		var result = await _pass.RunAsync(Window());

		// Assert: за целиком известной страницей проход не запрашивает следующую.
		Assert.That(_gateway.Queries, Has.Count.EqualTo(2));
		Assert.That(_gateway.Queries[1].Cursor, Is.EqualTo("cursor-2"));

		// Assert: в результате только новая запись смешанной страницы; остановка — ранняя.
		Assert.That(result.NewExecutions.Select(execution => execution.ExecId), Is.EqualTo(new[] { "fresh-1" }));
		Assert.That(result.PagesFetched, Is.EqualTo(2));
		Assert.That(result.EarlyStopped, Is.True);
		Assert.That(result.Exhausted, Is.False);
	}

	[TestMethod]
	[Description("Без ранней остановки проход листает известные страницы до исчерпания окна")]
	public async Task TryIfWindowPassWithoutEarlyStopContinuesKnownPagesToExhaustion()
	{
		// Arrange: обе страницы целиком из известных записей, ранняя остановка выключена —
		// так ходит backfill: известные страницы не означают, что хвост окна уже сохранён,
		// поэтому возобновление после обрыва обязано дочитать окно до конца.
		// Traceability: change:add-bybit-sync/design#d4
		// Traceability: change:add-bybit-sync/design#d5
		_knownIdProbe.Know("known-1", "known-2");
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>
		{
			List = [Execution("known-1", WindowEndMs - 1)],
			NextPageCursor = "cursor-2",
		});
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>
		{
			List = [Execution("known-2", WindowStartMs + 1)],
		});

		// Act
		var result = await _pass.RunAsync(Window(), new ExecutionWindowPassOptions { EarlyStopOnKnownPage = false });

		// Assert: проход долистал окно до исчерпания, новых записей не нашёл.
		Assert.That(_gateway.Queries, Has.Count.EqualTo(2));
		Assert.That(_gateway.Queries[1].Cursor, Is.EqualTo("cursor-2"));
		Assert.That(result.NewExecutions, Is.Empty);
		Assert.That(result.PagesFetched, Is.EqualTo(2));
		Assert.That(result.EarlyStopped, Is.False);
		Assert.That(result.Exhausted, Is.True);
	}

	[TestMethod]
	[Description("Окно с базовым активом уходит в запрос с фильтром baseCoin")]
	public async Task TryIfWindowBaseCoinIsSentInQuery()
	{
		// Arrange: опционное окно с фильтром базового актива — без явного baseCoin биржа
		// отдаёт записи только одного актива доски, поэтому фильтр обязан дойти до запроса.
		// Требование: история исполнения option запрашивается по каждому базовому активу.
		// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>
		{
			List = [Execution("exec-opt-1", WindowEndMs - 1)],
		});

		// Act
		var result = await _pass.RunAsync(Window("option") with { BaseCoin = "ETH" });

		// Assert: запрос несёт категорию, фильтр базового актива и границы окна.
		Assert.That(_gateway.Queries, Has.Count.EqualTo(1));
		Assert.That(_gateway.Queries[0].Category, Is.EqualTo("option"));
		Assert.That(_gateway.Queries[0].BaseCoin, Is.EqualTo("ETH"));
		Assert.That(_gateway.Queries[0].StartTimeMs, Is.EqualTo(WindowStartMs));
		Assert.That(_gateway.Queries[0].EndTimeMs, Is.EqualTo(WindowEndMs));
		Assert.That(result.NewExecutions.Select(execution => execution.ExecId), Is.EqualTo(new[] { "exec-opt-1" }));
	}

	[TestMethod]
	[Description("Окно без базового актива уходит в запрос без фильтра baseCoin")]
	public async Task TryIfWindowWithoutBaseCoinSendsNoBaseCoinFilter()
	{
		// Arrange: линейная доска отдаёт все символы категории без фильтров —
		// окно без baseCoin обязано уйти в запрос без параметра baseCoin.
		// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>
		{
			List = [Execution("exec-1", WindowEndMs - 1)],
		});

		// Act
		var result = await _pass.RunAsync(Window());

		// Assert: фильтр базового актива в запросе не задан.
		Assert.That(_gateway.Queries, Has.Count.EqualTo(1));
		Assert.That(_gateway.Queries[0].BaseCoin, Is.Null);
		Assert.That(result.NewExecutions.Select(execution => execution.ExecId), Is.EqualTo(new[] { "exec-1" }));
	}

	[TestMethod]
	[DataRow(0L)]
	[DataRow(-1L)]
	[DataRow(604800001L)]
	[Description("Пустое, перевёрнутое или длиннее семи дней окно отклоняется до запросов")]
	[ExpectedException(typeof(ArgumentOutOfRangeException))]
	public void ThrowOnWindowOutsideSevenDayLimit(long spanMs)
	{
		// Arrange: эндпоинт execution/list ограничивает окно семью днями и требует конец позже начала.
		var window = new ExecutionWindow { Category = "linear", StartMs = WindowStartMs, EndMs = WindowStartMs + spanMs };

		// Act — некорректное окно прерывается до обращения к шлюзу.
		try
		{
			_pass.RunAsync(window).GetAwaiter().GetResult();
		}
		catch (ArgumentOutOfRangeException)
		{
			// Assert: шлюз не запрашивался.
			Assert.That(_gateway.Queries, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Окно без категории отклоняется до запросов")]
	[ExpectedException(typeof(ArgumentException))]
	public void ThrowOnWindowWithoutCategory()
	{
		// Arrange: категория — обязательный параметр эндпоинта execution/list.
		var window = new ExecutionWindow { Category = " ", StartMs = WindowStartMs, EndMs = WindowEndMs };

		// Act — пустая категория прерывается до обращения к шлюзу.
		try
		{
			_pass.RunAsync(window).GetAwaiter().GetResult();
		}
		catch (ArgumentException)
		{
			// Assert: шлюз не запрашивался.
			Assert.That(_gateway.Queries, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(101)]
	[Description("Размер страницы вне диапазона [1..100] отклоняется до запросов")]
	[ExpectedException(typeof(ArgumentOutOfRangeException))]
	public void ThrowOnPageSizeOutsideDocumentedRange(int pageSize)
	{
		// Arrange: биржа ограничивает размер страницы execution/list диапазоном [1..100].
		var options = new ExecutionWindowPassOptions { PageSize = pageSize };

		// Act — выход за диапазон прерывается до обращения к шлюзу.
		try
		{
			_pass.RunAsync(Window(), options).GetAwaiter().GetResult();
		}
		catch (ArgumentOutOfRangeException)
		{
			// Assert: шлюз не запрашивался.
			Assert.That(_gateway.Queries, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	[Description("Нулевая зависимость конструктора отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullConstructorDependency(bool nullGateway)
	{
		// Arrange — Act: шлюз и проверка известных execId обязательны для прохода.
		if (nullGateway)
		{
			new ExecutionWindowPass(null!, _knownIdProbe);
		}
		else
		{
			new ExecutionWindowPass(_gateway, null!);
		}
	}

	#region Помощники

	private static ExecutionWindow Window(string category = "linear") => new()
	{
		Category = category,
		StartMs = WindowStartMs,
		EndMs = WindowEndMs,
	};

	private static BybitExecution Execution(string execId, long execTimeMs) => new()
	{
		Symbol = "BTCUSDT",
		ExecId = execId,
		Side = "Buy",
		ExecTimeMs = execTimeMs,
	};

	#endregion

	#region Фиктивные зависимости

	/// <summary>
	/// Фиктивный шлюз биржи: раздаёт заготовленные страницы по порядку и помнит все
	/// запросы прохода. При исчерпании сценария отвечает пустой страницей без курсора.
	/// </summary>
	private sealed class ScriptedGateway : IBybitHistoryGateway
	{
		private readonly Queue<BybitPagedResponse<BybitExecution>> _pages = new();

		/// <summary>Все запросы прохода в порядке отправления.</summary>
		public List<BybitExecutionListQuery> Queries { get; } = [];

		public void Enqueue(BybitPagedResponse<BybitExecution> page)
		{
			_pages.Enqueue(page);
		}

		public Task<BybitPagedResponse<BybitExecution>> GetExecutionListAsync(
			BybitExecutionListQuery query,
			CancellationToken cancellationToken = default)
		{
			Queries.Add(query);
			var page = _pages.Count > 0 ? _pages.Dequeue() : new BybitPagedResponse<BybitExecution>();
			return Task.FromResult(page);
		}

		public Task<BybitPagedResponse<BybitDeliveryRecord>> GetDeliveryRecordAsync(
			BybitDeliveryRecordQuery query,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException("Проход истории исполнения не запрашивает delivery-записи.");
		}
	}

	/// <summary>
	/// Фиктивная проверка известных execId: помнит сохранённые идентификаторы
	/// и все пачки, о которых спрашивал проход.
	/// </summary>
	private sealed class FakeKnownIdProbe : IExecutionKnownIdProbe
	{
		private readonly HashSet<string> _knownIds = new(StringComparer.Ordinal);

		/// <summary>Все пачки идентификаторов, о которых спрашивал проход, в порядке обращений.</summary>
		public List<IReadOnlyCollection<string>> AskedBatches { get; } = [];

		public void Know(params string[] execIds)
		{
			foreach (var execId in execIds)
			{
				_knownIds.Add(execId);
			}
		}

		public Task<IReadOnlySet<string>> FindKnownAsync(
			IReadOnlyCollection<string> execIds,
			CancellationToken cancellationToken = default)
		{
			AskedBatches.Add(execIds);
			var known = new HashSet<string>(
				execIds.Where(execId => _knownIds.Contains(execId)), StringComparer.Ordinal);
			return Task.FromResult<IReadOnlySet<string>>(known);
		}
	}

	#endregion
}
