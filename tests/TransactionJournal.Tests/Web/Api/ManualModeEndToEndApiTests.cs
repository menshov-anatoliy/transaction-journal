namespace TransactionJournal.Tests.Web.Api;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Сквозной сценарий ручного режима журнала на чистой базе без Bybit-ключей и
/// без сети: конструкция и сделки внесены вручную — во «Входящие» и сразу в
/// конструкцию, инкрементальная сборка из «Входящих» не трогает ручные данные,
/// экспирация закрывается ручными пометками с ценой внутренней стоимости
/// (частичной и полной), синтетическая связка создаётся вручную, а позиции,
/// FIFO, реализованный результат и индикатор финрезультата считаются из
/// ручного сырья теми же формулами, что и из биржевого. Фабрика поднимает
/// реальный хост Program на временной базе SQLite; публичный канал тикеров
/// подменён стабильными котировками — обращения к сети в прогоне отсутствуют.
/// </summary>
[TestClass]
public sealed class ManualModeEndToEndApiTests
{
	/// <summary>Линейный перп кейса: базовая монета XAUT.</summary>
	private const string PerpSymbol = "XAUTUSDT";

	/// <summary>Пут доски 01DEC28 со страйком 4400 — закрываемая нога связки.</summary>
	private const string PutSymbol = "XAUT-01DEC28-4400-P";

	/// <summary>Колл той же доски — встречный опцион ручной связки.</summary>
	private const string CallSymbol = "XAUT-01DEC28-4400-C";

	/// <summary>Момент получения котировок стаба — фиксированный для детерминизма.</summary>
	private static readonly DateTimeOffset QuotesReceivedAt = new(2028, 11, 20, 9, 0, 0, TimeSpan.Zero);

	/// <summary>
	/// Каноническое время доставки доски — 08:00 UTC 01.12.2028; пометки закрытия
	/// датированы этим моментом: валидация «дата не в будущем» у сделок требует
	/// прошлого, а авто-закрытие OTM не наступившей доставки не выводит —
	/// остатки к ней уже обнулены пометками.
	/// </summary>
	private static readonly DateTimeOffset ExpiryAt = new(2028, 12, 1, 8, 0, 0, TimeSpan.Zero);

	/// <summary>Расчётная цена базового актива: пут 4400 в деньгах на внутреннюю стоимость 100.</summary>
	private const decimal UnderlyingAtExpiry = 4300m;

	[TestMethod]
	[Description("Сквозной ручной режим: ручные конструкции и сделки, закрытие экспирации пометками с внутренней стоимостью, ручная связка, метрики и индикатор финрезультата считаются")]
	// Сценарий п. 7.1 change manual-journal-management: на чистой базе без
	// Bybit-ключей ручные сделки вносятся во «Входящие» и сразу в конструкцию,
	// инкрементальная сборка не трогает ручные данные, экспирация закрывается
	// пометками с ценой внутренней стоимости (частично и полностью), синтетическая
	// связка создаётся вручную; метрики (позиции, FIFO, реализованный результат)
	// и индикатор финрезультата вычисляются из ручного сырья.
	// Traceability: openspec:domain/manual-trades#requirement-manual-trade-full-member
	// Traceability: openspec:domain/constructions#requirement-position-close-lifecycle
	// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
	public async Task TryIfManualModeLifecycleOnCleanDatabaseComputesMetricsAndIndicators()
	{
		// Arrange: хост на чистой базе; справочник сеется напрямую — известные
		// символы разрешаются без сети, ключи биржи не заданы; источник котировок
		// подменён стабильными величинами, чтобы прогон не выходил в сеть.
		await using var factory = new SectionApiFactory(services =>
		{
			var quotes = new ManualModeQuoteSource();
			services.ReplaceFreshInstrumentQuoteSource(quotes);
			services.ReplaceInstrumentQuoteSource(quotes);
		});
		using var client = factory.CreateClient();
		await MigrateAsync(factory);
		SeedInstruments(factory.DatabasePath);
		var constructionId = await CreateConstructionAsync(factory.DatabasePath, "Ручной стрэддл XAUT");

		// Act: ручные сделки вносятся обоими путями — сразу в конструкцию
		// (стрэддл 0.5+0.5 и будущая пара связки) и во «Входящие» (общий пул).
		var putId = await CreateManualTradeAsync(client, constructionId, PutSymbol, "Buy", 0.5m, 139m, 1.5m, Minutes(2026, 8, 1, 12, 0));
		var callId = await CreateManualTradeAsync(client, constructionId, CallSymbol, "Buy", 0.5m, 12m, 0.6m, Minutes(2026, 8, 1, 12, 5));
		var perpId = await CreateManualTradeAsync(client, constructionId, PerpSymbol, "Buy", 0.2m, 4100m, 0.1m, Minutes(2026, 8, 20, 16, 0));
		var callSellId = await CreateManualTradeAsync(client, constructionId, CallSymbol, "Sell", 0.2m, 5m, 0.1m, Minutes(2026, 8, 20, 16, 4));
		var inboxPerpId = await CreateManualTradeAsync(client, null, PerpSymbol, "Buy", 0.3m, 4141.91m, 0.55m, Minutes(2026, 8, 20, 17, 0));
		var inboxCallId = await CreateManualTradeAsync(client, null, CallSymbol, "Sell", 0.3m, 5.9m, 0.35m, Minutes(2026, 8, 20, 17, 4));

		// Assert: сделки обоих путей приняты; во «Входящих» — ровно пул ручных
		// записей без execId, карточка образует из привязанных позиции.
		var inbox = await ReadInboxAsync(client);
		var inboxItems = inbox!["items"]!.AsArray();
		Assert.That(inboxItems, Has.Count.EqualTo(2));
		Assert.That(inboxItems.Select(node => node!["manualTradeId"]!.GetValue<long>()), Is.EquivalentTo(new[] { inboxPerpId, inboxCallId }));
		Assert.That(inboxItems.Select(node => node!["source"]!.GetValue<string>()), Has.All.EqualTo("manual"));
		Assert.That(inboxItems.Select(node => node["execId"]), Has.All.Null);

		var card = await ReadCardAsync(client, constructionId);
		Assert.That(card!["trades"]!.AsArray(), Has.Count.EqualTo(4));
		Assert.That(card["trades"]!.AsArray().Select(node => node!["source"]!.GetValue<string>()), Has.All.EqualTo("manual"));
		Assert.That(card["trades"]!.AsArray().Select(node => node["execId"]), Has.All.Null);
		Assert.That(card["hasOpenResidual"]!.GetValue<bool>(), Is.True);
		var putBefore = ReadPosition(card, PutSymbol);
		var callBefore = ReadPosition(card, CallSymbol);
		var perpBefore = ReadPosition(card, PerpSymbol);
		Assert.That(putBefore!["residual"]!.GetValue<decimal>(), Is.EqualTo(0.5m));
		Assert.That(callBefore["residual"]!.GetValue<decimal>(), Is.EqualTo(0.3m));
		Assert.That(perpBefore["residual"]!.GetValue<decimal>(), Is.EqualTo(0.2m));
		// Реализованный результат на этом этапе: пут и перп — только комиссии
		// (−1.5 и −0.1), колл — сопоставление продажи 0.2 (5−12)×0.2 = −1.4
		// и комиссии 0.7, итого −2.1.
		Assert.That(putBefore["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(-1.5m));
		Assert.That(callBefore["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(-2.1m));
		Assert.That(perpBefore["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(-0.1m));
		Assert.That(card["hasQuoteFailure"]!.GetValue<bool>(), Is.False);
		Assert.That(card["quotesAsOf"], Is.Not.Null);

		// Act: инкрементальная сборка из «Входящих» на чисто ручной базе —
		// биржевых исполнений нет, команда ничего не создаёт и ручные данные
		// не трогает.
		var assembleResponse = await client.PostAsync("/api/v1/inbox/assemble", null);
		Assert.That(assembleResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), await assembleResponse.Content.ReadAsStringAsync());
		var assemble = await assembleResponse.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(assemble!["constructionsCount"]!.GetValue<int>(), Is.EqualTo(0));
		Assert.That(assemble["boundCount"]!.GetValue<int>(), Is.EqualTo(0));
		Assert.That(assemble["tradesInInbox"]!.GetValue<int>(), Is.EqualTo(0));

		// Act: ручная синтетическая связка перпа и встречного колла — окно
		// времени не проверяется, количество согласовано с обеими сделками.
		var linkResponse = await client.PostAsJsonAsync(
			$"/api/v1/constructions/{constructionId}/synthetic-closes/manual-links",
			new
			{
				linearTradeId = $"manual:{perpId}",
				optionTradeId = $"manual:{callSellId}",
				quantity = 0.2m,
			});
		Assert.That(linkResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), await linkResponse.Content.ReadAsStringAsync());
		var link = await linkResponse.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(link!["id"]!.GetValue<long>(), Is.GreaterThan(0));

		// Assert: связка действует в логическом слое без распознанных связок:
		// лок 0.2×(4400−4100) + 0.2×5 − 0.2×139 − (0.1 + 0.1 + 0.2×3) = 32.4,
		// линейная нога показывает 0.2 синтетики и 0 прочего остатка.
		card = await ReadCardAsync(client, constructionId);
		var syntheticCloses = card!["syntheticCloses"]!.AsArray();
		Assert.That(syntheticCloses, Has.Count.EqualTo(1));
		Assert.That(syntheticCloses[0]!["closedLegSymbol"]!.GetValue<string>(), Is.EqualTo(PutSymbol));
		Assert.That(syntheticCloses[0]["quantity"]!.GetValue<decimal>(), Is.EqualTo(0.2m));
		Assert.That(syntheticCloses[0]["lockedValueUsdt"]!.GetValue<decimal>(), Is.EqualTo(32.4m));
		Assert.That(syntheticCloses[0]["disputed"]!.GetValue<bool>(), Is.False);
		Assert.That(syntheticCloses[0]["linearExecId"]!.GetValue<string>(), Is.EqualTo($"manual:{perpId}"));
		Assert.That(syntheticCloses[0]["counterOptionExecId"]!.GetValue<string>(), Is.EqualTo($"manual:{callSellId}"));
		// Строка снимка несёт идентификатор ручной связки из ответа создания:
		// удаление адресуется списком карточки без сессионных реестров.
		// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
		Assert.That(syntheticCloses[0]["manualLinkId"]!.GetValue<long>(), Is.EqualTo(link!["id"]!.GetValue<long>()));
		var perpWithLink = ReadPosition(card, PerpSymbol);
		Assert.That(perpWithLink!["syntheticResidual"]!.GetValue<decimal>(), Is.EqualTo(0.2m));
		Assert.That(perpWithLink["otherResidual"]!.GetValue<decimal>(), Is.EqualTo(0m));

		// Act: экспирация закрывается ручными пометками с ценой внутренней
		// стоимости на момент доставки: пут частично (0.3 из 0.5) по
		// |4300−4400| = 100, перп — весь остаток по расчётной цене базового
		// актива 4300. Пометки датированы моментом доставки: биржевых записей
		// закрытия в ручном режиме нет, авто-закрытие OTM не вмешивается,
		// потому что к наступлению доставки остатки уже нулевые.
		await AddCloseAnnotationAsync(client, constructionId, PerpSymbol, ExpiryAt, UnderlyingAtExpiry, quantity: null);
		await AddCloseAnnotationAsync(client, constructionId, PutSymbol, ExpiryAt, IntrinsicPutValue(), quantity: 0.3m);

		// Assert: частичная пометка закрывает 0.3 путовой ноги, позиция
		// остаётся открытой на 0.2; перп закрыт полностью.
		card = await ReadCardAsync(client, constructionId);
		var putPartial = ReadPosition(card!, PutSymbol);
		var perpClosed = ReadPosition(card, PerpSymbol);
		Assert.That(putPartial!["residual"]!.GetValue<decimal>(), Is.EqualTo(0.2m));
		Assert.That(putPartial["isOpen"]!.GetValue<bool>(), Is.True);
		// FIFO пута: (100−139)×0.3 = −11.7 и комиссия входа 1.5.
		Assert.That(putPartial["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(-13.2m));
		Assert.That(perpClosed!["residual"]!.GetValue<decimal>(), Is.EqualTo(0m));
		Assert.That(perpClosed["isOpen"]!.GetValue<bool>(), Is.False);
		// FIFO перпа: (4300−4100)×0.2 = 40 и комиссия входа 0.1.
		Assert.That(perpClosed["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(39.9m));

		// Act: остатки гасятся полными пометками с внутренней стоимостью:
		// пут 0.2 по 100, колл вне денег — по нулевой внутренней стоимости.
		await AddCloseAnnotationAsync(client, constructionId, PutSymbol, ExpiryAt.AddMinutes(5), IntrinsicPutValue(), quantity: null);
		await AddCloseAnnotationAsync(client, constructionId, CallSymbol, ExpiryAt.AddMinutes(10), 0m, quantity: null);

		// Assert: все позиции закрыты; FIFO и реализованный результат посчитаны
		// из ручного сырья: пут −(19.5 + 1.5) = −21, колл −(1.4 + 3.6 + 0.7) = −5.7,
		// перп +39.9; средние цены закрытия — цены пометок.
		card = await ReadCardAsync(client, constructionId);
		var putFinal = ReadPosition(card!, PutSymbol);
		var callFinal = ReadPosition(card, CallSymbol);
		var perpFinal = ReadPosition(card, PerpSymbol);
		Assert.That(putFinal!["residual"]!.GetValue<decimal>(), Is.EqualTo(0m));
		Assert.That(callFinal["residual"]!.GetValue<decimal>(), Is.EqualTo(0m));
		Assert.That(perpFinal["residual"]!.GetValue<decimal>(), Is.EqualTo(0m));
		Assert.That(putFinal["isOpen"]!.GetValue<bool>(), Is.False);
		Assert.That(callFinal["isOpen"]!.GetValue<bool>(), Is.False);
		Assert.That(perpFinal["isOpen"]!.GetValue<bool>(), Is.False);
		Assert.That(putFinal["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(-21m));
		Assert.That(putFinal["averageEntryPrice"]!.GetValue<decimal>(), Is.EqualTo(139m));
		Assert.That(putFinal["averageClosePrice"]!.GetValue<decimal>(), Is.EqualTo(100m));
		Assert.That(putFinal["accumulatedFees"]!.GetValue<decimal>(), Is.EqualTo(1.5m));
		Assert.That(callFinal["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(-5.7m));
		Assert.That(callFinal["averageClosePrice"]!.GetValue<decimal>(), Is.EqualTo(2m));
		Assert.That(perpFinal["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(39.9m));

		// Закрывающие записи — ручные пометки без предупреждений об избыточности.
		var closingEntries = card["closingEntries"]!.AsArray();
		Assert.That(closingEntries, Has.Count.EqualTo(4));
		Assert.That(closingEntries.Select(node => node!["kind"]!.GetValue<string>()), Has.All.EqualTo("manual-mark"));
		Assert.That(closingEntries.Select(node => node["manualAnnotationId"]), Has.None.Null);
		Assert.That(closingEntries.Where(node => node!["symbol"]!.GetValue<string>() == PutSymbol)
			.Select(node => node["quantity"]!.GetValue<decimal>()), Is.EquivalentTo(new decimal[] { -0.3m, -0.2m }));
		Assert.That(closingEntries.Single(node => node!["symbol"]!.GetValue<string>() == PerpSymbol)!["quantity"]!.GetValue<decimal>(), Is.EqualTo(-0.2m));
		Assert.That(closingEntries.Single(node => node!["symbol"]!.GetValue<string>() == CallSymbol)!["quantity"]!.GetValue<decimal>(), Is.EqualTo(-0.3m));
		Assert.That(card["closingWarnings"]!.AsArray(), Is.Empty);
		Assert.That(card["hasOpenResidual"]!.GetValue<bool>(), Is.False);

		// Метрики конструкции: реализованный 13.2, нереализованного остатка нет,
		// итог равен реализованному; индикатор финрезультата считан.
		var metrics = card["metrics"]!;
		Assert.That(metrics!["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(13.2m));
		Assert.That(metrics["unrealizedPnL"]!.GetValue<decimal>(), Is.EqualTo(0m));
		Assert.That(metrics["adjustmentsPnL"]!.GetValue<decimal>(), Is.EqualTo(0m));
		Assert.That(metrics["totalPnL"]!.GetValue<decimal>(), Is.EqualTo(13.2m));
		Assert.That(metrics["totalPnLPercent"]!.GetValue<decimal>(), Is.EqualTo(1.32m));
		Assert.That(metrics["closedAt"], Is.Not.Null);
		Assert.That(card["hasQuoteFailure"]!.GetValue<bool>(), Is.False);

		// Связка пережила закрытие: лок и логический слой на месте.
		Assert.That(card["syntheticCloses"]!.AsArray(), Has.Count.EqualTo(1));

		// Assert: индикатор финрезультата обзора конструкций — сводка и строка
		// таблицы сходятся с карточкой: реализованный 13.2, итог 13.2.
		var overview = await (await client.GetAsync("/api/v1/constructions/")).Content.ReadFromJsonAsync<JsonNode>();
		var summary = overview!["summary"]!;
		Assert.That(summary!["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(13.2m));
		Assert.That(summary["unrealizedPnL"]!.GetValue<decimal>(), Is.EqualTo(0m));
		Assert.That(summary["totalPnL"]!.GetValue<decimal>(), Is.EqualTo(13.2m));
		Assert.That(summary["hasQuoteFailure"]!.GetValue<bool>(), Is.False);
		Assert.That(summary["constructionCount"]!.GetValue<int>(), Is.EqualTo(1));
		var row = overview["items"]!.AsArray().Single()!;
		Assert.That(row["constructionId"]!.GetValue<long>(), Is.EqualTo(constructionId));
		Assert.That(row["realizedPnL"]!.GetValue<decimal>(), Is.EqualTo(13.2m));
		Assert.That(row["totalPnL"]!.GetValue<decimal>(), Is.EqualTo(13.2m));

		// «Входящие» пережили весь путь: обе ручные сделки остались в пуле.
		var inboxAtEnd = await ReadInboxAsync(client);
		Assert.That(inboxAtEnd!["items"]!.AsArray().Select(node => node!["manualTradeId"]!.GetValue<long>()),
			Is.EquivalentTo(new[] { inboxPerpId, inboxCallId }));
	}

	#region Помощники

	/// <summary>Внутренняя стоимость пута 4400 на расчётной цене экспирации 4300.</summary>
	private static decimal IntrinsicPutValue() => Math.Abs(UnderlyingAtExpiry - 4400m);

	/// <summary>Момент UTC кейса в часовом поясе нулевого смещения.</summary>
	private static DateTimeOffset Minutes(int year, int month, int day, int hour, int minute) =>
		new(year, month, day, hour, minute, 0, TimeSpan.Zero);

	/// <summary>Применяет миграции к базе хоста до сеяния сырья.</summary>
	private static async Task MigrateAsync(SectionApiFactory factory)
	{
		using var db = new JournalDbContext(OptionsFor(factory.DatabasePath));
		await db.Database.MigrateAsync();
	}

	/// <summary>Параметры прямого доступа к базе хоста для сеяния сырья и доменных записей.</summary>
	private static DbContextOptions<JournalDbContext> OptionsFor(string databasePath) =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={databasePath}")
			.Options;

	/// <summary>
	/// Сеет справочник кейса: перп XAUTUSDT и доска 01DEC28 08:00 UTC — известные
	/// символы разрешаются без сети, неизвестных символов ввод не содержит.
	/// </summary>
	private static void SeedInstruments(string databasePath)
	{
		using var db = new JournalDbContext(OptionsFor(databasePath));
		db.RawInstruments.Add(OptionInstrument(PutSymbol, "Put"));
		db.RawInstruments.Add(OptionInstrument(CallSymbol, "Call"));
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = PerpSymbol,
			Category = "linear",
			PayloadJson = """{"symbol":"XAUTUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"XAUT","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""",
			FetchedAt = QuotesReceivedAt,
		});
		db.SaveChanges();
	}

	/// <summary>Спецификация опциона в справочнике с каноническим временем доставки 08:00 UTC.</summary>
	private static RawInstrument OptionInstrument(string symbol, string optionsType) => new()
	{
		Symbol = symbol,
		Category = "option",
		PayloadJson = $$"""{"symbol":"{{symbol}}","baseCoin":"{{symbol.Split('-')[0]}}","quoteCoin":"USDT","settleCoin":"USDT","status":"Trading","optionsType":"{{optionsType}}","deliveryTime":"{{ExpiryAt.ToUnixTimeMilliseconds()}}","deliveryFeeRate":"0.00015"}""",
		FetchedAt = QuotesReceivedAt,
	};

	/// <summary>
	/// Создаёт конструкцию доменным use-case — так же, как команда создания
	/// конструкции из «Входящих»: отдельной HTTP-команды пустой конструкции
	/// в API нет, ручной режим создаёт её этим сервисом.
	/// </summary>
	private static async Task<long> CreateConstructionAsync(string databasePath, string name) =>
		(await new ConstructionService(OptionsFor(databasePath)).CreateAsync(name, 1000m)).Id;

	/// <summary>Вносит ручную сделку через контракт API; null-конструкция означает «Входящие».</summary>
	private static async Task<long> CreateManualTradeAsync(
		HttpClient client,
		long? constructionId,
		string symbol,
		string side,
		decimal quantity,
		decimal price,
		decimal feeUsdt,
		DateTimeOffset executedAt)
	{
		var response = await client.PostAsJsonAsync("/api/v1/manual-trades", new
		{
			executedAt,
			symbol,
			side,
			quantity,
			price,
			feeUsdt,
			constructionId,
		});
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
		var created = await response.Content.ReadFromJsonAsync<JsonNode>();
		return created!["id"]!.GetValue<long>();
	}

	/// <summary>
	/// Ставит ручную пометку закрытия через контракт API; незаданное количество
	/// закрывает весь остаток, заданное клэмпится остатком.
	/// </summary>
	private static async Task AddCloseAnnotationAsync(
		HttpClient client,
		long constructionId,
		string symbol,
		DateTimeOffset annotatedAt,
		decimal price,
		decimal? quantity)
	{
		var response = await client.PostAsJsonAsync($"/api/v1/constructions/{constructionId}/close-marks", new
		{
			symbol,
			annotatedAt,
			price,
			quantity,
		});
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await response.Content.ReadAsStringAsync());
	}

	/// <summary>Читает снимок карточки конструкции как JSON-узел.</summary>
	private static async Task<JsonNode> ReadCardAsync(HttpClient client, long constructionId)
	{
		var response = await client.GetAsync($"/api/v1/constructions/{constructionId}");
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
		return await response.Content.ReadFromJsonAsync<JsonNode>() ?? throw new InvalidOperationException("Карточка вернула пустой ответ.");
	}

	/// <summary>Читает снимок раздела «Входящие» как JSON-узел.</summary>
	private static async Task<JsonNode> ReadInboxAsync(HttpClient client)
	{
		var response = await client.GetAsync("/api/v1/inbox");
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
		return await response.Content.ReadFromJsonAsync<JsonNode>() ?? throw new InvalidOperationException("«Входящие» вернули пустой ответ.");
	}

	/// <summary>Строка позиции инструмента из снимка карточки.</summary>
	private static JsonNode ReadPosition(JsonNode card, string symbol) =>
		card["positions"]!.AsArray().Single(node => node!["symbol"]!.GetValue<string>() == symbol)!;

	/// <summary>
	/// Стабильный источник котировок ручного режима: котировки публичного канала тикеров
	/// подменены расчётными величинами экспирации кейса — базовый актив 4300,
	/// пут в деньгах 100, колл вне денег 0; обращения к сети отсутствуют.
	/// </summary>
	private sealed class ManualModeQuoteSource : IFreshInstrumentQuoteSource, IInstrumentQuoteSource
	{
		/// <summary>Свежая котировка инструмента — расчётная величина кейса.</summary>
		public Task<InstrumentQuoteSnapshot?> GetFreshQuoteAsync(string symbol, CancellationToken cancellationToken = default)
		{
			decimal? quote = symbol switch
			{
				PerpSymbol => UnderlyingAtExpiry,
				PutSymbol => IntrinsicPutValue(),
				CallSymbol => 0m,
				_ => null,
			};
			return Task.FromResult<InstrumentQuoteSnapshot?>(quote is { } price
				? new InstrumentQuoteSnapshot(symbol, price, QuotesReceivedAt)
				: null);
		}

		/// <summary>Последняя известная котировка — та же расчётная величина кейса.</summary>
		public Task<decimal?> GetLastQuoteAsync(string symbol, CancellationToken cancellationToken = default)
		{
			decimal? quote = symbol switch
			{
				PerpSymbol => UnderlyingAtExpiry,
				PutSymbol => IntrinsicPutValue(),
				CallSymbol => 0m,
				_ => null,
			};
			return Task.FromResult(quote);
		}
	}

	#endregion
}
