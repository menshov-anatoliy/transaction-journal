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
/// Проверки движка delivery-синхронизации категории на фиктивном шлюзе и хранилище
/// состояния: выбор режима по delivery-водяному знаку (первый запуск — backfill
/// 30-дневными окнами, повторные — инкремент с перекрытием), дедуп по ключу
/// symbol + deliveryTime на пересекающихся окнах, фиксация водяного знака только
/// по успешному проходу.
/// </summary>
[TestClass]
public class DeliveryCategorySyncTests
{
	// Виртуальные часы фиксированы на 2026-01-01: старт ManualTimeProvider совпадает с этой датой.
	private static readonly long NowMs = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
	private static readonly long DayMs = 86_400_000L;
	private static readonly long MonthMs = DeliveryWindowPass.MaxWindowMs;

	private ScriptedDeliveryGateway _gateway = null!;
	private FakeDeliveryKeyProbe _knownKeyProbe = null!;
	private FakeStateStore _stateStore = null!;
	private DeliveryCategorySync _engine = null!;

	[TestInitialize]
	public void Initialize()
	{
		_gateway = new ScriptedDeliveryGateway();
		_knownKeyProbe = new FakeDeliveryKeyProbe();
		_stateStore = new FakeStateStore();
		_engine = new DeliveryCategorySync(new DeliveryWindowPass(_gateway, _knownKeyProbe), _stateStore, new ManualTimeProvider());
	}

	[TestMethod]
	[Description("Первый запуск без delivery-водяного знака выбирает backfill и фиксирует знак, не трогая поля исполнения")]
	public async Task TryIfFirstRunWithoutWatermarkChoosesBackfillAndFixesWatermark()
	{
		// Arrange: исполнение категории уже синхронизировалось (есть ExecWatermarkMs и
		// граница backfill), а delivery-водяного знака нет — экспирации синхронизируются
		// впервые. Биржа отдаёт записи в двух окнах, третье окно пустое: глубже данных нет.
		// Требование: при отсутствии отметки о завершённой синхронизации выполняется
		// первичный backfill, по завершении фиксируется водяной знак.
		// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
		// Traceability: openspec:sync/bybit-history#scenario-first-run-backfill
		// Traceability: openspec:sync/bybit-history#scenario-backfill-pages-until-exhaustion
		await _stateStore.SaveAsync(new SyncState
		{
			Category = "option",
			ExecWatermarkMs = NowMs - 200 * DayMs,
			BackfillBoundaryMs = NowMs - 180 * DayMs,
		});
		_gateway.Enqueue(new BybitPagedResponse<BybitDeliveryRecord>
		{
			List = [Delivery("BTC-2JAN26-100000-C", NowMs - 2 * 3600_000L), Delivery("ETH-2JAN26-4000-P", NowMs - 10 * DayMs)],
		});
		_gateway.Enqueue(new BybitPagedResponse<BybitDeliveryRecord>
		{
			List = [Delivery("BTC-2DEC25-90000-P", NowMs - 40 * DayMs)],
		});
		_gateway.Enqueue(new BybitPagedResponse<BybitDeliveryRecord>());

		// Act
		var result = await _engine.RunAsync("option");

		// Assert: режим — первичный backfill, окна идут назад от момента запуска тридцатидневным шагом.
		Assert.That(result.Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(_gateway.Queries, Has.Count.EqualTo(3));
		Assert.That(_gateway.Queries[0].Category, Is.EqualTo("option"));
		Assert.That(_gateway.Queries[0].StartTimeMs, Is.EqualTo(NowMs - MonthMs));
		Assert.That(_gateway.Queries[0].EndTimeMs, Is.EqualTo(NowMs));
		Assert.That(_gateway.Queries[0].Limit, Is.EqualTo(50));
		Assert.That(_gateway.Queries[1].StartTimeMs, Is.EqualTo(NowMs - 2 * MonthMs));
		Assert.That(_gateway.Queries[1].EndTimeMs, Is.EqualTo(NowMs - MonthMs));
		Assert.That(_gateway.Queries[2].StartTimeMs, Is.EqualTo(NowMs - 3 * MonthMs));
		Assert.That(_gateway.Queries[2].EndTimeMs, Is.EqualTo(NowMs - 2 * MonthMs));

		// Assert: все записи новые, пустое окно означило исчерпания delivery-истории.
		Assert.That(result.NewDeliveries.Select(delivery => delivery.Symbol),
			Is.EqualTo(new[] { "BTC-2JAN26-100000-C", "ETH-2JAN26-4000-P", "BTC-2DEC25-90000-P" }));
		Assert.That(result.WindowsProcessed, Is.EqualTo(3));
		Assert.That(result.HistoryExhausted, Is.True);

		// Assert: состояние зафиксировано — delivery-водяной знак на момент запуска,
		// поля прохода исполнений не затронуты.
		var saved = _stateStore.Find("option");
		Assert.That(saved, Is.Not.Null);
		Assert.That(saved!.DeliveryWatermarkMs, Is.EqualTo(NowMs));
		Assert.That(saved.ExecWatermarkMs, Is.EqualTo(NowMs - 200 * DayMs));
		Assert.That(saved.BackfillBoundaryMs, Is.EqualTo(NowMs - 180 * DayMs));
		Assert.That(saved.LastSuccessAt, Is.EqualTo(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
		Assert.That(result.DeliveryWatermarkMs, Is.EqualTo(NowMs));
	}

	[TestMethod]
	[Description("Инкрементальная догрузка с перекрывающимся окном не создаёт дублей по symbol + deliveryTime")]
	public async Task TryIfOverlappingIncrementalWindowProducesNoDuplicates()
	{
		// Arrange: связка фиктивного писателя с проверкой ключей повторяет реальное
		// хранилище: вставленные записи становятся известными. Первый запуск — backfill:
		// оба delivery лежат в последних сутках, второе окно пустое.
		// Требование: инкрементальная догрузка запрашивает окно с перекрытием назад и
		// получает уже известные записи — система пропускает известные и обрабатывает
		// только новые; дублей не возникает.
		// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
		// Traceability: openspec:sync/bybit-history#scenario-window-overlap-no-duplicates
		// Traceability: openspec:sync/bybit-history#scenario-repeat-sync-no-duplicates
		var knownDelivery = Delivery("BTC-2JAN26-100000-C", NowMs - 2 * 3600_000L);
		var otherKnownDelivery = Delivery("ETH-2JAN26-4000-P", NowMs - 5 * 3600_000L);
		var freshDelivery = Delivery("BTC-2JAN26-100000-P", NowMs - 3600_000L);
		var writer = new FakeDeliveryWriter(_knownKeyProbe);
		var engine = new DeliveryCategorySync(
			new DeliveryWindowPass(_gateway, _knownKeyProbe), _stateStore, new ManualTimeProvider(), writer);
		_gateway.Enqueue(new BybitPagedResponse<BybitDeliveryRecord> { List = [knownDelivery, otherKnownDelivery] });
		_gateway.Enqueue(new BybitPagedResponse<BybitDeliveryRecord>());

		// Act: первый запуск сохраняет обе записи и фиксирует водяной знак.
		var firstRun = await engine.RunAsync("option");

		// Assert: backfill завершён исчерпанием, обе записи сохранены.
		Assert.That(firstRun.Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(firstRun.NewDeliveriesPersisted, Is.EqualTo(2));

		// Arrange: повторный запуск получает то же перекрытие — биржа снова отдаёт обе
		// известные записи и одну новую.
		_gateway.Queries.Clear();
		writer.Batches.Clear();
		_gateway.Enqueue(new BybitPagedResponse<BybitDeliveryRecord>
		{
			List = [freshDelivery, knownDelivery, otherKnownDelivery],
		});

		// Act
		var secondRun = await engine.RunAsync("option");

		// Assert: режим — инкремент, одно окно ровно от водяного знака минус суточное перекрытие.
		Assert.That(secondRun.Mode, Is.EqualTo(SyncRunMode.Incremental));
		Assert.That(_gateway.Queries, Has.Count.EqualTo(1));
		Assert.That(_gateway.Queries[0].StartTimeMs, Is.EqualTo(NowMs - DayMs));
		Assert.That(_gateway.Queries[0].EndTimeMs, Is.EqualTo(NowMs));

		// Assert: известные записи перекрытия отсеяны — новой признана только свежая;
		// писателю ушла пачка из одной записи, дубли не вставлялись.
		Assert.That(secondRun.NewDeliveries.Select(delivery => delivery.Symbol),
			Is.EqualTo(new[] { freshDelivery.Symbol }));
		Assert.That(secondRun.NewDeliveriesPersisted, Is.EqualTo(1));
		Assert.That(writer.Batches, Has.Count.EqualTo(1));
		Assert.That(writer.Batches[0].Keys,
			Is.EqualTo(new[] { new DeliveryRecordKey(freshDelivery.Symbol, freshDelivery.DeliveryTimeMs) }));

		// Assert: за оба запуска каждая запись вставлена ровно один раз.
		var insertedTotal = writer.AllInserted;
		Assert.That(insertedTotal, Has.Count.EqualTo(3));
		Assert.That(insertedTotal.Distinct().ToList(), Has.Count.EqualTo(3));
	}

	[TestMethod]
	[Description("Инкремент листает 30-дневные окна назад не глубже водяного знака минус перекрытие")]
	public async Task TryIfIncrementalWalksWindowsOnlyDownToWatermarkMinusOverlap()
	{
		// Arrange: delivery-водяной знак тридцать пять дней назад — хвост длиннее одного
		// окна, инкремент разбивается на два 30-дневных окна.
		// Требование: догрузка перечитывает только хвост истории и не уходит глубже знака.
		// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
		// Traceability: openspec:sync/bybit-history#scenario-subsequent-run-incremental
		await _stateStore.SaveAsync(new SyncState { Category = "linear", DeliveryWatermarkMs = NowMs - 35 * DayMs });
		_gateway.Enqueue(new BybitPagedResponse<BybitDeliveryRecord> { List = [Delivery("BTCUSDT-26MAR26", NowMs - DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitDeliveryRecord> { List = [Delivery("BTCUSDT-26FEB26", NowMs - 32 * DayMs)] });

		// Act
		var result = await _engine.RunAsync("linear");

		// Assert: два окна назад, самое старое начинается ровно на знаке минус перекрытие.
		Assert.That(result.Mode, Is.EqualTo(SyncRunMode.Incremental));
		Assert.That(_gateway.Queries, Has.Count.EqualTo(2));
		Assert.That(_gateway.Queries[0].StartTimeMs, Is.EqualTo(NowMs - MonthMs));
		Assert.That(_gateway.Queries[0].EndTimeMs, Is.EqualTo(NowMs));
		Assert.That(_gateway.Queries[1].StartTimeMs, Is.EqualTo(NowMs - 35 * DayMs - DayMs));
		Assert.That(_gateway.Queries[1].EndTimeMs, Is.EqualTo(NowMs - MonthMs));
		Assert.That(result.HistoryExhausted, Is.False);
		Assert.That(result.NewDeliveries.Select(delivery => delivery.Symbol),
			Is.EqualTo(new[] { "BTCUSDT-26MAR26", "BTCUSDT-26FEB26" }));

		// Assert: delivery-водяной знак продвинулся на момент запуска.
		Assert.That(_stateStore.Find("linear")!.DeliveryWatermarkMs, Is.EqualTo(NowMs));
	}

	[TestMethod]
	[Description("Свежие записи каждого окна передаются писателю пачками с прогрессом запуска")]
	public async Task TryIfFreshWindowBatchesArePassedToRawWriterWithProgressRun()
	{
		// Arrange: движок с фиктивным писателем; два окна приносят по одной новой записи,
		// третье окно пустое. Пустое окно не должно адресоваться писателю.
		// Требование: загрузчик пишет сырые delivery-записи пачками по мере прохождения
		// окон и продвигает счётчик запуска на размер каждой вставки.
		// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
		// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
		var writer = new FakeDeliveryWriter(_knownKeyProbe);
		var engine = new DeliveryCategorySync(
			new DeliveryWindowPass(_gateway, _knownKeyProbe), _stateStore, new ManualTimeProvider(), writer);
		var progressRun = new SyncRun
		{
			StartedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
			Mode = SyncRunMode.Backfill,
			Status = SyncRunStatus.Running,
		};
		_gateway.Enqueue(new BybitPagedResponse<BybitDeliveryRecord> { List = [Delivery("BTC-2JAN26-100000-C", NowMs - DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitDeliveryRecord> { List = [Delivery("ETH-2JAN26-4000-P", NowMs - 31 * DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitDeliveryRecord>());

		// Act
		var result = await engine.RunAsync("option", progressRun: progressRun);

		// Assert: писателю ушли ровно две пачки — по свежим записям непустых окон.
		Assert.That(writer.Batches, Has.Count.EqualTo(2));
		Assert.That(writer.Batches[0].Keys, Is.EqualTo(new[] { new DeliveryRecordKey("BTC-2JAN26-100000-C", NowMs - DayMs) }));
		Assert.That(writer.Batches[1].Keys, Is.EqualTo(new[] { new DeliveryRecordKey("ETH-2JAN26-4000-P", NowMs - 31 * DayMs) }));
		Assert.That(writer.Batches[0].Category, Is.EqualTo("option"));

		// Assert: обе пачки несут дескриптор запуска для продвижения счётчика.
		Assert.That(writer.Batches.All(batch => ReferenceEquals(batch.Run, progressRun)), Is.True);

		// Assert: итог движка учитывает фактически вставленные строки.
		Assert.That(result.NewDeliveriesPersisted, Is.EqualTo(2));
	}

	[TestMethod]
	[Description("Ошибка биржи прерывает delivery-проход без фиксации состояния — повторный запуск продолжит с прежней отметки")]
	[ExpectedException(typeof(BybitApiException))]
	public async Task ThrowIfFailedWindowLeavesStateUnfixed()
	{
		// Arrange: первый запуск, первое окно отдаёт запись, второе окно падает ошибкой биржи.
		// Требование: водяной знак фиксируется только по успешному синку — прерванный
		// проход оставляет состояние без изменений и повторяется без потери записей.
		// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
		// Traceability: openspec:sync/bybit-history#scenario-interrupted-sync-resumable
		_gateway.Enqueue(new BybitPagedResponse<BybitDeliveryRecord> { List = [Delivery("BTC-2JAN26-100000-C", NowMs - DayMs)] });
		_gateway.EnqueueError(new BybitApiException(10001, "ошибка биржи"));

		// Act — ошибка второго окна проходит наружу.
		try
		{
			await _engine.RunAsync("option");
		}
		catch (BybitApiException)
		{
			// Assert: состояние не зафиксировано — следующий запуск снова выполнит backfill.
			Assert.That(_stateStore.Saved, Is.Empty);
			Assert.That(_stateStore.Find("option"), Is.Null);
			throw;
		}
	}

	[TestMethod]
	[Description("Прогресс запуска отклоняется без писателя сырых delivery-записей")]
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
			await _engine.RunAsync("option", progressRun: progressRun);
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
	[Description("Null-категория отклоняется до обращения к шлюзу и хранилищу")]
	[ExpectedException(typeof(ArgumentNullException))]
	public async Task ThrowOnNullCategory()
	{
		// Arrange — Act: категория — обязательный параметр запроса delivery-истории.
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
		var options = new DeliveryCategorySyncOptions { IncrementalOverlapMs = -1 };

		// Act — некорректное перекрытие прерывается до чтения состояния и запросов.
		try
		{
			await _engine.RunAsync("option", options);
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
			new DeliveryCategorySync(null!, _stateStore);
		}
		else
		{
			new DeliveryCategorySync(new DeliveryWindowPass(_gateway, _knownKeyProbe), null!);
		}
	}

	#region Помощники

	private static BybitDeliveryRecord Delivery(string symbol, long deliveryTimeMs) => new()
	{
		Symbol = symbol,
		DeliveryTimeMs = deliveryTimeMs,
		Side = "Sell",
		Position = 0.1m,
		Strike = 100000m,
	};

	#endregion

	#region Фиктивные зависимости

	/// <summary>
	/// Фиктивный шлюз биржи для delivery-запросов: раздаёт заготовленные страницы и
	/// ошибки по порядку и помнит все запросы движка. При исчерпании сценария отвечает
	/// пустой страницей без курсора.
	/// </summary>
	private sealed class ScriptedDeliveryGateway : IBybitHistoryGateway
	{
		private readonly Queue<object> _responses = new();

		/// <summary>Все delivery-запросы движка в порядке отправления.</summary>
		public List<BybitDeliveryRecordQuery> Queries { get; } = [];

		public void Enqueue(BybitPagedResponse<BybitDeliveryRecord> page)
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
			throw new NotSupportedException("Движок delivery-синхронизации не запрашивает историю исполнения.");
		}

		public Task<BybitPagedResponse<BybitDeliveryRecord>> GetDeliveryRecordAsync(
			BybitDeliveryRecordQuery query,
			CancellationToken cancellationToken = default)
		{
			Queries.Add(query);
			if (_responses.Count == 0)
			{
				return Task.FromResult(new BybitPagedResponse<BybitDeliveryRecord>());
			}

			var response = _responses.Dequeue();
			return response is BybitApiException error
				? Task.FromException<BybitPagedResponse<BybitDeliveryRecord>>(error)
				: Task.FromResult((BybitPagedResponse<BybitDeliveryRecord>)response);
		}
	}

	/// <summary>
	/// Фиктивная проверка известных delivery-ключей: помнит сохранённые пары
	/// symbol + deliveryTime и все пачки, о которых спрашивал проход.
	/// </summary>
	private sealed class FakeDeliveryKeyProbe : IDeliveryKnownKeyProbe
	{
		private readonly HashSet<DeliveryRecordKey> _knownKeys = new();

		/// <summary>Все пачки ключей, о которых спрашивал проход, в порядке обращений.</summary>
		public List<IReadOnlyCollection<DeliveryRecordKey>> AskedBatches { get; } = [];

		public void Know(params DeliveryRecordKey[] keys)
		{
			_knownKeys.UnionWith(keys);
		}

		public Task<IReadOnlySet<DeliveryRecordKey>> FindKnownAsync(
			IReadOnlyCollection<DeliveryRecordKey> keys,
			CancellationToken cancellationToken = default)
		{
			AskedBatches.Add(keys);
			var known = new HashSet<DeliveryRecordKey>(keys.Where(_knownKeys.Contains));
			return Task.FromResult<IReadOnlySet<DeliveryRecordKey>>(known);
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
	/// Фиктивный писатель сырых delivery-записей: помнит каждую пачку с дескриптором
	/// запуска, отчитывается вставкой всех полученных записей и делает вставленные
	/// ключи известными для проверки — как настоящее хранилище.
	/// </summary>
	private sealed class FakeDeliveryWriter : IRawDeliveryBatchWriter
	{
		private readonly FakeDeliveryKeyProbe _probe;

		/// <summary>Все пачки в порядке вызова движка.</summary>
		public List<(string Category, IReadOnlyList<DeliveryRecordKey> Keys, SyncRun? Run)> Batches { get; } = [];

		/// <summary>Все вставленные ключи за время жизни писателя.</summary>
		public List<DeliveryRecordKey> AllInserted { get; } = [];

		public FakeDeliveryWriter(FakeDeliveryKeyProbe probe)
		{
			_probe = probe;
		}

		public Task<RawDeliveryBatchResult> WriteAsync(
			string category,
			IReadOnlyCollection<BybitDeliveryRecord> deliveries,
			SyncRun? progressRun = null,
			CancellationToken cancellationToken = default)
		{
			var keys = deliveries
				.Select(delivery => new DeliveryRecordKey(delivery.Symbol, delivery.DeliveryTimeMs))
				.ToList();
			Batches.Add((category, keys, progressRun));
			AllInserted.AddRange(keys);
			_probe.Know(keys.ToArray());
			return Task.FromResult(new RawDeliveryBatchResult
			{
				InsertedKeys = keys,
				SkippedKnownCount = 0,
			});
		}
	}

	#endregion
}
