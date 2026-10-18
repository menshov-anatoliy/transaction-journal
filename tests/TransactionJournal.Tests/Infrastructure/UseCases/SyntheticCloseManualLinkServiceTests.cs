using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Infrastructure.UseCases;

/// <summary>
/// Проверки use-case сервиса ручных синтетических связок на реальном
/// SQLite-хранилище: связка создаётся из двух сделок одной конструкции —
/// биржевых, ручных или смешанных — с согласованным количеством и без
/// проверки окна времени; отказы на сделки разных конструкций, несовпадение
/// страйка или экспирации встречного опциона с закрываемой ногой,
/// несогласованное количество, неизвестные конструкцию и сделки; свободное
/// удаление и отказ на повторное удаление.
/// </summary>
[TestClass]
public class SyntheticCloseManualLinkServiceTests
{
	private const string XautCall4400 = "XAUT-30OCT26-4400-C-USDT";
	private const string XautPut4400 = "XAUT-30OCT26-4400-P-USDT";
	private const string XautCall4500 = "XAUT-30OCT26-4500-C-USDT";
	private const string XautCallNov = "XAUT-27NOV26-4400-C-USDT";
	private const string XautPerp = "XAUTUSDT";

	private string _databasePath = null!;
	private SyntheticCloseManualLinkService _service = null!;
	private ConstructionService _constructions = null!;
	private TradeBindingService _binding = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке
		// со справочником инструментов доски 30OCT26 и соседних досок.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-manual-link-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
			SeedInstruments(db);
		}

		_service = new SyntheticCloseManualLinkService(CreateOptions());
		_constructions = new ConstructionService(CreateOptions());
		_binding = new TradeBindingService(CreateOptions());
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
	[Description("Связка создаётся из двух биржевых сделок конструкции и хранится с количеством")]
	// Пользователь соединяет линейную сделку и встречный опцион конструкции:
	// связка сохраняется ручными данными и действует в логическом слое.
	// Traceability: openspec:domain/synthetic-close#scenario-manual-link-created
	public async Task TryIfLinkCreatedFromTwoExchangeTrades()
	{
		// Arrange: конструкция стрэддла с пут-ногой, перпом и продажей колла.
		var construction = await CreateStraddleConstructionAsync();

		// Act: создаём связку из перпа и встречного колла.
		var link = await _service.CreateAsync(construction.Id, "case-perp", "case-call", 0.3m);

		// Assert: связка хранится с полной парой и количеством.
		Assert.That(link.Id, Is.GreaterThan(0));
		Assert.That(link.ConstructionId, Is.EqualTo(construction.Id));
		Assert.That(link.LinearTradeId, Is.EqualTo("case-perp"));
		Assert.That(link.OptionTradeId, Is.EqualTo("case-call"));
		Assert.That(link.Quantity, Is.EqualTo(0.3m));

		var stored = ReadLinks(construction.Id).Single();
		Assert.That(stored.LinearTradeId, Is.EqualTo("case-perp"));
		Assert.That(stored.OptionTradeId, Is.EqualTo("case-call"));
	}

	[TestMethod]
	[Description("Связка создаётся при сделках дальше 60 минут друг от друга")]
	// Окно времени — правило автопары распознавателя; ручная связка соединяет
	// сделки независимо от зазора между их исполнениями.
	// Traceability: openspec:domain/synthetic-close#scenario-manual-link-no-time-window
	public async Task TryIfLinkCreatedBeyondTimeWindow()
	{
		// Arrange: конструкции с пут-ногой и парой сделок с зазором шесть часов.
		var construction = await CreateStraddleConstructionAsync();
		await AddExchangeTradeAsync("late-perp", XautPerp, "linear", "Buy", "0.1", "4300", "0.1", ExecMs(2026, 10, 15, 8, 0), "USDT");
		await AddExchangeTradeAsync("late-call", XautCall4400, "option", "Sell", "0.1", "6", "0.1", ExecMs(2026, 10, 15, 14, 0), "USDT");
		await _binding.BindBatchAsync(construction.Id, ["late-perp", "late-call"]);

		// Act — Assert: связка на зазоре шесть часов создаётся без отказов.
		var link = await _service.CreateAsync(construction.Id, "late-perp", "late-call", 0.1m);
		Assert.That(link.Quantity, Is.EqualTo(0.1m));
	}

	[TestMethod]
	[Description("Связка соединяет ручную сделку с биржевой по единому ключу")]
	// Ссылки связки строковые: у биржевой сделки это execId, у ручной — ключ
	// строки сырья; единый ключ обслуживает оба источника.
	// Traceability: openspec:domain/synthetic-close#scenario-manual-link-created
	public async Task TryIfLinkCreatedFromManualAndExchangeTrades()
	{
		// Arrange: конструкция стрэддла и ручная покупка перпа.
		var construction = await CreateStraddleConstructionAsync();
		var manualPerp = await AddManualTradeAsync(construction.Id, XautPerp, "Buy", 0.2m, 4200m, 0.2m, ExecMs(2026, 10, 15, 12, 0));

		// Act: создаём связку ручного перпа с биржевым встречным коллом.
		var link = await _service.CreateAsync(construction.Id, $"manual:{manualPerp.Id}", "case-call", 0.2m);

		// Assert: связка хранит строковые ключи обоих источников.
		Assert.That(link.LinearTradeId, Is.EqualTo($"manual:{manualPerp.Id}"));
		Assert.That(link.OptionTradeId, Is.EqualTo("case-call"));
	}

	[TestMethod]
	[Description("Количество связки может быть меньше количеств обеих сделок")]
	// Согласованность количества — верхняя граница по каждой из сделок,
	// а не равенство: частичная ручная связка допустима.
	// Traceability: openspec:domain/synthetic-close#scenario-manual-link-created
	public async Task TryIfPartialQuantityAccepted()
	{
		// Arrange: конструкция стрэддла с парой сделок по 0.3.
		var construction = await CreateStraddleConstructionAsync();

		// Act: создаём связку количеством 0.25 — меньше обеих сделок.
		var link = await _service.CreateAsync(construction.Id, "case-perp", "case-call", 0.25m);

		// Assert: частичное количество сохранено как задано.
		Assert.That(link.Quantity, Is.EqualTo(0.25m));
	}

	[TestMethod]
	[Description("Связка из сделок разных конструкций отклоняется")]
	// Обе сделки связки обязаны принадлежать одной конструкции: встречный
	// опцион чужой конструкции не найден в конструкции линейной сделки.
	// Traceability: openspec:domain/synthetic-close#scenario-manual-link-rejected-on-mismatch
	public async Task ThrowOnLinkAcrossConstructions()
	{
		// Arrange: две конструкции; колл привязан ко второй.
		var construction = await CreateStraddleConstructionAsync();
		var other = await _constructions.CreateAsync("Другая конструкция", 500m);
		await AddExchangeTradeAsync("other-call", XautCall4400, "option", "Sell", "0.3", "5.9", "0.35", ExecMs(2026, 10, 15, 13, 4), "USDT");
		await _binding.BindAsync(other.Id, "other-call");

		// Act — Assert: встречный опцион не из конструкции линейной сделки.
		Assert.ThrowsAsync<SyntheticCloseManualLinkValidationException>(async () =>
			await _service.CreateAsync(construction.Id, "case-perp", "other-call", 0.3m));
	}

	[TestMethod]
	[Description("Несовпадение страйка встречного опциона с закрываемой ногой отклоняется")]
	// Встречный опцион обязан совпадать страйком с закрываемой ногой: доска
	// 4500 не имеет длинной пут-ноги в конструкции.
	// Traceability: openspec:domain/synthetic-close#scenario-manual-link-rejected-on-mismatch
	public async Task ThrowOnStrikeMismatch()
	{
		// Arrange: конструкция с пут-ногой 4400 и продажей колла 4500.
		var construction = await CreateStraddleConstructionAsync();
		await AddExchangeTradeAsync("strike-call", XautCall4500, "option", "Sell", "0.1", "3", "0.1", ExecMs(2026, 10, 15, 13, 30), "USDT");
		await _binding.BindAsync(construction.Id, "strike-call");

		// Act — Assert: страйк встречного опциона не совпадает с ногой.
		Assert.ThrowsAsync<SyntheticCloseManualLinkValidationException>(async () =>
			await _service.CreateAsync(construction.Id, "case-perp", "strike-call", 0.1m));
	}

	[TestMethod]
	[Description("Несовпадение экспирации встречного опциона с закрываемой ногой отклоняется")]
	// Встречный опцион обязан совпадать экспирацией с закрываемой ногой:
	// соседняя доска 27NOV26 не имеет длинной пут-ноги.
	// Traceability: openspec:domain/synthetic-close#scenario-manual-link-rejected-on-mismatch
	public async Task ThrowOnExpiryMismatch()
	{
		// Arrange: конструкция с пут-ногой доски 30OCT26 и продажей колла 27NOV26.
		var construction = await CreateStraddleConstructionAsync();
		await AddExchangeTradeAsync("nov-call", XautCallNov, "option", "Sell", "0.1", "4", "0.1", ExecMs(2026, 10, 15, 13, 30), "USDT");
		await _binding.BindAsync(construction.Id, "nov-call");

		// Act — Assert: экспирация встречного опциона не совпадает с ногой.
		Assert.ThrowsAsync<SyntheticCloseManualLinkValidationException>(async () =>
			await _service.CreateAsync(construction.Id, "case-perp", "nov-call", 0.1m));
	}

	[TestMethod]
	[Description("Количество связки больше количества линейной сделки отклоняется")]
	// Количество связки не превышает количество ни одной из сделок пары.
	// Traceability: openspec:domain/synthetic-close#scenario-manual-link-rejected-on-mismatch
	public async Task ThrowOnQuantityAboveLinear()
	{
		// Arrange: конструкция стрэддла и крупный перп 0.5 против колла 0.3.
		var construction = await CreateStraddleConstructionAsync();
		await AddExchangeTradeAsync("big-perp", XautPerp, "linear", "Buy", "0.5", "4300", "0.2", ExecMs(2026, 10, 15, 14, 0), "USDT");
		await _binding.BindAsync(construction.Id, "big-perp");

		// Act — Assert: количество 0.4 превышает колл 0.3.
		Assert.ThrowsAsync<SyntheticCloseManualLinkValidationException>(async () =>
			await _service.CreateAsync(construction.Id, "big-perp", "case-call", 0.4m));
	}

	[TestMethod]
	[Description("Количество связки больше количества встречного опциона отклоняется")]
	// Граница проверяется по обеим сделкам: линейной и встречному опциону.
	// Traceability: openspec:domain/synthetic-close#scenario-manual-link-rejected-on-mismatch
	public async Task ThrowOnQuantityAboveOption()
	{
		// Arrange: конструкция стрэддла; линейная сделка 0.3, колл 0.3.
		var construction = await CreateStraddleConstructionAsync();

		// Act — Assert: количество 0.4 превышает каждую из сделок 0.3.
		Assert.ThrowsAsync<SyntheticCloseManualLinkValidationException>(async () =>
			await _service.CreateAsync(construction.Id, "case-perp", "case-call", 0.4m));
	}

	[TestMethod]
	[Description("Неположительное количество связки отклоняется")]
	// Количество связки должно быть больше нуля: нулевая и отрицательная
	// связки смысла закрытия не несут.
	// Traceability: openspec:domain/synthetic-close#scenario-manual-link-rejected-on-mismatch
	public async Task ThrowOnNonPositiveQuantity()
	{
		// Arrange: конструкция стрэддла.
		var construction = await CreateStraddleConstructionAsync();

		// Act — Assert: нулевое количество.
		Assert.ThrowsAsync<SyntheticCloseManualLinkValidationException>(async () =>
			await _service.CreateAsync(construction.Id, "case-perp", "case-call", 0m));

		// Отрицательное количество тоже отклоняется.
		Assert.ThrowsAsync<SyntheticCloseManualLinkValidationException>(async () =>
			await _service.CreateAsync(construction.Id, "case-perp", "case-call", -0.1m));
	}

	[TestMethod]
	[Description("Связка в неизвестную конструкцию отклоняется")]
	// Создание адресует существующую конструкцию — владелицу ручных данных.
	// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public async Task ThrowOnUnknownConstruction()
	{
		// Arrange — Act: связка в конструкцию, которой нет.
		await _service.CreateAsync(999, "case-perp", "case-call", 0.3m);
	}

	[TestMethod]
	[Description("Пустой идентификатор линейной сделки отклоняется")]
	// Идентификаторы сделок связки обязательны: пустая строка — ошибка запроса.
	// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
	[ExpectedException(typeof(ArgumentException))]
	public async Task ThrowOnEmptyLinearTradeId()
	{
		// Arrange — Act: линейный идентификатор из пробелов.
		await _service.CreateAsync(1, "   ", "case-call", 0.3m);
	}

	[TestMethod]
	[Description("Пустой идентификатор встречного опциона отклоняется")]
	// Оба идентификатора связки обязательны.
	// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
	[ExpectedException(typeof(ArgumentException))]
	public async Task ThrowOnEmptyOptionTradeId()
	{
		// Arrange — Act: встречный идентификатор пустой.
		await _service.CreateAsync(1, "case-perp", "", 0.3m);
	}

	[TestMethod]
	[Description("Неизвестная линейная сделка отклоняется")]
	// Сделка, отсутствующая в конструкции (чужая или несуществующая), связку
	// не образует.
	// Traceability: openspec:domain/synthetic-close#scenario-manual-link-rejected-on-mismatch
	public async Task ThrowOnUnknownLinearTrade()
	{
		// Arrange: конструкция стрэддла.
		var construction = await CreateStraddleConstructionAsync();

		// Act — Assert: линейной сделки с таким ключом нет в конструкции.
		Assert.ThrowsAsync<SyntheticCloseManualLinkValidationException>(async () =>
			await _service.CreateAsync(construction.Id, "missing-perp", "case-call", 0.3m));
	}

	[TestMethod]
	[Description("Неизвестный встречный опцион отклоняется")]
	// Встречный опцион, отсутствующий в конструкции, связку не образует.
	// Traceability: openspec:domain/synthetic-close#scenario-manual-link-rejected-on-mismatch
	public async Task ThrowOnUnknownOptionTrade()
	{
		// Arrange: конструкция стрэддла.
		var construction = await CreateStraddleConstructionAsync();

		// Act — Assert: опциона с таким ключом нет в конструкции.
		Assert.ThrowsAsync<SyntheticCloseManualLinkValidationException>(async () =>
			await _service.CreateAsync(construction.Id, "case-perp", "missing-call", 0.3m));
	}

	[TestMethod]
	[Description("Удалённая связка исчезает из хранилища")]
	// Ручная связка удаляется свободно: логический слой пересчитывается
	// ближайшим чтением без удалённой связки.
	// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
	public async Task TryIfLinkDeleted()
	{
		// Arrange: конструкция стрэддла с созданной связкой.
		var construction = await CreateStraddleConstructionAsync();
		var link = await _service.CreateAsync(construction.Id, "case-perp", "case-call", 0.3m);

		// Act: удаляем связку.
		await _service.DeleteAsync(construction.Id, link.Id);

		// Assert: в хранилище связок конструкции пусто.
		Assert.That(ReadLinks(construction.Id), Is.Empty);
	}

	[TestMethod]
	[Description("Повторное удаление несуществующей связки отказывает")]
	// Удаление возможно только существующей связки конструкции.
	// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
	[ExpectedException(typeof(SyntheticCloseManualLinkNotFoundException))]
	public async Task ThrowOnDeleteUnknownLink()
	{
		// Arrange: конструкция стрэддла без связок.
		var construction = await CreateStraddleConstructionAsync();

		// Act: удаляем несуществующую связку.
		await _service.DeleteAsync(construction.Id, 999);
	}

	[TestMethod]
	[Description("Связка чужой конструкции по её идентификатору не удаляется")]
	// Ключ удаления — пара «конструкция + связка»: идентификатор связки другой
	// конструкции в текущей не найден.
	// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
	public async Task ThrowOnDeleteLinkOfOtherConstruction()
	{
		// Arrange: конструкция стрэддла со связкой и вторая конструкция.
		var construction = await CreateStraddleConstructionAsync();
		var link = await _service.CreateAsync(construction.Id, "case-perp", "case-call", 0.3m);
		var other = await _constructions.CreateAsync("Вторая конструкция", 500m);

		// Act — Assert: удаление по чужой конструкции отклоняется.
		Assert.ThrowsAsync<SyntheticCloseManualLinkNotFoundException>(async () =>
			await _service.DeleteAsync(other.Id, link.Id));

		// Связка первой конструкции осталась на месте.
		Assert.That(ReadLinks(construction.Id), Has.Count.EqualTo(1));
	}

	[TestMethod]
	[Description("Null-опции контекста отклоняются конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullOptions()
	{
		// Arrange — Act — Assert
		new SyntheticCloseManualLinkService(null!);
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>
	/// Создаёт конструкцию стрэддла: пут 0.5 @139, колл 0.5 @12, перп 0.3 @4141.91
	/// и продажа 0.3 колла @5.9 — все сделки биржевые и привязаны.
	/// </summary>
	private async Task<Construction> CreateStraddleConstructionAsync()
	{
		var construction = await _constructions.CreateAsync("XAUT стреддл 30OCT26 4400", 1000m);
		await AddExchangeTradeAsync("open-put", XautPut4400, "option", "Buy", "0.5", "139", "1.5", ExecMs(2026, 9, 30, 12, 0), "USDC");
		await AddExchangeTradeAsync("open-call", XautCall4400, "option", "Buy", "0.5", "12", "0.6", ExecMs(2026, 9, 30, 12, 5), "USDC");
		await AddExchangeTradeAsync("case-perp", XautPerp, "linear", "Buy", "0.3", "4141.91", "0.55", ExecMs(2026, 10, 15, 13, 0), "USDT");
		await AddExchangeTradeAsync("case-call", XautCall4400, "option", "Sell", "0.3", "5.9", "0.35", ExecMs(2026, 10, 15, 13, 4), "USDT");
		await _binding.BindBatchAsync(construction.Id, ["open-put", "open-call", "case-perp", "case-call"]);
		return construction;
	}

	/// <summary>Добавляет сырую запись исполнения; числа биржа шлёт строками.</summary>
	private async Task AddExchangeTradeAsync(
		string execId,
		string symbol,
		string category,
		string side,
		string execQty,
		string execPrice,
		string execFee,
		long execTimeMs,
		string feeCurrency)
	{
		using var db = new JournalDbContext(CreateOptions());
		db.RawExecutions.Add(new RawExecution
		{
			ExecId = execId,
			Category = category,
			Symbol = symbol,
			ExecTimeMs = execTimeMs,
			PayloadJson = $$"""{"symbol":"{{symbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"{{execFee}}","execId":"{{execId}}","execPrice":"{{execPrice}}","execQty":"{{execQty}}","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":"{{feeCurrency}}","isMaker":false}""",
			FetchedAt = FetchedAt,
		});
		await db.SaveChangesAsync();
	}

	/// <summary>Вносит ручную сделку прямо в сырьё и возвращает её с ключом строки.</summary>
	private async Task<ManualTrade> AddManualTradeAsync(
		long constructionId,
		string symbol,
		string side,
		decimal quantity,
		decimal price,
		decimal feeUsdt,
		long executedAtMs)
	{
		using var db = new JournalDbContext(CreateOptions());
		var trade = new ManualTrade
		{
			ExecutedAt = DateTimeOffset.FromUnixTimeMilliseconds(executedAtMs),
			Symbol = symbol,
			Side = side,
			Quantity = quantity,
			Price = price,
			FeeUsdt = feeUsdt,
			ConstructionId = constructionId,
			CreatedAt = FetchedAt,
			UpdatedAt = FetchedAt,
		};
		db.ManualTrades.Add(trade);
		await db.SaveChangesAsync();
		return trade;
	}

	/// <summary>Читает ручные связки конструкции напрямую из хранилища.</summary>
	private List<SyntheticCloseManualLink> ReadLinks(long constructionId)
	{
		using var db = new JournalDbContext(CreateOptions());
		return db.SyntheticCloseManualLinks
			.AsNoTracking()
			.Where(link => link.ConstructionId == constructionId)
			.OrderBy(link => link.Id)
			.ToList();
	}

	/// <summary>Справочник проверки: пут и коллы досок 30OCT26 и 27NOV26, перп XAUTUSDT.</summary>
	private static void SeedInstruments(JournalDbContext db)
	{
		db.RawInstruments.Add(OptionInstrument(XautPut4400, "Put", ExecMs(2026, 10, 30, 8, 0)));
		db.RawInstruments.Add(OptionInstrument(XautCall4400, "Call", ExecMs(2026, 10, 30, 8, 0)));
		db.RawInstruments.Add(OptionInstrument(XautCall4500, "Call", ExecMs(2026, 10, 30, 8, 0)));
		db.RawInstruments.Add(OptionInstrument(XautCallNov, "Call", ExecMs(2026, 11, 27, 8, 0)));
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = XautPerp,
			Category = "linear",
			PayloadJson = """{"symbol":"XAUTUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"XAUT","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""",
			FetchedAt = FetchedAt,
		});
		db.SaveChanges();
	}

	/// <summary>Спецификация опциона в справочнике с каноническим временем доставки 08:00 UTC.</summary>
	private static RawInstrument OptionInstrument(string symbol, string optionsType, long deliveryTimeMs) => new()
	{
		Symbol = symbol,
		Category = "option",
		PayloadJson = $$"""{"symbol":"{{symbol}}","baseCoin":"{{symbol.Split('-')[0]}}","quoteCoin":"USDT","settleCoin":"USDT","status":"Trading","optionsType":"{{optionsType}}","deliveryTime":"{{deliveryTimeMs}}","deliveryFeeRate":"0.00015"}""",
		FetchedAt = FetchedAt,
	};

	private static long ExecMs(int year, int month, int day, int hour, int minute) =>
		new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	/// <summary>Момент получения справочника и отметок создания ручных сделок.</summary>
	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	#endregion
}
