using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Bybit;
using TransactionJournal.Data;
using TransactionJournal.Sync;
using TransactionJournal.Tests.Bybit;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Sync;

/// <summary>
/// Проверки движка синхронизации категории на фиктивном шлюзе и хранилище состояния:
/// выбор режима по водяному знаку (первый запуск — backfill, повторные — инкремент
/// с перекрытием), обход окон назад, фиксация водяного знака и границы backfill
/// только по успешному проходу.
/// </summary>
[TestClass]
public class ExecutionCategorySyncTests
{
	// Виртуальные часы фиксированы на 2026-01-01: старт ManualTimeProvider совпадает с этой датой.
	private static readonly long NowMs = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
	private static readonly long DayMs = 86_400_000L;
	private static readonly long WeekMs = ExecutionWindowPass.MaxWindowMs;

	private ScriptedGateway _gateway = null!;
	private FakeKnownIdProbe _knownIdProbe = null!;
	private FakeStateStore _stateStore = null!;
	private ExecutionCategorySync _engine = null!;

	[TestInitialize]
	public void Initialize()
	{
		_gateway = new ScriptedGateway();
		_knownIdProbe = new FakeKnownIdProbe();
		_stateStore = new FakeStateStore();
		_engine = new ExecutionCategorySync(new ExecutionWindowPass(_gateway, _knownIdProbe), _stateStore, new ManualTimeProvider());
	}

	[TestMethod]
	[Description("Первый запуск без водяного знака выбирает backfill и фиксирует водяной знак с границей")]
	public async Task TryIfFirstRunWithoutWatermarkChoosesBackfillAndFixesWatermarkAndBoundary()
	{
		// Arrange: состояние категории пустое — успешного синка ещё не было. Биржа отдаёт
		// записи в двух окнах, третье окно пустое: глубже данных нет.
		// Требование: при отсутствии отметки о завершённой синхронизации выполняется первичный
		// backfill, по завершении фиксируются водяной знак и достигнутая граница истории.
		// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
		// Traceability: openspec:sync/bybit-history#scenario-first-run-backfill
		// Traceability: openspec:sync/bybit-history#scenario-backfill-depth-boundary
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>
		{
			List = [Execution("exec-new-1", NowMs - 1_000)],
		});
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>
		{
			List = [Execution("exec-new-2", NowMs - 8 * DayMs)],
		});
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());

		// Act
		var result = await _engine.RunAsync("linear");

		// Assert: режим — первичный backfill, окна идут назад от момента запуска семидневным шагом.
		Assert.That(result.Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(_gateway.Queries, Has.Count.EqualTo(3));
		Assert.That(_gateway.Queries[0].Category, Is.EqualTo("linear"));
		Assert.That(_gateway.Queries[0].StartTimeMs, Is.EqualTo(NowMs - WeekMs));
		Assert.That(_gateway.Queries[0].EndTimeMs, Is.EqualTo(NowMs));
		Assert.That(_gateway.Queries[0].Limit, Is.EqualTo(100));
		Assert.That(_gateway.Queries[1].StartTimeMs, Is.EqualTo(NowMs - 2 * WeekMs));
		Assert.That(_gateway.Queries[1].EndTimeMs, Is.EqualTo(NowMs - WeekMs));
		Assert.That(_gateway.Queries[2].StartTimeMs, Is.EqualTo(NowMs - 3 * WeekMs));
		Assert.That(_gateway.Queries[2].EndTimeMs, Is.EqualTo(NowMs - 2 * WeekMs));

		// Assert: обе записи новые, пустое окно означило исчерпание истории.
		Assert.That(result.NewExecutions.Select(execution => execution.ExecId),
			Is.EqualTo(new[] { "exec-new-1", "exec-new-2" }));
		Assert.That(result.WindowsProcessed, Is.EqualTo(3));
		Assert.That(result.HistoryExhausted, Is.True);
		Assert.That(result.EarlyStopped, Is.False);

		// Assert: состояние зафиксировано — водяной знак на момент запуска, граница backfill
		// на самой ранней достигнутой записи.
		var saved = _stateStore.Find("linear");
		Assert.That(saved, Is.Not.Null);
		Assert.That(saved!.ExecWatermarkMs, Is.EqualTo(NowMs));
		Assert.That(saved.BackfillBoundaryMs, Is.EqualTo(NowMs - 8 * DayMs));
		Assert.That(saved.LastSuccessAt, Is.EqualTo(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
		Assert.That(result.ExecWatermarkMs, Is.EqualTo(NowMs));
		Assert.That(result.BackfillBoundaryMs, Is.EqualTo(NowMs - 8 * DayMs));
	}

	[TestMethod]
	[Description("Повторный запуск при водяном знаке выбирает инкремент с перекрытием назад от знака")]
	public async Task TryIfWatermarkPresentChoosesIncrementalWithOverlapAndEarlyStop()
	{
		// Arrange: у категории есть водяной знак трёхдневной давности. Окно инкремента
		// читается от знака минус суточное перекрытие; страница целиком из известной записи —
		// ранняя остановка, вся история заново не перечитывается.
		// Требование: при наличии отметки о завершённой синхронизации система догружает только
		// записи от водяного знака с перекрытием назад и не перечитывает историю целиком.
		// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
		// Traceability: openspec:sync/bybit-history#scenario-subsequent-run-incremental
		await _stateStore.SaveAsync(new SyncState { Category = "linear", ExecWatermarkMs = NowMs - 3 * DayMs });
		_knownIdProbe.Know("exec-known-1");
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>
		{
			List = [Execution("exec-known-1", NowMs - 2 * DayMs)],
		});

		// Act
		var result = await _engine.RunAsync("linear");

		// Assert: режим — инкремент, запрошено одно окно от знака минус перекрытие до момента запуска.
		Assert.That(result.Mode, Is.EqualTo(SyncRunMode.Incremental));
		Assert.That(_gateway.Queries, Has.Count.EqualTo(1));
		Assert.That(_gateway.Queries[0].StartTimeMs, Is.EqualTo(NowMs - 3 * DayMs - DayMs));
		Assert.That(_gateway.Queries[0].EndTimeMs, Is.EqualTo(NowMs));

		// Assert: целиком известная страница остановила проход, новых записей нет.
		Assert.That(result.EarlyStopped, Is.True);
		Assert.That(result.HistoryExhausted, Is.False);
		Assert.That(result.NewExecutions, Is.Empty);
		Assert.That(result.WindowsProcessed, Is.EqualTo(1));

		// Assert: водяной знак продвинулся на момент запуска, граница backfill инкрементом не трогается.
		var saved = _stateStore.Find("linear");
		Assert.That(saved!.ExecWatermarkMs, Is.EqualTo(NowMs));
		Assert.That(saved.BackfillBoundaryMs, Is.Null);
		Assert.That(result.ExecWatermarkMs, Is.EqualTo(NowMs));
	}

	[TestMethod]
	[Description("Инкремент листает окна назад не глубже водяного знака минус перекрытие")]
	public async Task TryIfIncrementalWalksWindowsOnlyDownToWatermarkMinusOverlap()
	{
		// Arrange: водяной знак шестнадцать дней назад — хвост длиннее одного окна,
		// инкремент разбивается на несколько 7-дневных окон. В каждом окне по одной
		// новой записи, ранней остановки не происходит.
		// Требование: догрузка перечитывает только хвост истории и не уходит глубже знака.
		// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
		await _stateStore.SaveAsync(new SyncState { Category = "option", ExecWatermarkMs = NowMs - 16 * DayMs });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-1", NowMs - DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-2", NowMs - 8 * DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-3", NowMs - 15 * DayMs)] });

		// Act
		var result = await _engine.RunAsync("option");

		// Assert: три окна назад, самое старое начинается ровно на знаке минус перекрытие.
		Assert.That(result.Mode, Is.EqualTo(SyncRunMode.Incremental));
		Assert.That(_gateway.Queries, Has.Count.EqualTo(3));
		Assert.That(_gateway.Queries[2].StartTimeMs, Is.EqualTo(NowMs - 16 * DayMs - DayMs));
		Assert.That(_gateway.Queries[2].EndTimeMs, Is.EqualTo(NowMs - 2 * WeekMs));
		Assert.That(result.EarlyStopped, Is.False);
		Assert.That(result.NewExecutions.Select(execution => execution.ExecId),
			Is.EqualTo(new[] { "exec-1", "exec-2", "exec-3" }));

		// Assert: водяной знак продвинулся на момент запуска.
		Assert.That(_stateStore.Find("option")!.ExecWatermarkMs, Is.EqualTo(NowMs));
	}

	[TestMethod]
	[Description("Повторный полный проход не запрашивает окна старее зафиксированной границы backfill")]
	public async Task TryIfBackfillStopsAtFixedBoundaryOnRepeatedFullPass()
	{
		// Arrange: граница backfill уже зафиксирована десять дней назад, водяного знака нет —
		// полный проход. Второе окно обрезается границей, третьего запроса не должно быть.
		// Требование: достигнутая граница доступности фиксируется, повторные полные проходы
		// не запрашивают данные старше границы.
		// Traceability: openspec:sync/bybit-history#requirement-backfill-full-history
		// Traceability: openspec:sync/bybit-history#scenario-backfill-depth-boundary
		await _stateStore.SaveAsync(new SyncState { Category = "linear", BackfillBoundaryMs = NowMs - 10 * DayMs });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-1", NowMs - DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-2", NowMs - 8 * DayMs)] });

		// Act
		var result = await _engine.RunAsync("linear");

		// Assert: два окна, начало второго обрезано границей; глубже границы запросов нет.
		Assert.That(result.Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(_gateway.Queries, Has.Count.EqualTo(2));
		Assert.That(_gateway.Queries[1].StartTimeMs, Is.EqualTo(NowMs - 10 * DayMs));
		Assert.That(_gateway.Queries[1].EndTimeMs, Is.EqualTo(NowMs - WeekMs));
		Assert.That(result.HistoryExhausted, Is.False);

		// Assert: граница не откатилась — проход не читал глубже уже известной глубины.
		Assert.That(_stateStore.Find("linear")!.BackfillBoundaryMs, Is.EqualTo(NowMs - 10 * DayMs));
	}

	[TestMethod]
	[Description("Граница backfill фиксируется по каждой категории независимо")]
	public async Task TryIfBackfillBoundaryTrackedPerCategoryIndependently()
	{
		// Arrange: обе категории синхронизируются впервые, история разной глубины —
		// linear заканчивается двумя днями назад, option пятью.
		// Требование: backfill выполняется по каждой торговой категории отдельным проходом.
		// Traceability: openspec:sync/bybit-history#requirement-backfill-full-history
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-linear", NowMs - 2 * DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-option", NowMs - 5 * DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());

		// Act
		var linearResult = await _engine.RunAsync("linear");
		var optionResult = await _engine.RunAsync("option");

		// Assert: каждый проход запрашивал только свою категорию.
		Assert.That(linearResult.Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(optionResult.Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(_gateway.Queries.Select(query => query.Category),
			Is.EqualTo(new[] { "linear", "linear", "option", "option" }));

		// Assert: состояния категорий независимы — своя граница и свой водяной знак.
		var linearState = _stateStore.Find("linear");
		var optionState = _stateStore.Find("option");
		Assert.That(linearState!.BackfillBoundaryMs, Is.EqualTo(NowMs - 2 * DayMs));
		Assert.That(optionState!.BackfillBoundaryMs, Is.EqualTo(NowMs - 5 * DayMs));
		Assert.That(linearState.ExecWatermarkMs, Is.EqualTo(NowMs));
		Assert.That(optionState.ExecWatermarkMs, Is.EqualTo(NowMs));
	}

	[TestMethod]
	[Description("Свежие записи каждого окна передаются писателю пачками с прогрессом запуска")]
	public async Task TryIfFreshWindowBatchesArePassedToRawWriterWithProgressRun()
	{
		// Arrange: движок с фиктивным писателем; два окна приносят по одной новой записи,
		// третье окно пустое. Пустое окно не должно адресоваться писателю.
		// Требование: загрузчик пишет сырые записи пачками по мере прохождения окон и
		// продвигает счётчик запуска на размер каждой вставки.
		// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
		// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
		var writer = new FakeRawWriter();
		var engine = new ExecutionCategorySync(
			new ExecutionWindowPass(_gateway, _knownIdProbe), _stateStore, new ManualTimeProvider(), writer);
		var progressRun = new SyncRun
		{
			StartedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
			Mode = SyncRunMode.Backfill,
			Status = SyncRunStatus.Running,
		};
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-1", NowMs - DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-2", NowMs - 8 * DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());

		// Act
		var result = await engine.RunAsync("linear", progressRun: progressRun);

		// Assert: писателю ушли ровно две пачки — по свежим записям непустых окон.
		Assert.That(writer.Batches, Has.Count.EqualTo(2));
		Assert.That(writer.Batches[0].ExecIds, Is.EqualTo(new[] { "exec-1" }));
		Assert.That(writer.Batches[1].ExecIds, Is.EqualTo(new[] { "exec-2" }));
		Assert.That(writer.Batches[0].Category, Is.EqualTo("linear"));

		// Assert: обе пачки несут дескриптор запуска для продвижения счётчика.
		Assert.That(writer.Batches.All(batch => ReferenceEquals(batch.Run, progressRun)), Is.True);

		// Assert: итог движка учитывает фактически вставленные строки.
		Assert.That(result.NewExecutionsPersisted, Is.EqualTo(2));
	}

	[TestMethod]
	[Description("Прогресс запуска отклоняется без писателя сырых записей")]
	[ExpectedException(typeof(ArgumentException))]
	public async Task ThrowOnProgressRunWithoutRawWriter()
	{
		// Arrange: движок без писателя не может продвигать счётчик запуска пачками.
		var progressRun = new SyncRun
		{
			StartedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
			Mode = SyncRunMode.Backfill,
			Status = SyncRunStatus.Running,
		};

		// Act — некорректная комбинация прерывается до чтения состояния и запросов.
		try
		{
			await _engine.RunAsync("linear", progressRun: progressRun);
		}
		catch (ArgumentException)
		{
			// Assert: шлюз и хранилище не запрашивались.
			Assert.That(_gateway.Queries, Is.Empty);
			Assert.That(_stateStore.FindCalls, Is.Zero);
			throw;
		}
	}

	[TestMethod]
	[Description("Ошибка биржи прерывает проход без фиксации состояния — повторный запуск продолжит с прежней отметки")]
	[ExpectedException(typeof(BybitApiException))]
	public async Task ThrowIfFailedWindowLeavesStateUnfixed()
	{
		// Arrange: первый запуск, первое окно отдаёт запись, второе окно падает ошибкой биржи.
		// Требование: водяной знак фиксируется только по успешному синку — прерванный проход
		// оставляет состояние без изменений и повторяется без потери записей.
		// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
		// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-1", NowMs - DayMs)] });
		_gateway.EnqueueError(new BybitApiException(10001, "ошибка биржи"));

		// Act — ошибка второго окна проходит наружу.
		try
		{
			await _engine.RunAsync("linear");
		}
		catch (BybitApiException)
		{
			// Assert: состояние не зафиксировано — следующий запуск снова выполнит backfill.
			Assert.That(_stateStore.Saved, Is.Empty);
			Assert.That(_stateStore.Find("linear"), Is.Null);
			throw;
		}
	}

	[TestMethod]
	[DataRow(" ")]
	[Description("Пустая категория отклоняется до обращения к шлюзу и хранилищу")]
	[ExpectedException(typeof(ArgumentException))]
	public async Task ThrowOnEmptyCategory(string category)
	{
		// Arrange — Act: категория — ключ состояния и обязательный параметр эндпоинта.
		try
		{
			await _engine.RunAsync(category);
		}
		catch (ArgumentException)
		{
			// Assert: шлюз и хранилище не запрашивались.
			Assert.That(_gateway.Queries, Is.Empty);
			Assert.That(_stateStore.Saved, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Нулевая категория отклоняется до обращения к шлюзу и хранилищу")]
	[ExpectedException(typeof(ArgumentNullException))]
	public async Task ThrowOnNullCategory()
	{
		// Arrange — Act: категория — обязательный параметр запроса истории исполнения.
		try
		{
			await _engine.RunAsync(null!);
		}
		catch (ArgumentNullException)
		{
			// Assert: шлюз и хранилище не запрашивались.
			Assert.That(_gateway.Queries, Is.Empty);
			Assert.That(_stateStore.Saved, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Отрицательное перекрытие инкремента отклоняется до обращения к шлюзу и хранилищу")]
	[ExpectedException(typeof(ArgumentOutOfRangeException))]
	public async Task ThrowOnNegativeIncrementalOverlap()
	{
		// Arrange: перекрытие назад — защита от граничных гонок, отрицательное лишило бы её смысла.
		var options = new ExecutionCategorySyncOptions { IncrementalOverlapMs = -1 };

		// Act — некорректное перекрытие прерывается до чтения состояния и запросов.
		try
		{
			await _engine.RunAsync("linear", options);
		}
		catch (ArgumentOutOfRangeException)
		{
			// Assert: шлюз и хранилище не запрашивались.
			Assert.That(_gateway.Queries, Is.Empty);
			Assert.That(_stateStore.FindCalls, Is.Zero);
			throw;
		}
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	[Description("Нулевая зависимость конструктора отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullConstructorDependency(bool nullWindowPass)
	{
		// Arrange — Act: проход окна и хранилище состояния обязательны для движка.
		if (nullWindowPass)
		{
			new ExecutionCategorySync(null!, _stateStore);
		}
		else
		{
			new ExecutionCategorySync(new ExecutionWindowPass(_gateway, _knownIdProbe), null!);
		}
	}

	#region Помощники

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
	/// Фиктивный шлюз биржи: раздаёт заготовленные страницы и ошибки по порядку и помнит
	/// все запросы движка. При исчерпании сценария отвечает пустой страницей без курсора.
	/// </summary>
	private sealed class ScriptedGateway : IBybitHistoryGateway
	{
		private readonly Queue<object> _responses = new();

		/// <summary>Все запросы движка в порядке отправления.</summary>
		public List<BybitExecutionListQuery> Queries { get; } = [];

		public void Enqueue(BybitPagedResponse<BybitExecution> page)
		{
			_responses.Enqueue(page);
		}

		public void EnqueueError(BybitApiException error)
		{
			_responses.Enqueue(error);
		}

		public Task<BybitPagedResponse<BybitExecution>> GetExecutionListAsync(
			BybitExecutionListQuery query,
			CancellationToken cancellationToken = default)
		{
			Queries.Add(query);
			if (_responses.Count == 0)
			{
				return Task.FromResult(new BybitPagedResponse<BybitExecution>());
			}

			var response = _responses.Dequeue();
			return response is BybitApiException error
				? Task.FromException<BybitPagedResponse<BybitExecution>>(error)
				: Task.FromResult((BybitPagedResponse<BybitExecution>)response);
		}

		public Task<BybitPagedResponse<BybitDeliveryRecord>> GetDeliveryRecordAsync(
			BybitDeliveryRecordQuery query,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException("Движок синхронизации исполнения не запрашивает delivery-записи.");
		}
	}

	/// <summary>
	/// Фиктивная проверка известных execId: помнит сохранённые идентификаторы
	/// и все пачки, о которых спрашивал проход.
	/// </summary>
	private sealed class FakeKnownIdProbe : IExecutionKnownIdProbe
	{
		private readonly HashSet<string> _knownIds = new(StringComparer.Ordinal);

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
			var known = new HashSet<string>(
				execIds.Where(execId => _knownIds.Contains(execId)), StringComparer.Ordinal);
			return Task.FromResult<IReadOnlySet<string>>(known);
		}
	}

	/// <summary>
	/// Фиктивное хранилище состояния: держит по одной строке на категорию
	/// и помнит все сохранённые состояния и обращения за ними.
	/// </summary>
	private sealed class FakeStateStore : IExecutionSyncStateStore
	{
		private readonly Dictionary<string, SyncState> _states = new(StringComparer.Ordinal);

		/// <summary>Все состояния в порядке сохранения движком.</summary>
		public List<SyncState> Saved { get; } = [];

		/// <summary>Сколько раз движок читал состояние категории.</summary>
		public int FindCalls { get; private set; }

		public SyncState? Find(string category)
		{
			return _states.TryGetValue(category, out var state) ? state : null;
		}

		public Task<SyncState?> FindAsync(string category, CancellationToken cancellationToken = default)
		{
			FindCalls++;
			return Task.FromResult(Find(category));
		}

		public Task SaveAsync(SyncState state, CancellationToken cancellationToken = default)
		{
			_states[state.Category] = state;
			Saved.Add(state);
			return Task.CompletedTask;
		}
	}

	/// <summary>
	/// Фиктивный писатель сырых записей: помнит каждую пачку с дескриптором запуска
	/// и отчитывается вставкой всех полученных записей.
	/// </summary>
	private sealed class FakeRawWriter : IRawExecutionBatchWriter
	{
		/// <summary>Все пачки в порядке вызова движка.</summary>
		public List<(string Category, IReadOnlyList<string> ExecIds, SyncRun? Run)> Batches { get; } = [];

		public Task<RawExecutionBatchResult> WriteAsync(
			string category,
			IReadOnlyCollection<BybitExecution> executions,
			SyncRun? progressRun = null,
			CancellationToken cancellationToken = default)
		{
			var execIds = executions.Select(execution => execution.ExecId).ToList();
			Batches.Add((category, execIds, progressRun));
			return Task.FromResult(new RawExecutionBatchResult
			{
				InsertedExecIds = execIds,
				SkippedKnownCount = 0,
			});
		}
	}

	#endregion
}
