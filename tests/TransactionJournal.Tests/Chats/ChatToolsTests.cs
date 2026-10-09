using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Chats;
using TransactionJournal.Chats.Ports;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;
using OptionType = TransactionJournal.Domain.Materialization.OptionType;

namespace TransactionJournal.Tests.Chats;

/// <summary>
/// Проверки реестра инструментов чата: реестр содержит ровно три
/// read-only функции, собирается из набора источников чата — только
/// инструменты выбранных категорий закрытого справочника, карточка правила
/// читается полным текстом только по id, доска опционов отдаётся компактной
/// проекцией вместо сырых данных, а недоступность биржи отдаётся
/// структурированным «недоступно» с последней кэшированной проекцией марок
/// и её as-of.
/// </summary>
[TestClass]
public sealed class ChatToolsTests
{
	/// <summary>Фиксированный момент as-of для детерминированных ответов портов.</summary>
	private static readonly DateTimeOffset FixedNow = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

	[TestMethod]
	[Description("Реестр содержит ровно три read-only инструмента с фиксированными именами")]
	public void CreateTools_ExactlyThreeReadOnlyTools()
	{
		// Arrange: реестр поверх заглушек портов.
		var tools = CreateTools();

		// Act: собираем реестр функций инструментов.
		var functions = tools.CreateTools();

		// Assert: функций ровно три, имена фиксированы, пишущих и посторонних
		// инструментов в реестре нет.
		// Traceability: openspec:chats/sources#scenario-sources-write-never
		Assert.That(functions, Has.Count.EqualTo(3));
		Assert.That(functions.Select(function => function.Name), Is.EqualTo(
		[
			ChatTools.ReadRuleCardToolName,
			ChatTools.GetMarketSnapshotToolName,
			ChatTools.GetOptionBoardToolName,
		]));
		Assert.That(
			functions.Select(function => function.Name),
			Has.All.Matches<string>(name => name is "read_rule_card" or "get_market_snapshot" or "get_option_board"));
	}

	[TestMethod]
	[Description("Закрытый справочник источников содержит ровно три категории в каноническом порядке")]
	// Справочник источников закрыт: журнал, корпус правил, рынок Bybit — и
	// ничего сверх него; полный набор служит дефолтом чата.
	// Traceability: openspec:chats/sources#scenario-sources-three-categories
	public void TryIfClosedCatalog_ContainsExactlyThreeCategories()
	{
		// Assert: полный набор — все три категории справочника в каноническом порядке.
		Assert.That(ChatDataSourceCatalog.All, Is.EqualTo(new[]
		{
			ChatDataSource.Journal,
			ChatDataSource.RulesCorpus,
			ChatDataSource.BybitMarket,
		}));
		Assert.That(Enum.GetValues<ChatDataSource>(), Has.Length.EqualTo(3));
	}

	[TestMethod]
	[Description("Набор источников — параметр: реестр собирается только из инструментов выбранных категорий")]
	// Владелец выбирает подмножество справочника; реестр показывает ровно те
	// инструменты, что обслуживают выбранные источники: журнал читается
	// снимком контекста без тула, корпус даёт карточку правила, рынок —
	// снимок фьючерсов и доску опционов.
	// Traceability: openspec:chats/sources#scenario-sources-subset-parameter
	[DataRow(new ChatDataSource[] { ChatDataSource.Journal }, new string[] { })]
	[DataRow(new ChatDataSource[] { ChatDataSource.RulesCorpus }, new string[] { ChatTools.ReadRuleCardToolName })]
	[DataRow(new ChatDataSource[] { ChatDataSource.BybitMarket }, new string[] { ChatTools.GetMarketSnapshotToolName, ChatTools.GetOptionBoardToolName })]
	[DataRow(new ChatDataSource[] { ChatDataSource.RulesCorpus, ChatDataSource.BybitMarket }, new string[] { ChatTools.ReadRuleCardToolName, ChatTools.GetMarketSnapshotToolName, ChatTools.GetOptionBoardToolName })]
	public void TryIfSourcesSubset_RegistryContainsOnlySelectedSourceTools(ChatDataSource[] sources, string[] expectedToolNames)
	{
		// Act: собираем реестр из подмножества справочника.
		var functions = CreateTools().CreateTools(sources);

		// Assert: состав и порядок реестра совпадают с выбранными источниками.
		Assert.That(functions.Select(function => function.Name), Is.EqualTo(expectedToolNames));
	}

	[TestMethod]
	[Description("Пустой набор источников не собирает реестр")]
	// Чат обязан выбрать хотя бы одну категорию справочника: пустой набор —
	// ошибка параметров.
	// Traceability: openspec:chats/sources#scenario-sources-subset-parameter
	public void ThrowOnCreateToolsWithEmptySources()
	{
		// Assert
		Assert.Throws<ArgumentException>(() => CreateTools().CreateTools([]));
	}

	[TestMethod]
	[Description("Реестр чата без источника «рынок Bybit» не содержит рыночных инструментов")]
	// Реестр совпадает с набором источников чата: без рыночной категории ни
	// снимок фьючерсов, ни доска опционов не попадают в реестр.
	// Traceability: openspec:chats/sources#scenario-sources-registry-matches-chat-sources
	public void TryIfRegistryWithoutMarketSource_ContainsNoMarketTools()
	{
		// Arrange: чат только с журналом и корпусом правил.
		var sources = new[] { ChatDataSource.Journal, ChatDataSource.RulesCorpus };

		// Act
		var functions = CreateTools().CreateTools(sources);

		// Assert: остаётся только чтение карточки правила.
		Assert.That(functions.Select(function => function.Name), Is.EqualTo([ChatTools.ReadRuleCardToolName]));
	}

	[TestMethod]
	[Description("При любом наборе источников реестр содержит только три read-only инструмента")]
	// Источники read-only: ни один набор справочника не порождает пишущих
	// или посторонних инструментов — реестр всегда подмножество тройки чтения.
	// Traceability: openspec:chats/sources#scenario-sources-write-never
	public void TryIfAnySourceSet_RegistryKeepsOnlyReadOnlyTools()
	{
		// Arrange: все непустые подмножества закрытого справочника.
		var all = ChatDataSourceCatalog.All;

		// Act + Assert
		for (var mask = 1; mask < (1 << all.Count); mask++)
		{
			var sources = Enumerable.Range(0, all.Count)
				.Where(index => (mask & (1 << index)) != 0)
				.Select(index => all[index])
				.ToList();
			var functions = CreateTools().CreateTools(sources);
			var selectedNames = string.Join(", ", sources);

			Assert.That(
				functions.Select(function => function.Name),
				Is.SubsetOf(new[]
				{
					ChatTools.ReadRuleCardToolName,
					ChatTools.GetMarketSnapshotToolName,
					ChatTools.GetOptionBoardToolName,
				}),
				$"Набор источников: {selectedNames}");
		}
	}

	[TestMethod]
	[Description("Карточка правила читается полным текстом по id, неизвестный id отвечает текстом об отсутствии")]
	public async Task ReadRuleCardAsync_ReturnsFullTextOrNotFoundMessage()
	{
		// Arrange: корпус знает карточку ac-01 и не знает ac-99.
		var corpus = new Mock<IRuleCorpusReader>(MockBehavior.Strict);
		corpus
			.Setup(reader => reader.ReadCardAsync("ac-01", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new RuleCardContent { Id = "ac-01", Text = "Полный текст правила ac-01." });
		corpus
			.Setup(reader => reader.ReadCardAsync("ac-99", It.IsAny<CancellationToken>()))
			.ReturnsAsync((RuleCardContent?)null);
		var tools = CreateTools(corpus.Object);

		// Act: читаем известную и неизвестную карточки.
		var known = await tools.ReadRuleCardAsync("ac-01");
		var unknown = await tools.ReadRuleCardAsync("ac-99");

		// Assert: полный текст отдаётся целиком, отсутствие — не ошибка, а текст.
		// Traceability: openspec:chats/sources#scenario-sources-card-by-id
		Assert.That(known, Is.EqualTo("Полный текст правила ac-01."));
		Assert.That(unknown, Does.Contain("ac-99"));
		Assert.That(unknown, Does.Contain("не найдена"));
	}

	[TestMethod]
	[Description("Чтение карточки правила оставляет ссылку на неё в следе источников: только выданные карточки, без повторов")]
	// Ссылка пишется только при фактической выдаче текста карточки:
	// несуществующий id данных правила не дал, повторное чтение одной
	// карточки даёт в следе одну запись.
	// Traceability: openspec:chats/history#scenario-chat-source-trace-persisted
	public async Task ReadRuleCardAsync_RecordsRuleCardReferenceInTrace()
	{
		// Arrange: корпус знает карточку ac-01 и не знает ac-99.
		var corpus = new Mock<IRuleCorpusReader>(MockBehavior.Strict);
		corpus
			.Setup(reader => reader.ReadCardAsync("ac-01", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new RuleCardContent { Id = "ac-01", Text = "Полный текст правила ac-01." });
		corpus
			.Setup(reader => reader.ReadCardAsync("ac-99", It.IsAny<CancellationToken>()))
			.ReturnsAsync((RuleCardContent?)null);
		var tools = CreateTools(corpus.Object);
		var traceRecorder = new ChatSourceTraceRecorder();

		// Act: читаем найденную, отсутствующую и снова найденную карточки.
		_ = await tools.ReadRuleCardAsync("ac-01", traceRecorder);
		_ = await tools.ReadRuleCardAsync("ac-99", traceRecorder);
		_ = await tools.ReadRuleCardAsync("ac-01", traceRecorder);

		// Assert: три вызова инструмента и одна ссылка на ac-01 — отсутствующая
		// карточка ссылки не дала, повторное чтение задедуплировано; корпус
		// правил — не рыночные данные, as-of у вызовов чтения нет.
		var trace = traceRecorder.Build();
		Assert.That(trace, Is.Not.Null);
		Assert.That(trace!.Invocations, Has.Count.EqualTo(3));
		Assert.That(trace.Invocations, Has.All.Matches<ChatToolInvocation>(invocation => invocation.DataAsOf is null));
		Assert.That(trace.RuleCards, Is.EqualTo(new[] { "ac-01" }));
	}

	[TestMethod]
	[Description("Снимок рынка рендерится markdown с маркой, фандингом и as-of отметкой")]
	public async Task GetMarketSnapshotAsync_RendersMarkdownSnapshot()
	{
		// Arrange: порт отвечает доступным снимком перпа BTCUSDT.
		var market = new Mock<IChatMarketReader>(MockBehavior.Strict);
		market
			.Setup(reader => reader.ReadSnapshotAsync("BTC", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ChatMarketSnapshot
			{
				BaseCoin = "BTC",
				AsOf = FixedNow,
				IsAvailable = true,
				Symbol = "BTCUSDT",
				MarkPrice = 108975.4m,
				Bid1Price = 108970m,
				Ask1Price = 108980m,
				OpenInterest = 74033.459m,
				FundingRate = 0.0001m,
			});
		var tools = CreateTools(market.Object);

		// Act: строим markdown-снимок.
		var markdown = await tools.GetMarketSnapshotAsync("BTC");

		// Assert: снимок содержит ключевые поля и as-of отметку, а не сырой JSON биржи.
		Assert.That(markdown, Does.Contain("Снимок фьючерсного рынка BTC"));
		Assert.That(markdown, Does.Contain("as-of: 2030-01-01 12:00:00 UTC"));
		Assert.That(markdown, Does.Contain("BTCUSDT"));
		Assert.That(markdown, Does.Contain("Марка: 108975.4 USDT"));
		Assert.That(markdown, Does.Contain("Ставка фандинга: 0.0001"));
		Assert.That(markdown, Does.Contain("Открытый интерес: 74033.459 BTC"));
	}

	[TestMethod]
	[Description("Доска опционов отдаётся компактной проекцией: окно ±20%, строка на страйк, отсечённые страйки объявлены")]
	public async Task GetOptionBoardAsync_RendersCompactProjection()
	{
		// Arrange: доска с двумя экспирациями; страйк 135000 лежит вне окна
		// ±20% от марки 106000 и не должен попасть в проекцию.
		var market = new Mock<IChatMarketReader>(MockBehavior.Strict);
		market
			.Setup(reader => reader.ReadOptionBoardAsync("BTC", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ChatOptionBoard
			{
				BaseCoin = "BTC",
				AsOf = FixedNow,
				IsAvailable = true,
				UnderlyingPrice = 106000m,
				TotalTickerCount = 9,
				Quotes =
				[
					Quote("BTC-26DEC25-95000-C", new DateOnly(2025, 12, 26), 95000m, OptionType.Call, mark: 5160.25m, iv: 0.4865m, delta: 0.6211m, openInterest: 4210m, bid: 5100.5m, ask: 5220m),
					Quote("BTC-26DEC25-95000-P", new DateOnly(2025, 12, 26), 95000m, OptionType.Put, mark: 626.10m, iv: 0.5260m, delta: -0.3701m, openInterest: 9120m, bid: 620m, ask: 632.5m),
					Quote("BTC-26DEC25-106000-C", new DateOnly(2025, 12, 26), 106000m, OptionType.Call, mark: 4215.75m, iv: 0.4598m, delta: 0.5044m, openInterest: 15340m, bid: 4180m, ask: 4255.5m),
					Quote("BTC-26DEC25-135000-C", new DateOnly(2025, 12, 26), 135000m, OptionType.Call, mark: 318.40m, iv: 0.6257m, delta: 0.1502m, openInterest: 820m, bid: 310m, ask: 325.5m),
					Quote("BTC-27MAR26-100000-C", new DateOnly(2026, 3, 27), 100000m, OptionType.Call, mark: 10035.60m, iv: 0.5064m, delta: 0.5801m, openInterest: 6600m, bid: 9950m, ask: 10120.5m),
					Quote("BTC-27MAR26-100000-P", new DateOnly(2026, 3, 27), 100000m, OptionType.Put, mark: 3855.15m, iv: 0.5458m, delta: -0.4122m, openInterest: 5110m, bid: 3810m, ask: 3905.5m),
				],
			});
		var tools = CreateTools(market.Object);

		// Act: строим markdown-проекцию доски.
		var markdown = await tools.GetOptionBoardAsync("BTC");

		// Assert: окно объявлено с якорем-маркой, счётчики сырых и проекционных
		// котировок разделены.
		// Traceability: openspec:chats/sources#scenario-sources-option-board-projection
		Assert.That(markdown, Does.Contain("Доска опционов BTC"));
		Assert.That(markdown, Does.Contain("Окно проекции: страйки ±20% от марки базового актива 106000 USDT"));
		Assert.That(markdown, Does.Contain("Инструментов в ответе биржи: 9, в проекции: 6"));

		// Assert: строка на страйк — колл и пут одного страйка в одной строке
		// с колонками IV, греков, открытого интереса и бид-аска.
		Assert.That(markdown, Does.Contain("| Страйк | Колл бид/аск | Колл IV | Колл Δ | Колл Γ | Колл Θ | Колл Vega | Колл OI | Пут бид/аск | Пут IV | Пут Δ | Пут Γ | Пут Θ | Пут Vega | Пут OI |"));
		Assert.That(markdown, Does.Contain("| 95000 | 5100.5/5220 | 48.65% | 0.6211 | 0.0000389 | -84.1088 | 161.2205 | 4210 | 620/632.5 | 52.6% | -0.3701 | 0.0000389 | -84.1088 | 161.2205 | 9120 |"));
		Assert.That(markdown, Does.Contain("| 106000 | 4180/4255.5 |"));
		Assert.That(markdown, Does.Contain("Экспирация 2025-12-26"));
		Assert.That(markdown, Does.Contain("Экспирация 2026-03-27"));

		// Assert: страйк вне окна не попал в проекцию, но отсечение объявлено в шапке
		// экспирации — сырые данные биржи инструмент не отдаёт.
		Assert.That(markdown, Does.Not.Contain("135000"));
		Assert.That(markdown, Does.Contain("Показаны 2 из 3 страйков экспирации"));
	}

	[TestMethod]
	[Description("Недоступность биржи отдаётся структурированным «недоступно» с причиной, а не исключением")]
	public async Task GetOptionBoardAsync_UnavailableBoard_ReturnsStructuredUnavailable()
	{
		// Arrange: порт отвечает недоступностью с причиной сбоя биржи.
		var market = new Mock<IChatMarketReader>(MockBehavior.Strict);
		market
			.Setup(reader => reader.ReadOptionBoardAsync("BTC", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ChatOptionBoard
			{
				BaseCoin = "BTC",
				AsOf = FixedNow,
				IsAvailable = false,
				UnavailableReason = "Bybit API ответил ошибкой: retCode=10001.",
				TotalTickerCount = 0,
				Quotes = [],
			});
		var tools = CreateTools(market.Object);

		// Act: строим ответ инструмента.
		var markdown = await tools.GetOptionBoardAsync("BTC");

		// Assert: ответ помечен недоступностью с причиной; кэш пуст — таблицы
		// проекции нет, инструмент объявляет отсутствие кэшированных марок.
		// Traceability: openspec:chats/sources#requirement-sources-degradation-cached-asof
		Assert.That(markdown, Does.Contain("биржа недоступна"));
		Assert.That(markdown, Does.Contain("Причина: Bybit API ответил ошибкой: retCode=10001."));
		Assert.That(markdown, Does.Contain("Кэшированных марок опционов BTC в кэше марок нет"));
		Assert.That(markdown, Does.Not.Contain("Страйк"));
	}

	[TestMethod]
	[Description("Недоступность биржи с кэшем деградирует снимок в последнюю кэшированную марку с её as-of")]
	public async Task GetMarketSnapshotAsync_UnavailableWithCache_RendersCachedMarkWithItsAsOf()
	{
		// Arrange: порт отвечает недоступностью с последней кэшированной маркой
		// перпа и as-of момента её получения из кэша марок.
		var cachedAt = new DateTimeOffset(2029, 12, 31, 10, 0, 0, TimeSpan.Zero);
		var market = new Mock<IChatMarketReader>(MockBehavior.Strict);
		market
			.Setup(reader => reader.ReadSnapshotAsync("BTC", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ChatMarketSnapshot
			{
				BaseCoin = "BTC",
				AsOf = cachedAt,
				IsAvailable = false,
				UnavailableReason = "Bybit API ответил ошибкой: retCode=10001.",
				Symbol = "BTCUSDT",
				MarkPrice = 106000m,
			});
		var tools = CreateTools(market.Object);

		// Act: строим ответ инструмента.
		var markdown = await tools.GetMarketSnapshotAsync("BTC");

		// Assert: недоступность с причиной и последняя кэшированная марка с as-of
		// кэша — ассистент обязан пометить устаревшие данные и не давать
		// рыночно-зависимых рекомендаций.
		// Traceability: openspec:chats/sources#scenario-sources-market-down-cached-projection
		Assert.That(markdown, Does.Contain("Снимок фьючерсного рынка BTC — биржа недоступна"));
		Assert.That(markdown, Does.Contain("Причина: Bybit API ответил ошибкой: retCode=10001."));
		Assert.That(markdown, Does.Contain("Последняя кэшированная марка (as-of: 2029-12-31 10:00:00 UTC):"));
		Assert.That(markdown, Does.Contain("Инструмент: BTCUSDT"));
		Assert.That(markdown, Does.Contain("Марка: 106000 USDT"));
	}

	[TestMethod]
	[Description("Недоступность биржи без кэшированной марки отвечает «кэш пуст» вместо проекции")]
	public async Task GetMarketSnapshotAsync_UnavailableWithoutCache_SaysNoCachedMark()
	{
		// Arrange: порт отвечает недоступностью без марки — кэш провайдера пуст.
		var market = new Mock<IChatMarketReader>(MockBehavior.Strict);
		market
			.Setup(reader => reader.ReadSnapshotAsync("BTC", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ChatMarketSnapshot
			{
				BaseCoin = "BTC",
				AsOf = FixedNow,
				IsAvailable = false,
				UnavailableReason = "Bybit API ответил ошибкой: retCode=10001.",
			});
		var tools = CreateTools(market.Object);

		// Act: строим ответ инструмента.
		var markdown = await tools.GetMarketSnapshotAsync("BTC");

		// Assert: деградировать нечем — инструмент объявляет отсутствие кэшированной марки.
		// Traceability: openspec:chats/sources#scenario-sources-market-down-cached-projection
		Assert.That(markdown, Does.Contain("биржа недоступна"));
		Assert.That(markdown, Does.Contain("Кэшированной марки BTCUSDT в кэше марок нет"));
	}

	[TestMethod]
	[Description("Недоступность биржи с кэшем деградирует доску в таблицу кэшированных марок с её as-of")]
	public async Task GetOptionBoardAsync_UnavailableWithCache_RendersCachedMarksTableWithItsAsOf()
	{
		// Arrange: порт отвечает недоступностью с кэшированными марками двух
		// опционов и якорем из кэшированной марки перпа.
		var cachedAt = new DateTimeOffset(2029, 12, 31, 10, 0, 0, TimeSpan.Zero);
		var market = new Mock<IChatMarketReader>(MockBehavior.Strict);
		market
			.Setup(reader => reader.ReadOptionBoardAsync("BTC", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ChatOptionBoard
			{
				BaseCoin = "BTC",
				AsOf = cachedAt,
				IsAvailable = false,
				UnavailableReason = "Bybit API ответил ошибкой: retCode=10001.",
				UnderlyingPrice = 106000m,
				TotalTickerCount = 2,
				Quotes =
				[
					Quote("BTC-26DEC25-95000-C", new DateOnly(2025, 12, 26), 95000m, OptionType.Call, mark: 5160.25m),
					Quote("BTC-26DEC25-95000-P", new DateOnly(2025, 12, 26), 95000m, OptionType.Put, mark: 626.10m),
				],
			});
		var tools = CreateTools(market.Object);

		// Act: строим ответ инструмента.
		var markdown = await tools.GetOptionBoardAsync("BTC");

		// Assert: недоступность с причиной и кэшированная проекция марок с as-of
		// кэша; кэш хранит только марки — таблица деградации без IV, греков и
		// бид-аска; сырых данных биржи в ответе нет.
		// Traceability: openspec:chats/sources#scenario-sources-market-down-cached-projection
		Assert.That(markdown, Does.Contain("Доска опционов BTC — биржа недоступна"));
		Assert.That(markdown, Does.Contain("Причина: Bybit API ответил ошибкой: retCode=10001."));
		Assert.That(markdown, Does.Contain("Последняя кэшированная проекция марок (as-of: 2029-12-31 10:00:00 UTC):"));
		Assert.That(markdown, Does.Contain("Окно проекции: страйки ±20% от последней кэшированной марки базового актива 106000 USDT"));
		Assert.That(markdown, Does.Contain("Котировок в кэше: 2"));
		Assert.That(markdown, Does.Contain("| Страйк | Колл марка | Пут марка |"));
		Assert.That(markdown, Does.Contain("| 95000 | 5160.25 | 626.1 |"));
		Assert.That(markdown, Does.Contain("Экспирация 2025-12-26"));
		Assert.That(markdown, Does.Not.Contain("Колл IV"));
	}

	[TestMethod]
	[Description("Один вызов инструмента выполняет ровно одно чтение порта рыночных данных")]
	public async Task GetMarketSnapshotAsync_SingleCall_PerformsSinglePortRead()
	{
		// Arrange: порт отвечает доступным снимком.
		var market = new Mock<IChatMarketReader>(MockBehavior.Strict);
		market
			.Setup(reader => reader.ReadSnapshotAsync("BTC", It.IsAny<CancellationToken>()))
			.ReturnsAsync(new ChatMarketSnapshot
			{
				BaseCoin = "BTC",
				AsOf = FixedNow,
				IsAvailable = true,
				Symbol = "BTCUSDT",
				MarkPrice = 108975.4m,
			});
		var tools = CreateTools(market.Object);

		// Act: один вызов инструмента.
		await tools.GetMarketSnapshotAsync("BTC");

		// Assert: чтений порта ровно одно — «один вызов = один биржевой запрос»;
		// потолок числа вызовов на запрос ассистента ограничивает агентный цикл (3.3).
		// Traceability: openspec:chats/sources#requirement-sources-single-request-per-call
		market.Verify(
			reader => reader.ReadSnapshotAsync("BTC", It.IsAny<CancellationToken>()),
			Times.Once);
		market.VerifyNoOtherCalls();
	}

	/// <summary>Создаёт реестр поверх заглушек портов по умолчанию.</summary>
	private static ChatTools CreateTools()
	{
		var corpus = new Mock<IRuleCorpusReader>(MockBehavior.Loose);
		return CreateTools(corpus.Object);
	}

	/// <summary>Создаёт реестр поверх переданного читателя корпуса и заглушки рынка.</summary>
	private static ChatTools CreateTools(IRuleCorpusReader corpusReader)
	{
		var market = new Mock<IChatMarketReader>(MockBehavior.Loose);
		return new ChatTools(corpusReader, market.Object);
	}

	/// <summary>Создаёт реестр поверх читателя корпуса по умолчанию и переданного порта рынка.</summary>
	private static ChatTools CreateTools(IChatMarketReader marketReader)
	{
		var corpus = new Mock<IRuleCorpusReader>(MockBehavior.Loose);
		return new ChatTools(corpus.Object, marketReader);
	}

	/// <summary>Строит котировку доски с типовыми значениями греков.</summary>
	private static ChatOptionQuote Quote(
		string symbol,
		DateOnly expiry,
		decimal strike,
		OptionType type,
		decimal? mark = null,
		decimal? iv = null,
		decimal? delta = null,
		decimal? openInterest = null,
		decimal? bid = null,
		decimal? ask = null) => new()
	{
		Symbol = symbol,
		Expiry = expiry,
		Strike = strike,
		Type = type,
		MarkPrice = mark,
		MarkIv = iv,
		Delta = delta,
		Gamma = delta is null ? null : 0.0000389m,
		Vega = delta is null ? null : 161.2205m,
		Theta = delta is null ? null : -84.1088m,
		OpenInterest = openInterest,
		Bid1Price = bid,
		Bid1Size = bid is null ? null : 3m,
		Ask1Price = ask,
		Ask1Size = ask is null ? null : 2m,
	};
}
