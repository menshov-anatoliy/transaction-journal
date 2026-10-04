namespace TransactionJournal.Tests.Hints.Engine;

using Moq;
using TransactionJournal.Domain.Data;
using TransactionJournal.Hints;
using TransactionJournal.Hints.Corpus;
using TransactionJournal.Hints.Ports;

/// <summary>
/// Каркас e2e-тестов прохода движка: один прогон над стабами портов собирает
/// сохранённые записи подсказок, чтобы пары «сработало / не сработало» и поля
/// записей проверялись тем же путём, каким работает приложение.
/// </summary>
internal static class HintPassHarness
{
	/// <summary>Отметка as-of всех прогонов: четверг 2026-10-08, ISO-неделя 41.</summary>
	public static readonly DateTimeOffset FixedNow = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

	/// <summary>Марка спота BTC для условий у страйка.</summary>
	public const decimal BtcSpot = 50_000m;

	/// <summary>Марка спота ETH для условий у страйка.</summary>
	public const decimal EthSpot = 3_000m;

	/// <summary>
	/// Прогоняет проход над корпусом каталога и снапшотом журнала; возвращает итог и сохранённые записи.
	/// </summary>
	/// <param name="corpusDir">Каталог корпуса карточек.</param>
	/// <param name="snapshot">Снимок журнала прохода.</param>
	/// <param name="marks">Марки инструментов прохода; null — пустая доступная партия.</param>
	public static async Task<(HintPassResult Result, List<HintRecord> Added)> RunAsync(
		string corpusDir,
		JournalSnapshot snapshot,
		IReadOnlyDictionary<string, decimal>? marks = null)
	{
		var added = new List<HintRecord>();
		var hintStore = new Mock<IHintStore>();
		hintStore
			.Setup(store => store.AddAsync(It.IsAny<HintRecord>(), It.IsAny<CancellationToken>()))
			.Callback<HintRecord, CancellationToken>((record, _) => added.Add(record))
			.ReturnsAsync((HintRecord record, CancellationToken _) => record with { Id = added.Count });
		SetupLiveQueries(hintStore);

		var pass = new HintAgentPass(
			new RulesCorpusLoader(corpusDir),
			StubJournalReader(snapshot).Object,
			StubMarkSource(marks).Object,
			hintStore.Object,
			StubClock(null).Object);
		var result = await pass.RunAsync();
		return (result, added);
	}

	/// <summary>
	/// Прогоняет проход над реальным хранилищем подсказок (SQLite) — тот же
	/// путь, каким работает приложение: дедуп-окно, гашения и переходы читают
	/// и пишут настоящую базу. Записи, созданные этим проходом, вычисляются
	/// разностью содержимого хранилища до и после.
	/// </summary>
	/// <param name="corpusDir">Каталог корпуса карточек.</param>
	/// <param name="snapshot">Снимок журнала прохода.</param>
	/// <param name="hintStore">Реальное хранилище подсказок прохода.</param>
	/// <param name="marks">Марки инструментов прохода; null — пустая доступная партия.</param>
	/// <param name="now">Отметка as-of прохода; null — фиксированная отметка каркаса.</param>
	public static async Task<(HintPassResult Result, List<HintRecord> Added)> RunAsync(
		string corpusDir,
		JournalSnapshot snapshot,
		IHintStore hintStore,
		IReadOnlyDictionary<string, decimal>? marks = null,
		DateTimeOffset? now = null)
	{
		var beforeIds = (await hintStore.ListAllAsync().ConfigureAwait(false))
			.Select(record => record.Id)
			.ToHashSet();
		var pass = new HintAgentPass(
			new RulesCorpusLoader(corpusDir),
			StubJournalReader(snapshot).Object,
			StubMarkSource(marks).Object,
			hintStore,
			StubClock(now).Object);
		var result = await pass.RunAsync();
		var added = (await hintStore.ListAllAsync().ConfigureAwait(false))
			.Where(record => beforeIds.Contains(record.Id) == false)
			.ToList();
		return (result, added);
	}

	/// <summary>Стаб читателя журнал-снапшота с заданным снимком.</summary>
	private static Mock<IJournalSnapshotReader> StubJournalReader(JournalSnapshot snapshot)
	{
		var journalReader = new Mock<IJournalSnapshotReader>();
		journalReader
			.Setup(reader => reader.ReadAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync(snapshot);
		return journalReader;
	}

	/// <summary>Стаб источника марок с доступной партией.</summary>
	private static Mock<IMarkSource> StubMarkSource(IReadOnlyDictionary<string, decimal>? marks)
	{
		var markSource = new Mock<IMarkSource>();
		markSource
			.Setup(source => source.GetMarksAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(new MarkBatch { Marks = marks ?? new Dictionary<string, decimal>(), FailureReason = null });
		return markSource;
	}

	/// <summary>Стаб часов с фиксированной отметкой as-of.</summary>
	private static Mock<IClock> StubClock(DateTimeOffset? now)
	{
		var clock = new Mock<IClock>();
		clock.SetupGet(clock => clock.UtcNow).Returns(now ?? FixedNow);
		return clock;
	}

	/// <summary>
	/// Настраивает пустые ответы живых запросов стаба хранилища: поиск записи
	/// окна возвращает null, список живых записей пуст — стаб имитирует пустое
	/// хранилище, где ни одна подсказка не подавляется и не гасится.
	/// </summary>
	private static void SetupLiveQueries(Mock<IHintStore> hintStore)
	{
		hintStore
			.Setup(store => store.FindWindowEntryAsync(
				It.IsAny<string>(),
				It.IsAny<HintSubject>(),
				It.IsAny<string?>(),
				It.IsAny<CancellationToken>()))
			.ReturnsAsync((HintRecord?)null);
		hintStore
			.Setup(store => store.ListLiveAsync(It.IsAny<CancellationToken>()))
			.ReturnsAsync([]);
	}

	/// <summary>Собирает конструкцию снимка с подменами значимых для триггеров полей.</summary>
	public static ConstructionView Construction(
		long id = 1,
		bool isOpen = true,
		decimal? capital = null,
		decimal? profitValue = null,
		TargetUnit? profitUnit = null,
		decimal? totalPnL = null,
		decimal? totalPnLPercent = null,
		IReadOnlyList<PositionView>? positions = null,
		IReadOnlyList<TradeView>? trades = null)
		=> new()
		{
			Id = id,
			Name = $"Конструкция {id}",
			IsOpen = isOpen,
			AllocatedCapitalUsdt = capital,
			RiskValue = null,
			RiskUnit = null,
			ProfitValue = profitValue,
			ProfitUnit = profitUnit,
			RealizedPnL = 0m,
			UnrealizedPnL = positions?.Sum(position => position.UnrealizedPnL ?? 0m),
			AdjustmentsPnL = 0m,
			TotalPnL = totalPnL,
			TotalPnLPercent = totalPnLPercent,
			OpenedAt = trades is { Count: > 0 } ? trades.Min(trade => trade.ExecutedAt) : null,
			ClosedAt = null,
			Positions = positions ?? [],
			Trades = trades ?? [],
		};

	/// <summary>Открытая позиция конструкции по символу.</summary>
	public static PositionView Position(string symbol, decimal residual, decimal? unrealizedPnL = null)
		=> new()
		{
			Symbol = symbol,
			Residual = residual,
			IsOpen = residual != 0m,
			RealizedPnL = 0m,
			UnrealizedPnL = unrealizedPnL,
			AverageOpenPrice = null,
			MarkPrice = null,
			OpenedAt = FixedNow,
			ClosedAt = null,
		};

	/// <summary>Сделка хронологии конструкции.</summary>
	public static TradeView Trade(string execId, string symbol, decimal quantity, decimal price, decimal fee, int dayOffset = 0)
		=> new()
		{
			ExecId = execId,
			Symbol = symbol,
			Quantity = quantity,
			Price = price,
			Fee = fee,
			ExecutedAt = FixedNow.AddDays(dayOffset),
		};

	/// <summary>Карточка-минимум в памяти для юнит-тестов рендера и реестра.</summary>
	public static RuleCard MakeCard(
		string id,
		string? implementation = null,
		RuleClarity clarity = RuleClarity.Crisp,
		string? hintTemplate = null,
		IReadOnlyList<RuleThreshold>? thresholds = null)
		=> new()
		{
			Id = id,
			Title = $"Правило {id}",
			Character = "risk-mode",
			Clarity = clarity,
			Scope = RuleScope.OpenConstructions,
			Status = RuleCardStatus.Active,
			Thresholds = thresholds ?? [],
			TriggerImplementation = implementation,
			HintTemplate = hintTemplate,
			Sources =
			[
				new RuleSource { Tag = "ТЕСТ", File = "Тесты/Тест.md", Quotes = ["«Цитата»"] },
			],
			ConflictsWith = [],
		};
}
