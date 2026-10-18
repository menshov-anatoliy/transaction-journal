namespace TransactionJournal.Tests.Web.Api;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Интеграционные проверки HTTP-контрактов ручных сделок: внесение во «Входящие»
/// и сразу в конструкцию, правка с пересчётом позиций, удаление из конструкции,
/// отказ валидации будущей даты, отказ для несуществующей конструкции и
/// неизменяемость биржевых записей при правке и удалении. Фабрика поднимает
/// реальный хост на временной базе SQLite; сырьё сеется напрямую в базу хоста.
/// </summary>
[TestClass]
public sealed class ManualTradeSectionApiTests
{
	private const string LinearSymbol = "BTCUSDT";

	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	/// <summary>Момент «сейчас» кейса: все сделки кейса вносятся в прошлое относительно него.</summary>
	private static readonly DateTimeOffset CaseNow = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

	[TestMethod]
	[Description("Сделка без конструкции попадает во «Входящие» с признаком ручного источника")]
	// Сделка, созданная без указания конструкции, появляется во «Входящих»
	// с признаком источника «ручной ввод» (execId отсутствует) и доступна
	// привязке и сборке.
	// Traceability: openspec:domain/manual-trades#scenario-manual-trade-created-into-inbox
	public async Task TryIfManualTradeCreatedIntoInbox()
	{
		// Arrange: хост на чистой базе со справочником, знающим BTCUSDT.
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();
		await MigrateAsync(factory);
		SeedLinearInstrument(factory.DatabasePath);

		// Act: вносим ручную сделку без конструкции.
		var response = await client.PostAsJsonAsync("/api/v1/manual-trades", new
		{
			executedAt = CaseNow.AddMinutes(-30),
			symbol = LinearSymbol,
			side = "Buy",
			quantity = 0.5m,
			price = 42000m,
			feeUsdt = 1.5m,
			constructionId = (long?)null,
		});

		// Assert: сделка принята без привязки и видна во «Входящих» ручной записью.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
		var created = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(created!["id"]!.GetValue<long>(), Is.GreaterThan(0));
		Assert.That(created["constructionId"], Is.Null);

		var inbox = await (await client.GetAsync("/api/v1/inbox")).Content.ReadFromJsonAsync<JsonNode>();
		var manualRow = inbox!["items"]!.AsArray().Single(node => node!["symbol"]!.GetValue<string>() == LinearSymbol);
		Assert.That(manualRow!["execId"], Is.Null);
		Assert.That(manualRow["isBuy"]!.GetValue<bool>(), Is.True);
		Assert.That(manualRow["quantity"]!.GetValue<decimal>(), Is.EqualTo(0.5m));
		Assert.That(manualRow["price"]!.GetValue<decimal>(), Is.EqualTo(42000m));
	}

	[TestMethod]
	[Description("Сделка с constructionId попадает сразу в конструкцию, минуя «Входящие»")]
	// При создании с указанием конструкции сделка принадлежит ровно ей
	// и участвует в её позициях и метриках.
	// Traceability: openspec:domain/manual-trades#scenario-manual-trade-created-into-construction
	public async Task TryIfManualTradeCreatedIntoConstruction()
	{
		// Arrange: хост, справочник и конструкция-цель.
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();
		await MigrateAsync(factory);
		SeedLinearInstrument(factory.DatabasePath);
		var constructionId = await SeedConstructionAsync(factory.DatabasePath, "Ручной тренд");

		// Act: вносим сделку сразу в конструкцию.
		var response = await client.PostAsJsonAsync("/api/v1/manual-trades", new
		{
			executedAt = CaseNow.AddMinutes(-20),
			symbol = LinearSymbol,
			side = "Buy",
			quantity = 1m,
			price = 42000m,
			feeUsdt = 0m,
			constructionId,
		});

		// Assert: сделка привязана к конструкции, в карточке образует позицию,
		// а «Входящие» остаются пустыми.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
		var created = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(created!["constructionId"]!.GetValue<long>(), Is.EqualTo(constructionId));

		var card = await ReadCardAsync(client, constructionId);
		var tradeRow = card["trades"]!.AsArray().Single();
		Assert.That(tradeRow!["execId"], Is.Null);
		Assert.That(tradeRow["symbol"]!.GetValue<string>(), Is.EqualTo(LinearSymbol));
		var position = card["positions"]!.AsArray().Single(node => node!["symbol"]!.GetValue<string>() == LinearSymbol)!;
		Assert.That(position["residual"]!.GetValue<decimal>(), Is.EqualTo(1m));

		var inbox = await (await client.GetAsync("/api/v1/inbox")).Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(inbox!["items"]!.AsArray(), Is.Empty);
	}

	[TestMethod]
	[Description("Правка цены ручной сделки пересчитывает метрики конструкции при чтении")]
	// Правка ручной сделки, привязанной к конструкции, детерминированно
	// пересчитывает позиции, FIFO и метрики при очередном чтении.
	// Traceability: openspec:domain/manual-trades#scenario-manual-trade-edit-recalculates
	public async Task TryIfManualTradeEditRecalculatesMetrics()
	{
		// Arrange: конструкция с ручной покупкой и ручной продажей — реализованный
		// результат 50 USDT до правки.
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();
		await MigrateAsync(factory);
		SeedLinearInstrument(factory.DatabasePath);
		var constructionId = await SeedConstructionAsync(factory.DatabasePath, "Ручной скальп");
		var buyId = await CreateManualTradeAsync(client, constructionId, "Buy", 1m, 100m, minutesAgo: 20);
		await CreateManualTradeAsync(client, constructionId, "Sell", 1m, 150m, minutesAgo: 10);

		var before = await ReadCardAsync(client, constructionId);
		Assert.That(before["metrics"]!["realizedPnl"]!.GetValue<decimal>(), Is.EqualTo(50m));

		// Act: правим цену покупки 100 → 90.
		var edit = await client.PutAsJsonAsync($"/api/v1/manual-trades/{buyId}", new
		{
			executedAt = CaseNow.AddMinutes(-20),
			symbol = LinearSymbol,
			side = "Buy",
			quantity = 1m,
			price = 90m,
			feeUsdt = 0m,
		});

		// Assert: правка принята, позиция осталась привязанной, реализованный
		// результат пересчитался при чтении карточки: (150 − 90) × 1.
		Assert.That(edit.StatusCode, Is.EqualTo(HttpStatusCode.OK), await edit.Content.ReadAsStringAsync());
		var edited = await edit.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(edited!["price"]!.GetValue<decimal>(), Is.EqualTo(90m));
		Assert.That(edited["constructionId"]!.GetValue<long>(), Is.EqualTo(constructionId));

		var after = await ReadCardAsync(client, constructionId);
		Assert.That(after["metrics"]!["realizedPnl"]!.GetValue<decimal>(), Is.EqualTo(60m));
		var tradeRow = after["trades"]!.AsArray().Single(node => node!["isBuy"]!.GetValue<bool>())!;
		Assert.That(tradeRow["price"]!.GetValue<decimal>(), Is.EqualTo(90m));
	}

	[TestMethod]
	[Description("Удаление ручной сделки из конструкции убирает её из журнала и пересчитывает позицию")]
	// Сделка исчезает из журнала, позиции и метрики пересчитываются без неё.
	// Traceability: openspec:domain/manual-trades#scenario-manual-trade-delete-anywhere
	public async Task TryIfManualTradeDeletedFromConstruction()
	{
		// Arrange: конструкция с ручной покупкой; после удаления остаётся только позиция продажи.
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();
		await MigrateAsync(factory);
		SeedLinearInstrument(factory.DatabasePath);
		var constructionId = await SeedConstructionAsync(factory.DatabasePath, "Ручной выход");
		var buyId = await CreateManualTradeAsync(client, constructionId, "Buy", 1m, 100m, minutesAgo: 15);
		await CreateManualTradeAsync(client, constructionId, "Sell", 1m, 150m, minutesAgo: 5);

		// Act: удаляем покупку из таблицы конструкции.
		var deletion = await client.DeleteAsync($"/api/v1/manual-trades/{buyId}");

		// Assert: сделка исчезла из журнала целиком, позиция пересчиталась
		// коротким остатком −1 без покупок.
		Assert.That(deletion.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

		var card = await ReadCardAsync(client, constructionId);
		Assert.That(card["trades"]!.AsArray().Count(node => node!["execId"] is null), Is.EqualTo(1));
		var position = card["positions"]!.AsArray().Single(node => node!["symbol"]!.GetValue<string>() == LinearSymbol)!;
		Assert.That(position["residual"]!.GetValue<decimal>(), Is.EqualTo(-1m));

		var inbox = await (await client.GetAsync("/api/v1/inbox")).Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(inbox!["items"]!.AsArray(), Is.Empty);
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	[Description("Правка и удаление по идентификатору биржевой записи возвращают ошибку запроса")]
	// Попытка изменить биржевую запись — ошибка запроса: синхронизированные
	// сделки неизменяемы.
	// Traceability: openspec:domain/manual-trades#scenario-synced-trade-edit-rejected
	// Traceability: change:manual-journal-management/design#d4
	public async Task ThrowOnChangeExchangeRecord(bool edit)
	{
		// Arrange: биржевая запись исполнения в сырье хоста.
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();
		await MigrateAsync(factory);
		SeedLinearInstrument(factory.DatabasePath);
		var exchangeRecordId = await SeedExchangeExecutionAsync(factory.DatabasePath);

		// Act: правка и удаление адресуют биржевую запись через контракт ручных сделок.
		var response = edit
			? await client.PutAsJsonAsync($"/api/v1/manual-trades/{exchangeRecordId}", new
			{
				executedAt = CaseNow.AddMinutes(-1),
				symbol = LinearSymbol,
				side = "Buy",
				quantity = 1m,
				price = 42000m,
				feeUsdt = 0m,
			})
			: await client.DeleteAsync($"/api/v1/manual-trades/{exchangeRecordId}");

		// Assert: отказ запроса с человекочитаемой причиной о неизменяемости;
		// биржевая запись осталась в сырье.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await response.Content.ReadAsStringAsync());
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["error"]!.GetValue<string>(), Does.Contain("неизменяема"));

		using var db = new JournalDbContext(OptionsFor(factory.DatabasePath));
		Assert.That(db.RawExecutions.Count(execution => execution.Id == exchangeRecordId), Is.EqualTo(1));
	}

	[TestMethod]
	[Description("Внесение сделки с датой в будущем отклоняется ошибкой запроса")]
	// Будущая дата отклоняется серверной валидацией с человекочитаемым сообщением.
	// Traceability: openspec:domain/manual-trades#scenario-future-date-rejected
	public async Task ThrowOnCreateWithFutureDate()
	{
		// Arrange: хост на чистой базе; форма несёт дату в будущем.
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();
		await MigrateAsync(factory);

		// Act: вносим сделку с датой позже текущего момента системных часов хоста.
		var response = await client.PostAsJsonAsync("/api/v1/manual-trades", new
		{
			executedAt = DateTimeOffset.UtcNow.AddHours(1),
			symbol = LinearSymbol,
			side = "Buy",
			quantity = 1m,
			price = 42000m,
			feeUsdt = 0m,
			constructionId = (long?)null,
		});

		// Assert: отказ запроса с объяснением недопустимой даты.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await response.Content.ReadAsStringAsync());
		var payload = await response.Content.ReadFromJsonAsync<JsonNode>();
		Assert.That(payload!["error"]!.GetValue<string>(), Does.Contain("будущем"));
	}

	[TestMethod]
	[Description("Внесение в несуществующую конструкцию возвращает 404")]
	// Заданная при создании конструкция должна существовать.
	// Traceability: openspec:domain/manual-trades#requirement-manual-trade-entry
	public async Task ThrowOnCreateIntoUnknownConstruction()
	{
		// Arrange: хост на чистой базе.
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();
		await MigrateAsync(factory);

		// Act: вносим сделку в конструкцию с чужим идентификатором.
		var response = await client.PostAsJsonAsync("/api/v1/manual-trades", new
		{
			executedAt = CaseNow.AddMinutes(-1),
			symbol = LinearSymbol,
			side = "Buy",
			quantity = 1m,
			price = 42000m,
			feeUsdt = 0m,
			constructionId = 999L,
		});

		// Assert: конструкция не найдена.
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), await response.Content.ReadAsStringAsync());
	}

	[TestMethod]
	[Description("OpenAPI публикует эндпоинты ручных сделок")]
	// Контракт ручных сделок отражён в публикуемом документе OpenAPI.
	// Traceability: openspec:http-api/transport#scenario-openapi-schema-published
	public async Task TryIfOpenApiListsManualTradeEndpoints()
	{
		// Arrange: хост на чистой базе.
		await using var factory = new SectionApiFactory();
		using var client = factory.CreateClient();

		// Act: читаем документ OpenAPI.
		var document = await (await client.GetAsync("/openapi/v1.json")).Content.ReadFromJsonAsync<JsonNode>();

		// Assert: все три операции ручных сделок опубликованы.
		var paths = document!["paths"]!.AsObject().Select(path => path.Key).ToArray();
		Assert.That(paths, Does.Contain("/api/v1/manual-trades"));
		Assert.That(paths, Does.Contain("/api/v1/manual-trades/{id}"));
	}

	#region Помощники

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

	/// <summary>Сеет спецификацию линейного перпа в справочник: ручной ввод известного символа не зовёт сеть.</summary>
	private static void SeedLinearInstrument(string databasePath)
	{
		using var db = new JournalDbContext(OptionsFor(databasePath));
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = LinearSymbol,
			Category = "linear",
			PayloadJson = """{"symbol":"BTCUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"BTC","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""",
			FetchedAt = FetchedAt,
		});
		db.SaveChanges();
	}

	/// <summary>Создаёт конструкцию и возвращает её идентификатор.</summary>
	private static async Task<long> SeedConstructionAsync(string databasePath, string name) =>
		(await new ConstructionService(OptionsFor(databasePath)).CreateAsync(name, 1000m)).Id;

	/// <summary>Вносит ручную сделку в конструкцию через контракт API и возвращает её идентификатор.</summary>
	private static async Task<long> CreateManualTradeAsync(
		HttpClient client,
		long constructionId,
		string side,
		decimal quantity,
		decimal price,
		int minutesAgo)
	{
		var response = await client.PostAsJsonAsync("/api/v1/manual-trades", new
		{
			executedAt = CaseNow.AddMinutes(-minutesAgo),
			symbol = LinearSymbol,
			side,
			quantity,
			price,
			feeUsdt = 0m,
			constructionId,
		});
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
		var created = await response.Content.ReadFromJsonAsync<JsonNode>();
		return created!["id"]!.GetValue<long>();
	}

	/// <summary>Сеет сырую биржевую запись исполнения и возвращает идентификатор её строки сырья.</summary>
	private static async Task<long> SeedExchangeExecutionAsync(string databasePath)
	{
		var execTimeMs = CaseNow.AddMinutes(-30).ToUnixTimeMilliseconds();
		using var db = new JournalDbContext(OptionsFor(databasePath));
		var execution = new RawExecution
		{
			ExecId = "exec-exchange-case",
			Category = "linear",
			Symbol = LinearSymbol,
			ExecTimeMs = execTimeMs,
			PayloadJson = $$"""{"symbol":"{{LinearSymbol}}","orderId":"order-exchange-case","orderLinkId":"","side":"Buy","execFee":"0","execId":"exec-exchange-case","execPrice":"42000","execQty":"1","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":"USDT","isMaker":false}""",
			FetchedAt = FetchedAt,
		};
		db.RawExecutions.Add(execution);
		await db.SaveChangesAsync();
		return execution.Id;
	}

	/// <summary>Читает снимок карточки конструкции как JSON-узел.</summary>
	private static async Task<JsonNode> ReadCardAsync(HttpClient client, long constructionId)
	{
		var response = await client.GetAsync($"/api/v1/constructions/{constructionId}");
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
		return await response.Content.ReadFromJsonAsync<JsonNode>() ?? throw new InvalidOperationException("Карточка вернула пустой ответ.");
	}

	#endregion
}
