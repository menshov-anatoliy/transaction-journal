namespace TransactionJournal.Tests.Infrastructure.Chats;

using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Chats;
using TransactionJournal.Chats.Ports;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Chats;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Infrastructure.Hints;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using TransactionJournal.Hints.Ports;
using TransactionJournal.Tests;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Интеграционные проверки адаптера снимка контекста чата: снимок
/// собирается кодом поверх read-моделей журнала и несёт первичные факты
/// конструкции, портфельные агрегаты, лимиты и компактный индекс корпуса,
/// каждый раздел датирован as-of моментом сборки, живые подсказки движка
/// в снимок не попадают, а полный текст карточек в снимке не рендерится.
/// Traceability: openspec:chats/context#requirement-chat-context-deterministic-snapshot
/// </summary>
[TestClass]
public class ChatContextReaderTests
{
	private const string LinearSymbol = "BTCUSDT";

	private const string CallSymbol = "BTC-29DEC23-45000-C";

	/// <summary>Момент сборки снимка: фальшивые часы адаптера возвращают только его.</summary>
	private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	/// <summary>Каноническое время delivery инструмента опциона из справочника: 29DEC23 08:00 UTC.</summary>
	private static readonly long OptionDeliveryMs = new DateTimeOffset(2023, 12, 29, 8, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	/// <summary>Строка as-of отметки, ожидаемая в заголовке каждого раздела.</summary>
	private const string AsOfStamp = "2026-10-04 12:00:00 UTC";

	private string _databasePath = null!;

	private ConstructionService _constructionService = null!;

	private TradeBindingService _bindingService = null!;

	private CommentService _commentService = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке
		// со справочником линейного перпа BTCUSDT.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-chat-context-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
			SeedInstrumentCatalog(db);
		}

		_constructionService = new ConstructionService(CreateOptions());
		_bindingService = new TradeBindingService(CreateOptions());
		_commentService = new CommentService(CreateOptions());
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
	[Description("Снимок несёт первичные факты конструкции, портфельные агрегаты, лимиты и индекс корпуса с as-of каждого раздела")]
	// Проверяем сценарий первичных фактов: конструкция, позиции, результат,
	// портфельные агрегаты и лимиты присутствуют в markdown-снимке, каждый
	// раздел помечен одной as-of отметкой сборки.
	// Traceability: openspec:chats/context#scenario-chat-context-construction-snapshot-with-asof
	public async Task TryIfSnapshotCarriesPrimaryFactsWithPerSectionAsOf()
	{
		// Arrange: конструкция с капиталом 1000, риском 5% и профитом 200 USDT;
		// покупка 0.1 BTC по 42000 с комиссией 1 привязана к конструкции, у позиции
		// оставлен комментарий; свежая марка провайдера — 44000 на 2026-09-20 12:00.
		var construction = await _constructionService.CreateAsync("Календарь сентябрь", 1000m);
		await _constructionService.UpdateRiskAsync(construction.Id, 5m, TargetUnit.Percent);
		await _constructionService.UpdateProfitAsync(construction.Id, 200m, TargetUnit.Usdt);
		await _commentService.SetConstructionCommentAsync(construction.Id, "тестовая конструкция");
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await _bindingService.BindAsync(construction.Id, "exec-buy");
		await _commentService.SetPositionCommentAsync(construction.Id, LinearSymbol, "базовая позиция");
		var receivedAt = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);
		var reader = CreateReader(new StubFreshMarkSource(44000m, receivedAt), new StubRuleCorpus());

		// Act: собираем снимок контекста конструкции.
		var snapshot = await reader.ReadAsync(construction.Id);
		var markdown = snapshot.Markdown;

		// Assert: заголовок и все четыре раздела датированы одним as-of моментом,
		// конструкция описана первичными фактами с позициями и результатом.
		Assert.That(snapshot.AsOf, Is.EqualTo(Now));
		Assert.That(snapshot.IsConstructionClosed, Is.False);
		Assert.That(markdown, Does.Contain($"# Снимок контекста чата (as-of: {AsOfStamp})"));
		Assert.That(markdown, Does.Contain($"## Конструкция «Календарь сентябрь» (as-of: {AsOfStamp})"));
		Assert.That(markdown, Does.Contain("- Идентификатор: 1"));
		Assert.That(markdown, Does.Contain("- Статус: открыта"));
		Assert.That(markdown, Does.Contain("- Выделенный капитал: 1000 USDT"));
		Assert.That(markdown, Does.Contain("- Комментарий владельца: тестовая конструкция"));
		Assert.That(markdown, Does.Contain("- Открыта: 2023-12-28 10:00:00 UTC"));
		Assert.That(markdown, Does.Contain("| BTCUSDT | 0.1 | открыта | 42000 | — | -1 | 200 | 199 | 1 |"));
		Assert.That(markdown, Does.Contain("  - BTCUSDT: базовая позиция"));
		Assert.That(markdown, Does.Contain("итог 199 USDT (19.9% капитала)"));
		Assert.That(markdown, Does.Contain("- Марки оценки: 2026-09-20 12:00:00 UTC"));
		Assert.That(markdown, Does.Contain($"## Портфельные агрегаты (as-of: {AsOfStamp})"));
		Assert.That(markdown, Does.Contain("- Конструкций в журнале: 1, из них с открытыми позициями: 1"));
		Assert.That(markdown, Does.Contain("- Реализованный PnL журнала: -1 USDT"));
		Assert.That(markdown, Does.Contain("- Нереализованный PnL журнала: 200 USDT"));
		Assert.That(markdown, Does.Contain($"## Лимиты конструкции (as-of: {AsOfStamp})"));
		Assert.That(markdown, Does.Contain("- Риск: 5% капитала = 50 USDT"));
		Assert.That(markdown, Does.Contain("- Профит: 20% капитала = 200 USDT"));
		Assert.That(markdown, Does.Contain($"## Индекс корпуса правил (as-of: {AsOfStamp})"));
		Assert.That(markdown, Does.Contain("- ac-01 — Лимиты риска на период — Убыток недели против лимита 1%. (статус: active)"));
		Assert.That(markdown, Does.Contain("- ac-19 — Лотерейные опционы — Избыток прибыли в дешёвые опционы. (статус: retired)"));
		Assert.That(markdown, Does.Contain("read_rule_card"));
	}

	[TestMethod]
	[Description("Снимок чата без привязки к конструкции несёт портфельный уровень: агрегаты, лимиты журнала и индекс корпуса без раздела конструкции")]
	// Проверяем сценарий портфельного снимка: ReadAsync без привязки отдаёт
	// агрегаты журнала, периодные лимиты риска из карточки ac-01 с денежным
	// масштабом от совокупного капитала и индекс корпуса — раздела конструкции
	// и её лимитов в markdown нет, пост-мортем не выбран.
	// Traceability: openspec:chats/context#scenario-chat-context-portfolio-snapshot-without-construction
	public async Task TryIfUnboundChatSnapshotCarriesPortfolioLevelWithoutConstructionSection()
	{
		// Arrange: журнал с одной конструкцией (капитал 1000) и позицией
		// с реализованным и нереализованным результатом; корпус с порогами
		// периодных лимитов риска карточки ac-01 — 1/5/10% капитала.
		var construction = await _constructionService.CreateAsync("Календарь сентябрь", 1000m);
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await _bindingService.BindAsync(construction.Id, "exec-buy");
		var reader = CreateReader(new StubFreshMarkSource(44000m, FetchedAt), new StubRuleCorpus(
		[
			new RuleCardThreshold { Name = "weeklyRiskLimit", Value = "1", Unit = "percent" },
			new RuleCardThreshold { Name = "monthlyRiskLimit", Value = "5", Unit = "percent" },
			new RuleCardThreshold { Name = "quarterlyRiskLimit", Value = "10", Unit = "percent" },
		]));

		// Act: собираем снимок контекста чата без привязки к конструкции.
		var snapshot = await reader.ReadAsync(null);
		var markdown = snapshot.Markdown;

		// Assert: портфельный снимок датирован моментом сборки и несёт
		// агрегаты, лимиты журнала и индекс корпуса — разделов конструкции нет.
		Assert.That(snapshot.AsOf, Is.EqualTo(Now));
		Assert.That(snapshot.IsConstructionClosed, Is.False);
		Assert.That(markdown, Does.Contain($"# Снимок контекста чата (as-of: {AsOfStamp})"));
		Assert.That(markdown, Does.Contain($"## Портфельные агрегаты (as-of: {AsOfStamp})"));
		Assert.That(markdown, Does.Contain("- Конструкций в журнале: 1, из них с открытыми позициями: 1"));
		Assert.That(markdown, Does.Contain("- Реализованный PnL журнала: -1 USDT"));
		Assert.That(markdown, Does.Contain("- Нереализованный PnL журнала: 200 USDT"));
		Assert.That(markdown, Does.Contain($"## Лимиты журнала (as-of: {AsOfStamp})"));
		Assert.That(markdown, Does.Contain("- Совокупный выделенный капитал: 1000 USDT"));
		Assert.That(markdown, Does.Contain("- Лимит убытка на неделю: 1% капитала (= 10 USDT)"));
		Assert.That(markdown, Does.Contain("- Лимит убытка на месяц: 5% капитала (= 50 USDT)"));
		Assert.That(markdown, Does.Contain("- Лимит убытка на квартал: 10% капитала (= 100 USDT)"));
		Assert.That(markdown, Does.Contain($"## Индекс корпуса правил (as-of: {AsOfStamp})"));
		Assert.That(markdown, Does.Contain("read_rule_card"));
		Assert.That(markdown, Does.Not.Contain("## Конструкция «"));
		Assert.That(markdown, Does.Not.Contain("## Лимиты конструкции"));
	}

	[TestMethod]
	[Description("Подмножество источников только с рынком Bybit оставляет в снимке одну шапку")]
	// Набор источников чата режет состав снимка: без журнала и корпуса правил
	// в промпте нет ни конструкции, ни агрегатов, ни лимитов, ни индекса
	// корпуса — только шапка снимка.
	// Traceability: openspec:chats/sources#scenario-sources-subset-parameter
	public async Task TryIfOnlyMarketSourceSelected_SnapshotKeepsHeaderOnly()
	{
		// Arrange: конструкция с привязанной сделкой и заглушка корпуса.
		var construction = await _constructionService.CreateAsync("Календарь сентябрь", 1000m);
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await _bindingService.BindAsync(construction.Id, "exec-buy");
		var reader = CreateReader(new StubFreshMarkSource(44000m, FetchedAt), new StubRuleCorpus());

		// Act: снимок по набору источников чата — только рынок Bybit.
		var snapshot = await reader.ReadAsync(construction.Id, [ChatDataSource.BybitMarket]);

		// Assert: в снимке осталась только шапка, журнальные и корпусные
		// разделы отсечены.
		Assert.That(snapshot.Markdown, Does.Contain($"# Снимок контекста чата (as-of: {AsOfStamp})"));
		Assert.That(snapshot.Markdown, Does.Not.Contain("## Конструкция «"));
		Assert.That(snapshot.Markdown, Does.Not.Contain("## Портфельные агрегаты"));
		Assert.That(snapshot.Markdown, Does.Not.Contain("## Лимиты"));
		Assert.That(snapshot.Markdown, Does.Not.Contain("## Индекс корпуса правил"));
		Assert.That(snapshot.Markdown, Does.Not.Contain("read_rule_card"));
	}

	[TestMethod]
	[Description("Подмножество источников только с журналом даёт журнальные разделы конструкции без индекса корпуса")]
	// Источник «корпус правил» не выбран — карточный индекс в снимок не
	// попадает; журнальные разделы конструкции, агрегатов и лимитов остаются.
	// Traceability: openspec:chats/sources#scenario-sources-subset-parameter
	public async Task TryIfOnlyJournalSourceSelected_SnapshotKeepsJournalSectionsWithoutRuleIndex()
	{
		// Arrange: конструкция с привязанной сделкой и заглушка корпуса.
		var construction = await _constructionService.CreateAsync("Календарь сентябрь", 1000m);
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await _bindingService.BindAsync(construction.Id, "exec-buy");
		var reader = CreateReader(new StubFreshMarkSource(44000m, FetchedAt), new StubRuleCorpus());

		// Act: снимок по набору источников чата — только журнал.
		var snapshot = await reader.ReadAsync(construction.Id, [ChatDataSource.Journal]);

		// Assert: журнальные разделы на месте, корпусных нет.
		Assert.That(snapshot.Markdown, Does.Contain("## Конструкция «"));
		Assert.That(snapshot.Markdown, Does.Contain("## Портфельные агрегаты"));
		Assert.That(snapshot.Markdown, Does.Contain("## Лимиты конструкции"));
		Assert.That(snapshot.Markdown, Does.Not.Contain("## Индекс корпуса правил"));
		Assert.That(snapshot.Markdown, Does.Not.Contain("read_rule_card"));
	}

	[TestMethod]
	[Description("Чат без привязки с одним источником «журнал» получает агрегаты и лимиты журнала без индекса корпуса")]
	// На портфельном уровне набор источников режет только корпусный раздел:
	// журнальные агрегаты и лимиты остаются, индекс корпуса отсечён.
	// Traceability: openspec:chats/sources#scenario-sources-subset-parameter
	public async Task TryIfUnboundChatWithOnlyJournalSource_KeepsPortfolioSectionsWithoutRuleIndex()
	{
		// Arrange: журнал с конструкцией и позицией; корпус с недельным
		// порогом лимита риска.
		var construction = await _constructionService.CreateAsync("Календарь сентябрь", 1000m);
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await _bindingService.BindAsync(construction.Id, "exec-buy");
		var reader = CreateReader(new StubFreshMarkSource(44000m, FetchedAt), new StubRuleCorpus(
		[
			new RuleCardThreshold { Name = "weeklyRiskLimit", Value = "1", Unit = "percent" },
		]));

		// Act: снимок чата без привязки по набору источников — только журнал.
		var snapshot = await reader.ReadAsync(null, [ChatDataSource.Journal]);

		// Assert: агрегаты и лимиты журнала на месте, корпусных разделов нет.
		Assert.That(snapshot.Markdown, Does.Contain("## Портфельные агрегаты"));
		Assert.That(snapshot.Markdown, Does.Contain("## Лимиты журнала"));
		Assert.That(snapshot.Markdown, Does.Not.Contain("## Конструкция «"));
		Assert.That(snapshot.Markdown, Does.Not.Contain("## Индекс корпуса правил"));
		Assert.That(snapshot.Markdown, Does.Not.Contain("read_rule_card"));
	}

	[TestMethod]
	[Description("Чат без привязки с одним источником «корпус правил» получает только индекс корпуса")]
	// Без источника «журнал» журнальные разделы отсечены даже у портфельного
	// снимка: остаётся шапка и индекс корпуса правил.
	// Traceability: openspec:chats/sources#scenario-sources-subset-parameter
	public async Task TryIfUnboundChatWithOnlyRuleCorpusSource_KeepsRuleIndexWithoutJournalSections()
	{
		// Arrange: журнал с одной конструкцией, заглушка корпуса.
		await _constructionService.CreateAsync("Календарь сентябрь", 1000m);
		var reader = CreateReader(new StubFreshMarkSource(44000m, FetchedAt), new StubRuleCorpus());

		// Act: снимок чата без привязки по набору источников — только корпус.
		var snapshot = await reader.ReadAsync(null, [ChatDataSource.RulesCorpus]);

		// Assert: только шапка и индекс корпуса; журнальных разделов нет.
		Assert.That(snapshot.Markdown, Does.Contain("## Индекс корпуса правил"));
		Assert.That(snapshot.Markdown, Does.Contain("ac-01 — Лимиты риска на период"));
		Assert.That(snapshot.Markdown, Does.Not.Contain("## Портфельные агрегаты"));
		Assert.That(snapshot.Markdown, Does.Not.Contain("## Лимиты журнала"));
		Assert.That(snapshot.Markdown, Does.Not.Contain("## Конструкция «"));
	}

	[TestMethod]
	[Description("Отсутствие порогов лимитов в корпусе не ломает портфельный снимок")]
	// Проверяем деградацию раздела лимитов журнала: без карточки порогов
	// снимок всё равно собирается — агрегаты и индекс присутствуют, раздел
	// лимитов журнала деградирует явным текстом.
	// Traceability: openspec:chats/context#scenario-chat-context-portfolio-snapshot-without-construction
	public async Task TryIfMissingJournalLimitsKeepPortfolioSnapshotUsable()
	{
		// Arrange: журнал с одной конструкцией и пустым набором порогов корпуса.
		await _constructionService.CreateAsync("Календарь сентябрь", 1000m);
		var reader = CreateReader(new StubFreshMarkSource(44000m, FetchedAt), new StubRuleCorpus());

		// Act: собираем портфельный снимок без карточки лимитов.
		var snapshot = await reader.ReadAsync(null);

		// Assert: снимок пригоден — агрегаты и индекс на месте, лимиты
		// журнала деградировали явным текстом.
		Assert.That(snapshot.Markdown, Does.Contain($"## Лимиты журнала (as-of: {AsOfStamp})"));
		Assert.That(snapshot.Markdown, Does.Contain("- Совокупный выделенный капитал: 1000 USDT"));
		Assert.That(snapshot.Markdown, Does.Contain("- Периодные лимиты риска в корпусе не заданы"));
		Assert.That(snapshot.Markdown, Does.Contain("## Портфельные агрегаты"));
		Assert.That(snapshot.Markdown, Does.Contain("## Индекс корпуса правил"));
	}

	[TestMethod]
	[Description("Живые подсказки движка не попадают в снимок, даже когда они есть в хранилище")]
	// Проверяем сценарий исключения подсказок: в хранилище есть живая
	// new-подсказка по конструкции, но снимок собран без обращения к подсказкам —
	// ни текста, ни самого слова «подсказка» в markdown нет.
	// Traceability: openspec:chats/context#scenario-chat-context-hints-excluded
	public async Task TryIfLiveHintsStayOutOfSnapshot()
	{
		// Arrange: конструкция и живая подсказка движка по ней в хранилище.
		var construction = await _constructionService.CreateAsync("Стерддл декабрь", 1000m);
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await _bindingService.BindAsync(construction.Id, "exec-buy");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Hints.Add(new HintEntity
			{
				RuleId = "ac-01",
				SubjectKind = "конструкция",
				SubjectConstructionId = construction.Id,
				Character = "risk-mode",
				Clarity = "crisp",
				SourcesJson = "[]",
				Text = "ЖИВАЯ ПОДСКАЗКА ДВИЖКА: остановите наращивание риска",
				FactsJson = "{}",
				AsOf = FetchedAt,
				Status = HintStatus.New.ToString(),
			});
			await db.SaveChangesAsync();
		}

		var reader = CreateReader(new StubFreshMarkSource(44000m, FetchedAt), new StubRuleCorpus());

		// Act: собираем снимок контекста конструкции.
		var snapshot = await reader.ReadAsync(construction.Id);

		// Assert: текст живой подсказки и само слово «подсказка» в снимке отсутствуют.
		Assert.That(snapshot.Markdown, Does.Not.Contain("ЖИВАЯ ПОДСКАЗКА"));
		Assert.That(snapshot.Markdown, Does.Not.Contain("подсказк").IgnoreCase);
	}

	[TestMethod]
	[Description("Индекс корпуса остаётся компактным: полный текст карточек в снимок не рендерится")]
	// Проверяем сценарий «только карточный индекс»: снимок берёт у читателя
	// корпуса лишь индекс, чтение полного текста карточки не выполняется вовсе —
	// заглушка читателя падает при любом обращении к ReadCardAsync.
	// Traceability: openspec:chats/context#scenario-chat-context-card-index-only
	public async Task TryIfRuleIndexStaysCompactWithoutFullText()
	{
		// Arrange: конструкция с капиталом и заглушка корпуса с запретом полного текста.
		var construction = await _constructionService.CreateAsync("Календарь сентябрь", 1000m);
		var reader = CreateReader(new StubFreshMarkSource(44000m, FetchedAt), new StubRuleCorpus());

		// Act: собираем снимок контекста конструкции.
		var snapshot = await reader.ReadAsync(construction.Id);

		// Assert: индекс присутствует, а полнотекстовые разделы карточки
		// («Триггер:», «Источники:») в снимке не рендерятся.
		Assert.That(snapshot.Markdown, Does.Contain("ac-01 — Лимиты риска на период"));
		Assert.That(snapshot.Markdown, Does.Not.Contain("Триггер:"));
		Assert.That(snapshot.Markdown, Does.Not.Contain("Источники:"));
	}

	[TestMethod]
	[Description("Закрытая конструкция помечает снимок режимом пост-мортема")]
	// Проверяем признак пост-мортема: конструкция с ручным статусом «закрыта»
	// даёт снимок с IsConstructionClosed = true, построенный по финальному состоянию.
	// Traceability: openspec:chats/context#requirement-chat-context-postmortem-mode
	public async Task TryIfClosedConstructionSwitchesSnapshotToPostmortem()
	{
		// Arrange: конструкция с позицией, переведённая в статус «закрыта».
		var construction = await _constructionService.CreateAsync("Календарь сентябрь", 1000m);
		await AddLinearTradeAsync("exec-buy", "Buy", "0.1", "42000", "1", ExecMs(2023, 12, 28, 10, 0));
		await _bindingService.BindAsync(construction.Id, "exec-buy");
		await _constructionService.ChangeStatusAsync(construction.Id, ConstructionStatus.Closed);
		var reader = CreateReader(new StubFreshMarkSource(44000m, FetchedAt), new StubRuleCorpus());

		// Act: собираем снимок контекста закрытой конструкции.
		var snapshot = await reader.ReadAsync(construction.Id);

		// Assert: признак пост-мортема выставлен, статус в тексте — «закрыта».
		Assert.That(snapshot.IsConstructionClosed, Is.True);
		Assert.That(snapshot.Markdown, Does.Contain("- Статус: закрыта"));
	}

	#region Помощники

	/// <summary>Собирает адаптер снимка над реальными read-моделями и заглушкой корпуса.</summary>
	private ChatContextReader CreateReader(IFreshInstrumentMarkSource freshMarkSource, IRuleCorpusReader corpus)
	{
		var options = CreateOptions();
		return new ChatContextReader(
			new ConstructionDetailReadModel(options, new JournalMetricsReadModel(options, freshMarkSource), new PositionReadModel(options)),
			new JournalMetricsReadModel(options, freshMarkSource),
			corpus,
			new FixedTimeProvider(Now));
	}

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Справочник инструментов: опцион BTC с delivery 29DEC23 08:00 UTC и линейный перп BTCUSDT.</summary>
	private static void SeedInstrumentCatalog(JournalDbContext db)
	{
		var optionPayload =
			$$"""{"symbol":"{{CallSymbol}}","baseCoin":"BTC","quoteCoin":"USD","settleCoin":"USDC","status":"Trading","optionsType":"Call","deliveryTime":"{{OptionDeliveryMs}}","deliveryFeeRate":"0.00015"}""";
		var linearPayload =
			"""{"symbol":"BTCUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"BTC","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""";
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = CallSymbol,
			Category = "option",
			PayloadJson = optionPayload,
			FetchedAt = FetchedAt,
		});
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = LinearSymbol,
			Category = "linear",
			PayloadJson = linearPayload,
			FetchedAt = FetchedAt,
		});
		db.SaveChanges();
	}

	/// <summary>Добавляет сырую запись исполнения линейного перпа BTCUSDT; числа биржа шлёт строками.</summary>
	private async Task AddLinearTradeAsync(string execId, string side, string execQty, string execPrice, string execFee, long execTimeMs)
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawExecutions.Add(new RawExecution
		{
			ExecId = execId,
			Category = "linear",
			Symbol = LinearSymbol,
			ExecTimeMs = execTimeMs,
			PayloadJson = $$"""{"symbol":"{{LinearSymbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"{{execFee}}","execId":"{{execId}}","execPrice":"{{execPrice}}","execQty":"{{execQty}}","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":"USDT","isMaker":false}""",
			FetchedAt = FetchedAt,
		});
		await db.SaveChangesAsync();
	}

	private static long ExecMs(int year, int month, int day, int hour, int minute) =>
		new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	/// <summary>Заглушка свежей марки: всегда отдаёт одну и ту же цену с фиксированным временем получения.</summary>
	private sealed class StubFreshMarkSource(decimal markPrice, DateTimeOffset receivedAt) : IFreshInstrumentMarkSource
	{
		public Task<InstrumentMarkSnapshot?> GetFreshMarkAsync(string symbol, CancellationToken cancellationToken = default) =>
			Task.FromResult<InstrumentMarkSnapshot?>(new InstrumentMarkSnapshot(symbol, markPrice, receivedAt));
	}

	/// <summary>
	/// Заглушка читателя корпуса: индекс из двух карточек — активной и выведенной;
	/// чтение полного текста в снимке запрещено — заглушка падает при обращении;
	/// пороги отдаются только при заданном наборе, пустой список — порогов нет.
	/// </summary>
	private sealed class StubRuleCorpus(IReadOnlyList<RuleCardThreshold>? thresholds = null) : IRuleCorpusReader
	{
		public Task<IReadOnlyList<RuleCardSummary>> ListIndexAsync(CancellationToken cancellationToken = default) =>
			Task.FromResult<IReadOnlyList<RuleCardSummary>>(
			[
				new RuleCardSummary { Id = "ac-01", Title = "Лимиты риска на период", Summary = "Убыток недели против лимита 1%.", Status = "active" },
				new RuleCardSummary { Id = "ac-19", Title = "Лотерейные опционы", Summary = "Избыток прибыли в дешёвые опционы.", Status = "retired" },
			]);

		public Task<RuleCardContent?> ReadCardAsync(string cardId, CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("Снимок контекста не должен читать полный текст карточек");

		public Task<IReadOnlyList<RuleCardThreshold>> ReadCardThresholdsAsync(string cardId, CancellationToken cancellationToken = default) =>
			Task.FromResult(thresholds ?? (IReadOnlyList<RuleCardThreshold>)[]);
	}

	#endregion
}
