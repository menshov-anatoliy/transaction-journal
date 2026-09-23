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
	private FakeOptionBaseCoinSource _optionBaseCoins = null!;
	private ExecutionCategorySync _engine = null!;

	[TestInitialize]
	public void Initialize()
	{
		_gateway = new ScriptedGateway();
		_knownIdProbe = new FakeKnownIdProbe();
		_stateStore = new FakeStateStore();
		_optionBaseCoins = new FakeOptionBaseCoinSource("BTC");
		_engine = new ExecutionCategorySync(
			new ExecutionWindowPass(_gateway, _knownIdProbe), _gateway, _stateStore, _optionBaseCoins, new ManualTimeProvider());
	}

	[TestMethod]
	[Description("Первый запуск без водяного знака выбирает backfill и фиксирует водяной знак с границей")]
	public async Task TryIfFirstRunWithoutWatermarkChoosesBackfillAndFixesWatermarkAndBoundary()
	{
		// Arrange: состояние категории пустое — успешного синка ещё не было. Глубина backfill
		// ограничена тремя неделями: биржа отдаёт записи в двух окнах, третье окно пустое,
		// но проход завершается только на полу глубины.
		// Требование: при отсутствии отметки о завершённой синхронизации выполняется первичный
		// backfill, по завершении фиксируются водяной знак и достигнутая граница истории.
		// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
		// Traceability: openspec:sync/bybit-history#scenario-first-run-backfill
		// Traceability: openspec:sync/bybit-history#scenario-backfill-depth-boundary
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = 3 * WeekMs };
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
		var result = await _engine.RunAsync("linear", options);

		// Assert: режим — первичный backfill, окна идут назад от момента запуска семидневным шагом
		// до пола глубины; линейная категория запрашивается целиком, без фильтра по активу.
		// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
		Assert.That(result.Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(_gateway.Queries, Has.Count.EqualTo(3));
		Assert.That(_gateway.Queries[0].Category, Is.EqualTo("linear"));
		Assert.That(_gateway.Queries[0].BaseCoin, Is.Null);
		Assert.That(_gateway.Queries[0].StartTimeMs, Is.EqualTo(NowMs - WeekMs));
		Assert.That(_gateway.Queries[0].EndTimeMs, Is.EqualTo(NowMs));
		Assert.That(_gateway.Queries[0].Limit, Is.EqualTo(100));
		Assert.That(_gateway.Queries[1].StartTimeMs, Is.EqualTo(NowMs - 2 * WeekMs));
		Assert.That(_gateway.Queries[1].EndTimeMs, Is.EqualTo(NowMs - WeekMs));
		Assert.That(_gateway.Queries[2].StartTimeMs, Is.EqualTo(NowMs - 3 * WeekMs));
		Assert.That(_gateway.Queries[2].EndTimeMs, Is.EqualTo(NowMs - 2 * WeekMs));

		// Assert: обе записи новые, перебор дошёл до пола глубины.
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

		// Assert: без отказов биржи перечень пропущенных областей пуст.
		// Traceability: openspec:sync/bybit-history#requirement-contract-unavailable-area-skip
		Assert.That(result.SkippedAreas, Is.Empty);
	}

	[TestMethod]
	[Description("Пустое окно не завершает backfill: записи за перерывом торговли загружаются")]
	public async Task TryIfEmptyWindowDoesNotFinishBackfillPass()
	{
		// Arrange: глубина три недели; второе окно пустое — перерыв в торговле, третье окно
		// за перерывом снова приносит запись.
		// Требование: пустое окно не завершает проход — окна листаются назад до пола глубины,
		// записи старее перерыва загружаются.
		// Traceability: openspec:sync/bybit-history#scenario-trading-gap-does-not-truncate-history
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = 3 * WeekMs };
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-1", NowMs - DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-2", NowMs - 15 * DayMs)] });

		// Act
		var result = await _engine.RunAsync("linear", options);

		// Assert: все три окна запрошены — пустое окно не остановило перебор.
		Assert.That(_gateway.Queries, Has.Count.EqualTo(3));
		Assert.That(result.NewExecutions.Select(execution => execution.ExecId),
			Is.EqualTo(new[] { "exec-1", "exec-2" }));
		Assert.That(result.HistoryExhausted, Is.True);

		// Assert: граница backfill — самая ранняя запись за перерывом торговли.
		Assert.That(_stateStore.Find("linear")!.BackfillBoundaryMs, Is.EqualTo(NowMs - 15 * DayMs));
	}

	[TestMethod]
	[Description("Backfill останавливается на полу глубины, последнее окно усекается до пола")]
	public async Task TryIfBackfillWalkStopsAtDepthFloor()
	{
		// Arrange: глубина десять дней — пол между границами окон. Второе окно усекается
		// до пола, третьего запроса быть не должно.
		// Требование: backfill листает окна назад до границы максимальной глубины истории,
		// после неё перебор исчерпан.
		// Traceability: openspec:sync/bybit-history#scenario-backfill-pages-until-exhaustion
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = 10 * DayMs };
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-1", NowMs - DayMs)] });

		// Act
		var result = await _engine.RunAsync("linear", options);

		// Assert: ровно два окна, начало второго срезано полом глубины.
		Assert.That(_gateway.Queries, Has.Count.EqualTo(2));
		Assert.That(_gateway.Queries[1].StartTimeMs, Is.EqualTo(NowMs - 10 * DayMs));
		Assert.That(_gateway.Queries[1].EndTimeMs, Is.EqualTo(NowMs - WeekMs));
		Assert.That(result.WindowsProcessed, Is.EqualTo(2));
		Assert.That(result.HistoryExhausted, Is.True);
	}

	[TestMethod]
	[Description("Backfill option отправляет безфильтровое окно и по окну на каждый актив из источника")]
	public async Task TryIfOptionBackfillQueriesEveryBaseCoinFromSource()
	{
		// Arrange: источник доски отдаёт три актива, глубина одного окна — безфильтровая
		// область плюс по области на актив.
		// Требование: опционная категория проходится безфильтровой областью — биржа без
		// фильтра отдаёт записи всех активов аккаунта — и по каждому базовому активу
		// отдельно для гарантированной полноты истории актива.
		// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
		var optionBaseCoins = new FakeOptionBaseCoinSource("BTC", "ETH", "SOL");
		var engine = new ExecutionCategorySync(
			new ExecutionWindowPass(_gateway, _knownIdProbe), _gateway, _stateStore, optionBaseCoins, new ManualTimeProvider());
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = WeekMs };
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-unfiltered", NowMs - DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-btc", NowMs - DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());

		// Act
		var result = await engine.RunAsync("option", options);

		// Assert: безфильтровое окно запрошено первым без фильтра по активу, затем
		// по окну на каждый актив в порядке источника.
		// Traceability: openspec:sync/bybit-history#scenario-non-btc-option-trades-loaded
		Assert.That(_gateway.Queries, Has.Count.EqualTo(4));
		Assert.That(_gateway.Queries.Select(query => query.Category),
			Is.EqualTo(new[] { "option", "option", "option", "option" }));
		Assert.That(_gateway.Queries.Select(query => query.BaseCoin),
			Is.EqualTo(new string?[] { null, "BTC", "ETH", "SOL" }));
		Assert.That(optionBaseCoins.Calls, Is.EqualTo(1));

		// Assert: записи безфильтрового и по-активных проходов собираются в один результат,
		// перебор исчерпан на полу.
		Assert.That(result.NewExecutions.Select(execution => execution.ExecId),
			Is.EqualTo(new[] { "exec-unfiltered", "exec-btc" }));
		Assert.That(result.WindowsProcessed, Is.EqualTo(4));
		Assert.That(result.HistoryExhausted, Is.True);
		Assert.That(_stateStore.Find("option")!.ExecWatermarkMs, Is.EqualTo(NowMs));
	}

	[TestMethod]
	[Description("Инкремент option обходит безфильтровую область и область каждого актива, остановка одной не отменяет остальные")]
	public async Task TryIfOptionIncrementalWalksEveryBaseCoinScope()
	{
		// Arrange: водяной знак трёхдневной давности, доска из двух активов. Страницы
		// безфильтровой области и BTC целиком известны — ранняя остановка, страница ETH
		// приносит новую запись.
		// Требование: области прохода обходятся и в инкрементальном режиме; остановка одной
		// области не отменяет обход остальных.
		// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
		// Traceability: openspec:sync/bybit-history#scenario-subsequent-run-incremental
		var optionBaseCoins = new FakeOptionBaseCoinSource("BTC", "ETH");
		var engine = new ExecutionCategorySync(
			new ExecutionWindowPass(_gateway, _knownIdProbe), _gateway, _stateStore, optionBaseCoins, new ManualTimeProvider());
		await _stateStore.SaveAsync(new SyncState { Category = "option", ExecWatermarkMs = NowMs - 3 * DayMs });
		_knownIdProbe.Know("exec-known", "exec-known-2");
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-known", NowMs - 2 * DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-known-2", NowMs - 2 * DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-eth", NowMs - 2 * DayMs)] });

		// Act
		var result = await engine.RunAsync("option");

		// Assert: безфильтровая область и каждая по-активная получили своё окно от знака
		// минус перекрытие до момента запуска.
		Assert.That(_gateway.Queries, Has.Count.EqualTo(3));
		Assert.That(_gateway.Queries.Select(query => query.BaseCoin), Is.EqualTo(new string?[] { null, "BTC", "ETH" }));
		Assert.That(_gateway.Queries.All(query => query.StartTimeMs == NowMs - 3 * DayMs - DayMs), Is.True);
		Assert.That(_gateway.Queries.All(query => query.EndTimeMs == NowMs), Is.True);

		// Assert: первые две области остановились рано на известных страницах, ETH принёс новую запись.
		Assert.That(result.EarlyStopped, Is.True);
		Assert.That(result.NewExecutions.Select(execution => execution.ExecId), Is.EqualTo(new[] { "exec-eth" }));
	}

	[TestMethod]
	[Description("Безфильтровая область option загружает записи активов, отсутствующих в перечне областей")]
	public async Task TryIfUnfilteredOptionScopeLoadsRecordsOfUnlistedBaseCoins()
	{
		// Arrange: перечень областей знает только BTC, но аккаунт торговал опционами ETH —
		// биржа без фильтра отдаёт записи всех активов аккаунта.
		// Требование: безфильтровая область запрашивает окна без baseCoin, её записи
		// загружаются и сохраняются — сделки не-BTC активов не теряются.
		// Traceability: openspec:sync/bybit-history#scenario-non-btc-option-trades-loaded
		var writer = new IdempotentRawWriter(_knownIdProbe);
		var engine = new ExecutionCategorySync(
			new ExecutionWindowPass(_gateway, _knownIdProbe), _gateway, _stateStore, _optionBaseCoins, new ManualTimeProvider(), writer);
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = WeekMs };
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>
		{
			List = [Execution("exec-eth-1", NowMs - DayMs, "ETH-25DEC23-3000-C")],
		});
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());

		// Act
		var result = await engine.RunAsync("option", options);

		// Assert: безфильтровое окно ушло без фильтра по активу, запись ETH загружена
		// и сохранена пачкой сырых записей.
		// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
		Assert.That(_gateway.Queries[0].BaseCoin, Is.Null);
		Assert.That(result.NewExecutions.Select(execution => execution.ExecId), Is.EqualTo(new[] { "exec-eth-1" }));
		Assert.That(writer.InsertedExecIds, Is.EqualTo(new[] { "exec-eth-1" }));
	}

	[TestMethod]
	[Description("Повторная выгрузка безфильтровых записей по-активной областью не создаёт дублей")]
	public async Task TryIfPerBaseCoinScopeDoesNotDuplicateUnfilteredRecords()
	{
		// Arrange: водяной знак суток, доска из ETH; безфильтровая и по-активная области
		// возвращают одну и ту же запись; вставленная запись сразу видна проверке
		// известных execId, как в сырьевом хранилище.
		// Требование: известные записи пропускаются по биржевому идентификатору исполнения —
		// дубли между безфильтровым и по-активным проходами не создаются.
		// Traceability: openspec:sync/bybit-history#scenario-base-coin-passes-no-duplicates
		var writer = new IdempotentRawWriter(_knownIdProbe);
		var engine = new ExecutionCategorySync(
			new ExecutionWindowPass(_gateway, _knownIdProbe), _gateway, _stateStore, new FakeOptionBaseCoinSource("ETH"), new ManualTimeProvider(), writer);
		await _stateStore.SaveAsync(new SyncState { Category = "option", ExecWatermarkMs = NowMs - DayMs });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>
		{
			List = [Execution("exec-eth-1", NowMs - 2 * 3600_000L, "ETH-25DEC23-3000-C")],
		});
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>
		{
			List = [Execution("exec-eth-1", NowMs - 2 * 3600_000L, "ETH-25DEC23-3000-C")],
		});

		// Act
		var result = await engine.RunAsync("option");

		// Assert: запись вставлена один раз — безфильтровой областью; по-активная область
		// узнала её и остановилась рано, новых вставок нет.
		Assert.That(_gateway.Queries.Select(query => query.BaseCoin), Is.EqualTo(new string?[] { null, "ETH" }));
		Assert.That(writer.InsertedExecIds, Is.EqualTo(new[] { "exec-eth-1" }));
		Assert.That(result.NewExecutions.Select(execution => execution.ExecId), Is.EqualTo(new[] { "exec-eth-1" }));
		Assert.That(result.WindowsProcessed, Is.EqualTo(2));
		Assert.That(result.EarlyStopped, Is.True);
	}

	[TestMethod]
	[Description("Актив, обнаруженный безфильтровым проходом, получает собственную область на следующем запуске")]
	public async Task TryIfBaseCoinDiscoveredByUnfilteredPassGetsOwnScopeNextRun()
	{
		// Arrange: первый запуск знает только BTC; безфильтровая область приносит запись
		// XAUT, которая сохраняется в сырьё. Второй запуск читает перечень с XAUT —
		// сырьевое хранилище уже называет этот актив.
		// Требование: актив, впервые встреченный безфильтровым проходом, определяется
		// в перечне источников и проходится отдельной областью очередным запуском.
		// Traceability: openspec:sync/bybit-history#scenario-new-base-coin-picked-up
		var writer = new IdempotentRawWriter(_knownIdProbe);
		var optionBaseCoins = new FakeOptionBaseCoinSource(
			new IReadOnlyList<string>[] { ["BTC"], ["BTC", "XAUT"] });
		var engine = new ExecutionCategorySync(
			new ExecutionWindowPass(_gateway, _knownIdProbe), _gateway, _stateStore, optionBaseCoins, new ManualTimeProvider(), writer);
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = WeekMs };

		// Первый запуск: безфильтровая область загружает XAUT, область BTC пуста.
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>
		{
			List = [Execution("exec-xaut-1", NowMs - DayMs, "XAUT-25DEC23-5000-P")],
		});
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());
		await engine.RunAsync("option", options);

		// Второй запуск: водяной знак первого запуска переводит категорию в инкремент;
		// страницы пустые, кроме области XAUT, которая возвращает уже известную запись.
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>
		{
			List = [Execution("exec-xaut-1", NowMs - DayMs, "XAUT-25DEC23-5000-P")],
		});

		// Act
		var result = await engine.RunAsync("option");

		// Assert: второй запуск запросил безфильтровую область и области обоих активов —
		// XAUT получил собственную область; повторная страница XAUT распознана как
		// известная, дублей нет.
		Assert.That(_gateway.Queries, Has.Count.EqualTo(5));
		Assert.That(_gateway.Queries.Skip(2).Select(query => query.BaseCoin),
			Is.EqualTo(new string?[] { null, "BTC", "XAUT" }));
		Assert.That(writer.InsertedExecIds, Is.EqualTo(new[] { "exec-xaut-1" }));
		Assert.That(result.NewExecutions, Is.Empty);
		Assert.That(result.PassedBaseCoins, Is.EqualTo(new[] { "BTC", "XAUT" }));
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
		await _stateStore.SaveAsync(new SyncState { Category = "linear", ExecWatermarkMs = NowMs - 16 * DayMs });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-1", NowMs - DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-2", NowMs - 8 * DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-3", NowMs - 15 * DayMs)] });

		// Act
		var result = await _engine.RunAsync("linear");

		// Assert: три окна назад, самое старое начинается ровно на знаке минус перекрытие.
		Assert.That(result.Mode, Is.EqualTo(SyncRunMode.Incremental));
		Assert.That(_gateway.Queries, Has.Count.EqualTo(3));
		Assert.That(_gateway.Queries[2].StartTimeMs, Is.EqualTo(NowMs - 16 * DayMs - DayMs));
		Assert.That(_gateway.Queries[2].EndTimeMs, Is.EqualTo(NowMs - 2 * WeekMs));
		Assert.That(result.EarlyStopped, Is.False);
		Assert.That(result.NewExecutions.Select(execution => execution.ExecId),
			Is.EqualTo(new[] { "exec-1", "exec-2", "exec-3" }));

		// Assert: водяной знак продвинулся на момент запуска.
		Assert.That(_stateStore.Find("linear")!.ExecWatermarkMs, Is.EqualTo(NowMs));
	}

	[TestMethod]
	[Description("Зафиксированная граница backfill не ограничивает повторный полный проход")]
	public async Task TryIfFixedBoundaryDoesNotClampRepeatedBackfill()
	{
		// Arrange: граница backfill зафиксирована десять дней назад, водяного знака нет —
		// полный проход глубиной три недели. Граница — только факт о самой ранней увиденной
		// записи: окна листаются до пола глубины, а не до границы.
		// Требование: граница фиксируется как факт синхронизации и не ограничивает глубину
		// последующих проходов — повторные полные проходы листают окна до границы глубины.
		// Traceability: openspec:sync/bybit-history#requirement-backfill-full-history
		// Traceability: openspec:sync/bybit-history#scenario-backfill-depth-boundary
		await _stateStore.SaveAsync(new SyncState { Category = "linear", BackfillBoundaryMs = NowMs - 10 * DayMs });
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = 3 * WeekMs };
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-1", NowMs - DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-2", NowMs - 8 * DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());

		// Act
		var result = await _engine.RunAsync("linear", options);

		// Assert: три окна до пола глубины — второе окно началось глубже зафиксированной границы.
		Assert.That(result.Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(_gateway.Queries, Has.Count.EqualTo(3));
		Assert.That(_gateway.Queries[1].StartTimeMs, Is.EqualTo(NowMs - 2 * WeekMs));
		Assert.That(_gateway.Queries[2].StartTimeMs, Is.EqualTo(NowMs - 3 * WeekMs));
		Assert.That(result.HistoryExhausted, Is.True);

		// Assert: граница не откатилась — проход не увидел записей старше уже известной глубины.
		Assert.That(_stateStore.Find("linear")!.BackfillBoundaryMs, Is.EqualTo(NowMs - 10 * DayMs));
	}

	[TestMethod]
	[Description("Граница backfill фиксируется по каждой категории независимо")]
	public async Task TryIfBackfillBoundaryTrackedPerCategoryIndependently()
	{
		// Arrange: обе категории синхронизируются впервые, история разной глубины —
		// linear заканчивается двумя днями назад, option пятью. Глубина двух окон:
		// option проходится безфильтровой областью и областью актива доски.
		// Требование: backfill выполняется по каждой торговой категории отдельным проходом,
		// опционная категория — безфильтровой областью и с фильтром по базовому активу
		// по-активной области.
		// Traceability: openspec:sync/bybit-history#requirement-backfill-full-history
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = 2 * WeekMs };
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-linear", NowMs - 2 * DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-option", NowMs - 5 * DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());

		// Act
		var linearResult = await _engine.RunAsync("linear", options);
		var optionResult = await _engine.RunAsync("option", options);

		// Assert: каждый проход запрашивал только свою категорию; опционная безфильтровая
		// область несёт окна без фильтра, по-активная — фильтр актива из источника доски,
		// линейные — без фильтра.
		Assert.That(linearResult.Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(optionResult.Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(_gateway.Queries.Select(query => query.Category),
			Is.EqualTo(new[] { "linear", "linear", "option", "option", "option", "option" }));
		Assert.That(_gateway.Queries.Select(query => query.BaseCoin),
			Is.EqualTo(new string?[] { null, null, null, null, "BTC", "BTC" }));

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
			new ExecutionWindowPass(_gateway, _knownIdProbe), _gateway, _stateStore, _optionBaseCoins, new ManualTimeProvider(), writer);
		var progressRun = new SyncRun
		{
			StartedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
			Mode = SyncRunMode.Backfill,
			Status = SyncRunStatus.Running,
		};
		// Глубина двух окон: оба окна приносят по одной новой записи.
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = 2 * WeekMs };
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-1", NowMs - DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-2", NowMs - 8 * DayMs)] });

		// Act
		var result = await engine.RunAsync("linear", options, progressRun);

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
	[Description("Неположительная глубина backfill отклоняется до обращения к шлюзу и хранилищу")]
	[ExpectedException(typeof(ArgumentOutOfRangeException))]
	public async Task ThrowOnNonPositiveMaxBackfillDepth()
	{
		// Arrange: неположительный пол глубины сделал бы backfill пустым или бесконечным.
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = 0 };

		// Act — некорректная глубина прерывается до чтения состояния и запросов.
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
	[DataRow(false, false)]
	[DataRow(false, false, false)]
	[Description("Нулевая зависимость конструктора отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullConstructorDependency(
		bool nullWindowPass,
		bool nullStateStore = true,
		bool nullOptionBaseCoins = true)
	{
		// Arrange — Act: проход окна, шлюз биржи, хранилище состояния и источник активов обязательны движку.
		if (nullWindowPass)
		{
			new ExecutionCategorySync(null!, _gateway, _stateStore, _optionBaseCoins);
		}
		else if (nullStateStore)
		{
			new ExecutionCategorySync(new ExecutionWindowPass(_gateway, _knownIdProbe), _gateway, null!, _optionBaseCoins);
		}
		else
		{
			new ExecutionCategorySync(new ExecutionWindowPass(_gateway, _knownIdProbe), _gateway, _stateStore, null!);
		}
	}

	[TestMethod]
	[Description("Конфигурация глубже границы хранения биржи зажимает пол backfill — запросы окон не заходят в запретную зону")]
	public async Task TryIfConfigFloorDeeperThanBoundaryClampedToExchangeBoundary()
	{
		// Arrange: глубина backfill 730 дней пробивает границу хранения: серверное время
		// биржи даёт самую раннюю запрашиваемую дату на 723 дня позади, пол зажимается до неё.
		// Требование: пол backfill ограничивается границей хранения истории биржи, вычисленной
		// по серверному времени с запасом; запросы окон раньше границы не отправляются.
		// Traceability: openspec:sync/bybit-history#scenario-floor-clamped-to-exchange-boundary
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = 730 * DayMs };
		var allowedEarliestMs = NowMs - 723 * DayMs;

		// Act: перебор идёт до зажатого пола; незаготовленные окна шлюз отвечает пустыми страницами.
		var result = await _engine.RunAsync("linear", options);

		// Assert: все запросы — не раньше зажатого пола, последнее окно началось ровно на границе.
		Assert.That(_gateway.Queries, Is.Not.Empty);
		Assert.That(_gateway.Queries.All(query => query.StartTimeMs >= allowedEarliestMs), Is.True);
		Assert.That(_gateway.Queries.Last().StartTimeMs, Is.EqualTo(allowedEarliestMs));

		// Assert: окно полноты семи дней укладывается 104 раза в 723 дня, последнее — усечено до пола.
		Assert.That(_gateway.Queries, Has.Count.EqualTo(104));
		Assert.That(result.Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(result.HistoryExhausted, Is.True);
	}

	[TestMethod]
	[Description("Отказ серверного времени не роняет запуск — пол считается по локальным часам")]
	public async Task TryIfServerTimeFailureFallsBackToLocalTime()
	{
		// Arrange: эндпоинт серверного времени недоступен; глубина backfill мала и границы
		// хранения не пробивает ни по каким часам.
		// Требование: недоступность серверного времени не прерывает синхронизацию —
		// fallback на локальное время, а запас границы покрывает дрейф часов.
		// Traceability: openspec:sync/bybit-history#scenario-floor-clamped-to-exchange-boundary
		_gateway.ServerTimeError = new BybitApiException(10006, "Too many visits!");
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = 3 * WeekMs };
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-1", NowMs - DayMs)] });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());

		// Act
		var result = await _engine.RunAsync("linear", options);

		// Assert: серверное время запрошено один раз, запуск успешен, окна идут по локальным часам.
		Assert.That(_gateway.ServerTimeCalls, Is.EqualTo(1));
		Assert.That(result.Mode, Is.EqualTo(SyncRunMode.Backfill));
		Assert.That(_gateway.Queries, Has.Count.EqualTo(3));
		Assert.That(_gateway.Queries[0].StartTimeMs, Is.EqualTo(NowMs - WeekMs));
		Assert.That(_stateStore.Find("linear")!.ExecWatermarkMs, Is.EqualTo(NowMs));
	}

	[TestMethod]
	[Description("Инкрементальная догрузка с водяным знаком старше границы зажимает нижнюю границу перебора")]
	public async Task TryIfIncrementalTargetOlderThanBoundaryClamped()
	{
		// Arrange: водяной знак 800-дневной давности — за границей хранения биржи;
		// нижняя граница инкремента зажимается до самой ранней запрашиваемой даты.
		// Требование: долгий перерыв между запусками не отправляет запросы в запретную
		// зону — нижняя граница догрузки зажимается к границе хранения.
		// Traceability: openspec:sync/bybit-history#scenario-incremental-boundary-clamped
		await _stateStore.SaveAsync(new SyncState { Category = "linear", ExecWatermarkMs = NowMs - 800 * DayMs });
		var options = new ExecutionCategorySyncOptions { IncrementalOverlapMs = 0 };
		var allowedEarliestMs = NowMs - 723 * DayMs;

		// Act
		var result = await _engine.RunAsync("linear", options);

		// Assert: режим — инкремент, все запросы не раньше границы, последнее окно — от неё.
		Assert.That(result.Mode, Is.EqualTo(SyncRunMode.Incremental));
		Assert.That(_gateway.Queries, Is.Not.Empty);
		Assert.That(_gateway.Queries.All(query => query.StartTimeMs >= allowedEarliestMs), Is.True);
		Assert.That(_gateway.Queries.Last().StartTimeMs, Is.EqualTo(allowedEarliestMs));
		Assert.That(result.HistoryExhausted, Is.False);

		// Assert: водяной знак продвинут к моменту запуска.
		Assert.That(result.ExecWatermarkMs, Is.EqualTo(NowMs));
	}

	[TestMethod]
	[Description("Пограничное окно у пола повторяется с началом на границе хранения — записи зажатого окна сохраняются")]
	public async Task TryIfBoundaryWindowRetriedWithClampedStartAndRecordsSaved()
	{
		// Arrange: глубина 730 дней, пол зажат границей хранения; последнее окно перебора
		// [граница, предыдущее окно] биржа отвергает как выходящий за глубину, повторный
		// запрос с зажатым началом приносит запись у границы.
		// Требование: окно, отклонённое за глубину хранения, повторяется один раз
		// с временем начала на границе, записи зажатого диапазона загружаются и сохраняются.
		// Traceability: openspec:sync/bybit-history#scenario-boundary-window-retried-clamped
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = 730 * DayMs };
		var allowedEarliestMs = NowMs - 723 * DayMs;
		var lastWindowStartMs = NowMs - 721 * DayMs;

		// Первые 103 окна перебора пусты, последнее окно у границы отклоняется,
		// ретрай того же окна с зажатым началом отдаёт запись у границы.
		for (var index = 0; index < 103; index++)
		{
			_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());
		}

		_gateway.EnqueueError(new BybitApiException(
			10001, "Can't query order earlier than 2 years, please check your params: startTime or endTime!"));
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>
		{
			List = [Execution("exec-at-boundary", NowMs - 722 * DayMs)],
		});

		// Act
		var result = await _engine.RunAsync("linear", options);

		// Assert: отказавшее окно и его ретрай запрошены с началом ровно на границе хранения.
		Assert.That(_gateway.Queries, Has.Count.EqualTo(105));
		Assert.That(_gateway.Queries[103].StartTimeMs, Is.EqualTo(allowedEarliestMs));
		Assert.That(_gateway.Queries[104].StartTimeMs, Is.EqualTo(allowedEarliestMs));
		Assert.That(_gateway.Queries[104].EndTimeMs, Is.EqualTo(lastWindowStartMs));

		// Assert: запись зажатого окна загружена и сохранена, запуск успешен.
		Assert.That(result.NewExecutions.Select(execution => execution.ExecId),
			Is.EqualTo(new[] { "exec-at-boundary" }));
		Assert.That(result.HistoryExhausted, Is.True);
		Assert.That(_stateStore.Find("linear")!.BackfillBoundaryMs, Is.EqualTo(NowMs - 722 * DayMs));
	}

	[TestMethod]
	[Description("Повторный пограничный отказ исчерпывает единственную область linear — проход категории завершается без сбоя")]
	public async Task TryIfRepeatedBoundaryRefusalEndsWalkWithoutOtherScopes()
	{
		// Arrange: linear читается единственной областью без фильтра; первое же окно
		// биржа отвергает за глубину хранения, повтор с зажатым началом тоже отклонён.
		// Требование: повторный пограничный отказ исчерпывает историю области — оставшиеся
		// окна не запрашиваются, запуск успешен с водяным знаком.
		// Traceability: openspec:sync/bybit-history#scenario-boundary-refusal-ends-walk
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = WeekMs };
		_gateway.EnqueueError(new BybitApiException(
			10001, "Can't query order earlier than 2 years, please check your params: startTime or endTime!"));
		_gateway.EnqueueError(new BybitApiException(
			10001, "Can't query order earlier than 2 years, please check your params: startTime or endTime!"));

		// Act: исключение не выходит наружу — запуск завершается штатно.
		var result = await _engine.RunAsync("linear", options);

		// Assert: область запрошена дважды — отказ и его ретрай с зажатым началом;
		// оставшиеся окна не запрашивались.
		Assert.That(_gateway.Queries, Has.Count.EqualTo(2));
		Assert.That(_gateway.Queries.All(query => query.BaseCoin == null), Is.True);

		// Assert: перебор исчерпан, водяной знак зафиксирован, запуск успешен.
		Assert.That(result.HistoryExhausted, Is.True);
		Assert.That(result.ExecWatermarkMs, Is.EqualTo(NowMs));
		var saved = _stateStore.Find("linear");
		Assert.That(saved, Is.Not.Null);
		Assert.That(saved!.ExecWatermarkMs, Is.EqualTo(NowMs));
		Assert.That(saved.LastSuccessAt, Is.Not.Null);

		// Assert: пограничное исчерпание — не пропуск области: перечень пропущенных пуст.
		// Traceability: openspec:sync/bybit-history#requirement-contract-unavailable-area-skip
		Assert.That(result.SkippedAreas, Is.Empty);
	}

	[TestMethod]
	[Description("Пограничное исчерпание области BTC не отменяет обход безфильтровой области и области ETH")]
	public async Task TryIfBoundaryExhaustionInOptionAreaDoesNotStopOtherAreas()
	{
		// Arrange: доска из одного актива; первое же окно по-активной области BTC биржа
		// отвергает за глубину хранения, повтор с зажатым началом тоже отклонён.
		// Требование: пограничное исчерпание действует в границах одной области — обход
		// её окон прекращается, безфильтровая и по-активная области других активов
		// запрашиваются штатно.
		// Traceability: openspec:sync/bybit-history#scenario-boundary-exhaustion-scoped-to-area
		var optionBaseCoins = new FakeOptionBaseCoinSource("BTC", "ETH");
		var engine = new ExecutionCategorySync(
			new ExecutionWindowPass(_gateway, _knownIdProbe), _gateway, _stateStore, optionBaseCoins, new ManualTimeProvider());
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = WeekMs };
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());
		_gateway.EnqueueError(new BybitApiException(
			10001, "Can't query order earlier than 2 years, please check your params: startTime or endTime!"));
		_gateway.EnqueueError(new BybitApiException(
			10001, "Can't query order earlier than 2 years, please check your params: startTime or endTime!"));
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-eth", NowMs - DayMs)] });

		// Act: исключение не выходит наружу — запуск завершается штатно.
		var result = await engine.RunAsync("option", options);

		// Assert: безфильтровая область пройдена до исчерпания BTC, область ETH запрошена
		// и догружена; оставшиеся окна BTC не запрашивались.
		Assert.That(_gateway.Queries, Has.Count.EqualTo(4));
		Assert.That(_gateway.Queries.Select(query => query.BaseCoin),
			Is.EqualTo(new string?[] { null, "BTC", "BTC", "ETH" }));
		Assert.That(result.NewExecutions.Select(execution => execution.ExecId), Is.EqualTo(new[] { "exec-eth" }));

		// Assert: запуск успешен с водяным знаком; исчерпание одной области в backfill
		// не отменяет факт исчерпания перебора категории.
		// Traceability: openspec:sync/bybit-history#scenario-boundary-refusal-ends-walk
		Assert.That(result.HistoryExhausted, Is.True);
		Assert.That(result.ExecWatermarkMs, Is.EqualTo(NowMs));
		var saved = _stateStore.Find("option");
		Assert.That(saved, Is.Not.Null);
		Assert.That(saved!.ExecWatermarkMs, Is.EqualTo(NowMs));
		Assert.That(saved.LastSuccessAt, Is.Not.Null);

		// Assert: пограничное исчерпание — не пропуск области: перечень пропущенных пуст.
		// Traceability: openspec:sync/bybit-history#requirement-contract-unavailable-area-skip
		Assert.That(result.SkippedAreas, Is.Empty);
	}

	[TestMethod]
	[Description("Пограничное исчерпание в инкременте отражается отчётным флагом, не отменяя обход остальных областей")]
	public async Task TryIfIncrementalBoundaryExhaustionIsReportedWhileOtherAreasWalked()
	{
		// Arrange: водяной знак суток, доска из двух активов; окно области BTC биржа
		// отвергает за глубину хранения дважды.
		// Требование: в инкрементальном режиме HistoryExhausted отражает исчерпание хотя бы
		// одной области, при этом области остальных активов обходятся штатно.
		// Traceability: openspec:sync/bybit-history#scenario-boundary-exhaustion-scoped-to-area
		var optionBaseCoins = new FakeOptionBaseCoinSource("BTC", "ETH");
		var engine = new ExecutionCategorySync(
			new ExecutionWindowPass(_gateway, _knownIdProbe), _gateway, _stateStore, optionBaseCoins, new ManualTimeProvider());
		await _stateStore.SaveAsync(new SyncState { Category = "option", ExecWatermarkMs = NowMs - DayMs });
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());
		_gateway.EnqueueError(new BybitApiException(
			10001, "Can't query order earlier than 2 years, please check your params: startTime or endTime!"));
		_gateway.EnqueueError(new BybitApiException(
			10001, "Can't query order earlier than 2 years, please check your params: startTime or endTime!"));
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-eth", NowMs - 2 * 3600_000L)] });

		// Act
		var result = await engine.RunAsync("option");

		// Assert: область ETH пройдена после исчерпания BTC.
		Assert.That(_gateway.Queries.Select(query => query.BaseCoin),
			Is.EqualTo(new string?[] { null, "BTC", "BTC", "ETH" }));
		Assert.That(result.NewExecutions.Select(execution => execution.ExecId), Is.EqualTo(new[] { "exec-eth" }));

		// Assert: исчерпание хотя бы одной области отражено флагом, водяной знак штатно
		// зафиксирован, запуск успешен.
		// Traceability: openspec:sync/bybit-history#scenario-boundary-refusal-ends-walk
		Assert.That(result.HistoryExhausted, Is.True);
		Assert.That(result.ExecWatermarkMs, Is.EqualTo(NowMs));
		Assert.That(_stateStore.Find("option")!.LastSuccessAt, Is.Not.Null);
	}

	[TestMethod]
	[Description("Отказ 110023 на безфильтровой области option пропускает её в backfill — по-активные области догружаются, запуск успешен")]
	public async Task TryIfUnavailableOptionAreaSkippedInBackfillAndOtherScopesLoaded()
	{
		// Arrange: первое же окно безфильтровой области биржа отвечает отказом
		// «контракт недоступен для торговли», окно области ETH приносит запись.
		// Требование: отказ 110023 не прерывает запуск — область пропускается без ретрая,
		// остальные области догружаются, водяной знак фиксируется штатно.
		// Traceability: openspec:sync/bybit-history#scenario-unavailable-option-area-skipped
		var optionBaseCoins = new FakeOptionBaseCoinSource("BTC", "ETH");
		var engine = new ExecutionCategorySync(
			new ExecutionWindowPass(_gateway, _knownIdProbe), _gateway, _stateStore, optionBaseCoins, new ManualTimeProvider());
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = WeekMs };
		_gateway.EnqueueError(new BybitApiException(110023, "The contract is not available for trades"));
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-eth", NowMs - DayMs)] });

		// Act
		var result = await engine.RunAsync("option", options);

		// Assert: безфильтровая область запрошена один раз — отказ не ретраится, оставшиеся
		// окна области не запрашиваются, по-активные области догружены.
		Assert.That(_gateway.Queries, Has.Count.EqualTo(3));
		Assert.That(_gateway.Queries.Select(query => query.BaseCoin),
			Is.EqualTo(new string?[] { null, "BTC", "ETH" }));
		Assert.That(result.NewExecutions.Select(execution => execution.ExecId), Is.EqualTo(new[] { "exec-eth" }));
		Assert.That(result.WindowsProcessed, Is.EqualTo(2));

		// Assert: пропуск безфильтровой области зафиксирован меткой категории, запуск
		// успешен с водяным знаком.
		// Traceability: openspec:sync/bybit-history#requirement-contract-unavailable-area-skip
		Assert.That(result.SkippedAreas, Is.EqualTo(new[] { "option" }));
		Assert.That(result.ExecWatermarkMs, Is.EqualTo(NowMs));
		var saved = _stateStore.Find("option");
		Assert.That(saved, Is.Not.Null);
		Assert.That(saved!.ExecWatermarkMs, Is.EqualTo(NowMs));
		Assert.That(saved.LastSuccessAt, Is.Not.Null);
	}

	[TestMethod]
	[Description("Отказ 110023 на безфильтровой области option пропускает её в инкременте — по-активные области догружаются, запуск успешен")]
	public async Task TryIfUnavailableOptionAreaSkippedInIncrementalAndOtherScopesLoaded()
	{
		// Arrange: водяной знак суток; окно безфильтровой области биржа отвечает отказом
		// «контракт недоступен», окно области ETH приносит новую запись.
		// Требование: отказ 110023 не прерывает инкрементальную догрузку — область
		// пропускается, остальные догружаются, водяной знак фиксируется штатно.
		// Traceability: openspec:sync/bybit-history#scenario-unavailable-option-area-skipped
		var optionBaseCoins = new FakeOptionBaseCoinSource("BTC", "ETH");
		var engine = new ExecutionCategorySync(
			new ExecutionWindowPass(_gateway, _knownIdProbe), _gateway, _stateStore, optionBaseCoins, new ManualTimeProvider());
		await _stateStore.SaveAsync(new SyncState { Category = "option", ExecWatermarkMs = NowMs - DayMs });
		_gateway.EnqueueError(new BybitApiException(110023, "The contract is not available for trades"));
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-eth", NowMs - 2 * 3600_000L)] });

		// Act
		var result = await engine.RunAsync("option");

		// Assert: безфильтровая и по-активные области получили по окну, отказ не ретраился.
		Assert.That(_gateway.Queries, Has.Count.EqualTo(3));
		Assert.That(_gateway.Queries.Select(query => query.BaseCoin),
			Is.EqualTo(new string?[] { null, "BTC", "ETH" }));
		Assert.That(result.NewExecutions.Select(execution => execution.ExecId), Is.EqualTo(new[] { "exec-eth" }));

		// Assert: пропуск зафиксирован меткой, история не объявлена исчерпанной —
		// пропуск и исчерпание перебора разные факты, водяной знак зафиксирован штатно.
		// Traceability: openspec:sync/bybit-history#requirement-contract-unavailable-area-skip
		Assert.That(result.SkippedAreas, Is.EqualTo(new[] { "option" }));
		Assert.That(result.HistoryExhausted, Is.False);
		Assert.That(result.ExecWatermarkMs, Is.EqualTo(NowMs));
		Assert.That(_stateStore.Find("option")!.LastSuccessAt, Is.Not.Null);
	}

	[TestMethod]
	[Description("Отказ 110023 на единственной безфильтровой области linear завершает проход категории без сбоя")]
	public async Task TryIfUnavailableSingleLinearAreaEndsCategoryWalk()
	{
		// Arrange: linear читается одной областью без фильтра; первое же окно биржа
		// отвечает отказом «контракт недоступен для торговли».
		// Требование: пропуск единственной области завершает проход категории — запуск
		// успешен со штатно зафиксированным водяным знаком, метка пропуска — категория.
		// Traceability: openspec:sync/bybit-history#scenario-unavailable-single-area-ends-walk
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = WeekMs };
		_gateway.EnqueueError(new BybitApiException(110023, "The contract is not available for trades"));

		// Act: исключение не выходит наружу — запуск завершается штатно.
		var result = await _engine.RunAsync("linear", options);

		// Assert: область запрошена один раз — отказ не ретраился, окна не листались.
		Assert.That(_gateway.Queries, Has.Count.EqualTo(1));
		Assert.That(_gateway.Queries[0].BaseCoin, Is.Null);
		Assert.That(result.NewExecutions, Is.Empty);
		Assert.That(result.WindowsProcessed, Is.EqualTo(0));

		// Assert: метка пропуска — только категория, водяной знак зафиксирован штатно.
		// Traceability: openspec:sync/bybit-history#requirement-contract-unavailable-area-skip
		Assert.That(result.SkippedAreas, Is.EqualTo(new[] { "linear" }));
		Assert.That(result.ExecWatermarkMs, Is.EqualTo(NowMs));
		var saved = _stateStore.Find("linear");
		Assert.That(saved, Is.Not.Null);
		Assert.That(saved!.ExecWatermarkMs, Is.EqualTo(NowMs));
		Assert.That(saved.LastSuccessAt, Is.Not.Null);
	}

	[TestMethod]
	[Description("Перечень пройденных активов доски собирает и пропущенные по 110023, и догруженные области")]
	public async Task TryIfPassedBaseCoinsIncludeSkippedAndWalkedBoardAreas()
	{
		// Arrange: безфильтровая область биржа отвечает отказом «контракт недоступен
		// для торговли», области BTC и ETH запрашиваются штатно.
		// Требование: перечень пройденных областей доски — вход расчёта непокрытых
		// активов — отражает все запрошенные по-активные области запуска, включая
		// пропущенные как недоступные; безфильтровая область покрытием актива не считается.
		// Traceability: openspec:sync/bybit-history#requirement-uncovered-base-coin-visibility
		var optionBaseCoins = new FakeOptionBaseCoinSource("BTC", "ETH");
		var engine = new ExecutionCategorySync(
			new ExecutionWindowPass(_gateway, _knownIdProbe), _gateway, _stateStore, optionBaseCoins, new ManualTimeProvider());
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = WeekMs };
		_gateway.EnqueueError(new BybitApiException(110023, "The contract is not available for trades"));
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution>());
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-eth", NowMs - DayMs)] });

		// Act
		var result = await engine.RunAsync("option", options);

		// Assert: обе по-активные области запрошены и перечислены пройденными — пропуск
		// безфильтровой области не выбрасывает активы из перечня, а сама она покрытия
		// актива не даёт.
		// Traceability: openspec:sync/bybit-history#requirement-uncovered-base-coin-visibility
		Assert.That(result.PassedBaseCoins, Is.EqualTo(new[] { "BTC", "ETH" }));
	}

	[TestMethod]
	[Description("Категория без деления доски не даёт пройденных базовых активов")]
	public async Task TryIfLinearRunPassesNoBaseCoins()
	{
		// Arrange: linear читается одной областью без фильтра по активу — пройденных
		// базовых активов доски у категории нет.
		// Требование: перечень пройденных активов доски наполняют только option-области.
		// Traceability: openspec:sync/bybit-history#requirement-uncovered-base-coin-visibility
		var options = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = WeekMs };
		_gateway.Enqueue(new BybitPagedResponse<BybitExecution> { List = [Execution("exec-lin", NowMs - DayMs)] });

		// Act
		var result = await _engine.RunAsync("linear", options);

		// Assert
		Assert.That(result.PassedBaseCoins, Is.Empty);
	}

	#region Помощники

	private static BybitExecution Execution(string execId, long execTimeMs) => Execution(execId, execTimeMs, "BTCUSDT");

	private static BybitExecution Execution(string execId, long execTimeMs, string symbol) => new()
	{
		Symbol = symbol,
		ExecId = execId,
		Side = "Buy",
		ExecTimeMs = execTimeMs,
	};

	#endregion

	#region Фиктивные зависимости

	/// <summary>
	/// Фиктивный источник базовых активов опционной доски: возвращает заготовленный
	/// список активов каждого обращения и помнит число обращений за списком.
	/// </summary>
	private sealed class FakeOptionBaseCoinSource : IOptionBaseCoinSource
	{
		private readonly IReadOnlyList<IReadOnlyList<string>> _listsByCall;

		public FakeOptionBaseCoinSource(params string[] baseCoins)
			: this(new IReadOnlyList<string>[] { baseCoins })
		{
		}

		/// <summary>Создаёт источник с перечнем активов, меняющимся между обращениями.</summary>
		public FakeOptionBaseCoinSource(IReadOnlyList<IReadOnlyList<string>> baseCoinsByCall)
		{
			_listsByCall = baseCoinsByCall;
		}

		/// <summary>Сколько раз движок запрашивал список активов.</summary>
		public int Calls { get; private set; }

		public Task<IReadOnlyList<string>> GetBaseCoinsAsync(CancellationToken cancellationToken = default)
		{
			var list = _listsByCall[Math.Min(Calls, _listsByCall.Count - 1)];
			Calls++;
			return Task.FromResult(list);
		}
	}

	/// <summary>
	/// Писатель сырых записей, связанный с проверкой известных execId: вставленная пачка
	/// сразу становится известной — так же записи сырьевого хранилища видны повторным
	/// проходам того же запуска и проходам следующих запусков.
	/// </summary>
	private sealed class IdempotentRawWriter : IRawExecutionBatchWriter
	{
		private readonly FakeKnownIdProbe _probe;

		public IdempotentRawWriter(FakeKnownIdProbe probe)
		{
			_probe = probe;
		}

		/// <summary>Все вставленные execId в порядке вызовов.</summary>
		public List<string> InsertedExecIds { get; } = [];

		public Task<RawExecutionBatchResult> WriteAsync(
			string category,
			IReadOnlyCollection<BybitExecution> executions,
			SyncRun? progressRun = null,
			CancellationToken cancellationToken = default)
		{
			foreach (var execution in executions)
			{
				_probe.Know(execution.ExecId);
				InsertedExecIds.Add(execution.ExecId);
			}

			return Task.FromResult(new RawExecutionBatchResult
			{
				InsertedExecIds = executions.Select(execution => execution.ExecId).ToList(),
				SkippedKnownCount = 0,
			});
		}
	}

	/// <summary>
	/// Фиктивный шлюз биржи: раздаёт заготовленные страницы и ошибки по порядку и помнит
	/// все запросы движка. При исчерпании сценария отвечает пустой страницей без курсора.
	/// Серверное время по умолчанию совпадает с виртуальными часами движка.
	/// </summary>
	private sealed class ScriptedGateway : IBybitHistoryGateway
	{
		private readonly Queue<object> _responses = new();

		/// <summary>Все запросы движка в порядке отправления.</summary>
		public List<BybitExecutionListQuery> Queries { get; } = [];

		/// <summary>Серверное время биржи, отдаваемое шлюзом; по умолчанию — момент запуска.</summary>
		public long ServerTimeMs { get; set; } = NowMs;

		/// <summary>Отказ эндпоинта серверного времени; null — эндпоинт отвечает успешно.</summary>
		public BybitApiException? ServerTimeError { get; set; }

		/// <summary>Сколько раз движок запрашивал серверное время.</summary>
		public int ServerTimeCalls { get; private set; }

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

		public Task<long> GetServerTimeMsAsync(CancellationToken cancellationToken = default)
		{
			ServerTimeCalls++;
			return ServerTimeError is not null
				? Task.FromException<long>(ServerTimeError)
				: Task.FromResult(ServerTimeMs);
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
	/// и помнит все сохранённые состояния, обращения за ними и сбросы.
	/// </summary>
	private sealed class FakeStateStore : IExecutionSyncStateStore
	{
		private readonly Dictionary<string, SyncState> _states = new(StringComparer.Ordinal);

		/// <summary>Все состояния в порядке сохранения движком.</summary>
		public List<SyncState> Saved { get; } = [];

		/// <summary>Сколько раз движок читал состояние категории.</summary>
		public int FindCalls { get; private set; }

		/// <summary>Категории, чьё состояние сбрасывали, в порядке вызовов.</summary>
		public List<string> ResetCategories { get; } = [];

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

		public Task ResetAsync(string category, CancellationToken cancellationToken = default)
		{
			ResetCategories.Add(category);
			_states.Remove(category);
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
