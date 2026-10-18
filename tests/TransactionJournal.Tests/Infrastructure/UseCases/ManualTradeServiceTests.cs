using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Domain.Bybit;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Application;
using TransactionJournal.Application.Sync;
using TransactionJournal.Application.ManualTrades;
using TransactionJournal.Domain;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Infrastructure.UseCases;

/// <summary>
/// Проверки use-case сервиса ручных сделок на реальном SQLite-хранилище:
/// внесение во «Входящие» и сразу в конструкцию, свободная правка атрибутов
/// без смены привязки, удаление из любого места, разрешение символа справочником
/// и все отказы валидации — будущая дата, неположительные количество и цена,
/// отрицательная комиссия, нераспознанная сторона; охрана неизменяемости
/// биржевых записей при правке и удалении.
/// </summary>
[TestClass]
public class ManualTradeServiceTests
{
	private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;
	private JournalSyncStore _store = null!;
	private ScriptedInstrumentSource _source = null!;
	private FixedTimeProvider _time = null!;
	private ManualTradeService _service = null!;
	private ConstructionService _constructions = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-manual-trade-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		_store = new JournalSyncStore(CreateOptions(), _time = new FixedTimeProvider(Now));
		_source = new ScriptedInstrumentSource();
		_service = new ManualTradeService(
			CreateOptions(),
			new ManualInstrumentResolver(_source, _store),
			_time);
		_constructions = new ConstructionService(CreateOptions());
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
	[Description("Сделка вносится во «Входящие»: привязки нет, отметки времени проставлены, сеть не вызывалась")]
	// Сделка вносится пользователем и без указания конструкции попадает во «Входящие».
	// Traceability: openspec:domain/manual-trades#requirement-manual-trade-entry
	public async Task TryIfTradeCreatedIntoInbox()
	{
		// Arrange: символ BTCUSDT уже известен справочнику — сеть не нужна.
		// Требование: известный символ разрешается справочником без запроса биржи.
		await _store.WriteAsync("linear", [LinearInstrument("BTCUSDT")]);
		var draft = Draft("BTCUSDT", "Buy", 0.5m, 42000m, 1.5m, executedAt: Now.AddMinutes(-30));

		// Act: вносим сделку без конструкции.
		var trade = await _service.CreateAsync(draft);

		// Assert: сделка хранится во «Входящих» с полным набором атрибутов;
		// справочник уже знал символ — сетевых запросов не было.
		Assert.That(trade.Id, Is.GreaterThan(0));
		Assert.That(trade.ConstructionId, Is.Null);
		Assert.That(trade.Symbol, Is.EqualTo("BTCUSDT"));
		Assert.That(trade.Side, Is.EqualTo("Buy"));
		Assert.That(trade.Quantity, Is.EqualTo(0.5m));
		Assert.That(trade.Price, Is.EqualTo(42000m));
		Assert.That(trade.FeeUsdt, Is.EqualTo(1.5m));
		Assert.That(trade.ExecutedAt, Is.EqualTo(Now.AddMinutes(-30)));
		Assert.That(trade.CreatedAt, Is.EqualTo(Now));
		Assert.That(trade.UpdatedAt, Is.EqualTo(Now));
		Assert.That(_source.Queries, Is.Empty);
	}

	[TestMethod]
	[Description("Сделка вносится сразу в конструкцию и принадлежит ровно ей")]
	// При создании с указанием конструкции сделка немедленно попадает в неё,
	// минуя «Входящие».
	// Traceability: openspec:domain/manual-trades#scenario-manual-trade-created-into-construction
	public async Task TryIfTradeCreatedIntoConstruction()
	{
		// Arrange: конструкция-цель и известный справочнику символ.
		await _store.WriteAsync("linear", [LinearInstrument("BTCUSDT")]);
		var construction = await _constructions.CreateAsync("Ручной диапазон", 1000m);

		// Act: вносим сделку сразу в конструкцию.
		var trade = await _service.CreateAsync(Draft(
			"BTCUSDT", "Buy", 1m, 42000m, 0m,
			constructionId: construction.Id,
			executedAt: Now.AddMinutes(-10)));

		// Assert: привязка хранится в сырье сделки — конструкция ровно одна.
		Assert.That(trade.ConstructionId, Is.EqualTo(construction.Id));

		using var db = new JournalDbContext(CreateOptions());
		Assert.That(db.ManualTrades.Single().ConstructionId, Is.EqualTo(construction.Id));
	}

	[TestMethod]
	[Description("Неизвестный символ пополняет справочник до сохранения сделки")]
	// Отсутствующий символ запрашивается у биржи и сохраняется в справочник
	// до принятия сделки — сделка остаётся материализуемой.
	// Traceability: openspec:domain/manual-trades#scenario-unknown-symbol-enriches-catalog
	public async Task TryIfUnknownSymbolEnrichesCatalogBeforeSave()
	{
		// Arrange: опционный символ отсутствует в справочнике, биржа отдаёт спецификацию.
		var symbol = "BTC-29DEC23-45000-C";
		_source.Add("option", OptionInstrument(symbol));

		// Act: вносим сделку с неизвестным справочнику символом.
		var trade = await _service.CreateAsync(Draft(symbol, "Buy", 2m, 100m, 0m, executedAt: Now.AddMinutes(-5)));

		// Assert: сделка сохранена, спецификация записана в сырой справочник.
		Assert.That(trade.Id, Is.GreaterThan(0));
		Assert.That(_source.Queries, Has.Count.EqualTo(1));

		using var db = new JournalDbContext(CreateOptions());
		Assert.That(db.RawInstruments.Single().Symbol, Is.EqualTo(symbol));
	}

	[TestMethod]
	[Description("Правка заменяет атрибуты целиком, обновляет отметку и сохраняет привязку")]
	// Ручная сделка доступна правке; изменения подхватываются производными
	// при чтении, а привязка правкой не переносится.
	// Traceability: openspec:domain/manual-trades#requirement-manual-trade-edit-delete
	public async Task TryIfTradeEditedWithAttributesAndKeptBinding()
	{
		// Arrange: сделка в конструкции, часы сдвинуты вперёд для новой отметки правки.
		await _store.WriteAsync("linear", [LinearInstrument("BTCUSDT")]);
		var first = await _constructions.CreateAsync("Первая", 500m);
		var second = await _constructions.CreateAsync("Вторая", 500m);
		var trade = await _service.CreateAsync(Draft(
			"BTCUSDT", "Buy", 1m, 42000m, 1m,
			constructionId: first.Id,
			executedAt: Now.AddMinutes(-20)));
		_time.UtcNow = Now.AddHours(2);

		// Act: правим все атрибуты; привязка в черновике указывает на другую конструкцию.
		var edited = await _service.EditAsync(trade.Id, Draft(
			"BTCUSDT", "Sell", 2m, 43000m, 2.5m,
			constructionId: second.Id,
			executedAt: Now.AddMinutes(-15)));

		// Assert: атрибуты заменены, отметка правки обновлена, привязка не тронута —
		// перенос между конструкциями выполняют команды привязки, а не правка.
		Assert.That(edited.Side, Is.EqualTo("Sell"));
		Assert.That(edited.Quantity, Is.EqualTo(2m));
		Assert.That(edited.Price, Is.EqualTo(43000m));
		Assert.That(edited.FeeUsdt, Is.EqualTo(2.5m));
		Assert.That(edited.ExecutedAt, Is.EqualTo(Now.AddMinutes(-15)));
		Assert.That(edited.CreatedAt, Is.EqualTo(Now));
		Assert.That(edited.UpdatedAt, Is.EqualTo(Now.AddHours(2)));
		Assert.That(edited.ConstructionId, Is.EqualTo(first.Id));
	}

	[TestMethod]
	[Description("Удаление убирает сделку из любого места — из «Входящих» и из конструкции")]
	// Удалённая сделка исчезает из журнала, производные пересчитываются при чтении.
	// Traceability: openspec:domain/manual-trades#scenario-manual-trade-delete-anywhere
	public async Task TryIfTradeDeletedFromAnyPlace()
	{
		// Arrange: одна сделка во «Входящих» и одна в конструкции.
		await _store.WriteAsync("linear", [LinearInstrument("BTCUSDT")]);
		var construction = await _constructions.CreateAsync("На удаление", 300m);
		var inInbox = await _service.CreateAsync(Draft("BTCUSDT", "Buy", 1m, 42000m, 0m, executedAt: Now.AddMinutes(-3)));
		var inConstruction = await _service.CreateAsync(Draft(
			"BTCUSDT", "Sell", 1m, 42500m, 0m,
			constructionId: construction.Id,
			executedAt: Now.AddMinutes(-2)));

		// Act: удаляем обе.
		await _service.DeleteAsync(inInbox.Id);
		await _service.DeleteAsync(inConstruction.Id);

		// Assert: ручных сделок в журнале не осталось.
		using var db = new JournalDbContext(CreateOptions());
		Assert.That(db.ManualTrades.ToList(), Is.Empty);
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(-1)]
	[Description("Правка несуществующей ручной сделки отклоняется")]
	[ExpectedException(typeof(ManualTradeNotFoundException))]
	public async Task ThrowOnEditUnknownTrade(long id)
	{
		// Arrange: известный справочнику символ, но идентификатору сделки не соответствует ничто.
		await _store.WriteAsync("linear", [LinearInstrument("BTCUSDT")]);

		// Act — Assert: правка неизвестной записи отклоняется.
		await _service.EditAsync(id, Draft("BTCUSDT", "Buy", 1m, 42000m, 0m, executedAt: Now.AddMinutes(-1)));
	}

	[TestMethod]
	[Description("Удаление несуществующей ручной сделки отклоняется")]
	[ExpectedException(typeof(ManualTradeNotFoundException))]
	public async Task ThrowOnDeleteUnknownTrade()
	{
		// Arrange — Act — Assert: удалять нечего, запись отсутствует.
		await _service.DeleteAsync(123);
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	[Description("Правка и удаление по идентификатору биржевой записи отклоняются")]
	// Синхронизированная сделка неизменяема: правка и удаление отклоняются.
	// Traceability: openspec:domain/manual-trades#scenario-synced-trade-edit-rejected
	public async Task ThrowOnChangeExchangeRecord(bool edit)
	{
		// Arrange: биржевая запись с известным идентификатором строки сырья.
		const long exchangeRecordId = 41;
		using (var seedDb = new JournalDbContext(CreateOptions()))
		{
			seedDb.RawExecutions.Add(new RawExecution
			{
				Id = exchangeRecordId,
				ExecId = "exec-exchange-1",
				Category = "linear",
				Symbol = "BTCUSDT",
				ExecTimeMs = Now.AddMinutes(-30).ToUnixTimeMilliseconds(),
				PayloadJson = "{}",
				FetchedAt = Now,
			});
			seedDb.SaveChanges();
		}

		// Act: правка и удаление адресуют биржевую запись — обе операции отклоняются.
		ManualTradeImmutableException? interruption;
		if (edit)
		{
			interruption = Assert.ThrowsAsync<ManualTradeImmutableException>(() => _service.EditAsync(
				exchangeRecordId,
				Draft("BTCUSDT", "Buy", 1m, 42000m, 0m, executedAt: Now.AddMinutes(-1))));
		}
		else
		{
			interruption = Assert.ThrowsAsync<ManualTradeImmutableException>(() => _service.DeleteAsync(exchangeRecordId));
		}

		// Assert: отказ называет запись и объясняет неизменяемость биржевых данных.
		Assert.That(interruption!.RecordId, Is.EqualTo(exchangeRecordId));
		Assert.That(interruption.Message, Does.Contain("неизменяема"));

		// Биржевая запись осталась на месте — охрана не имела побочных эффектов.
		using var db = new JournalDbContext(CreateOptions());
		Assert.That(db.RawExecutions.Count(execution => execution.Id == exchangeRecordId), Is.EqualTo(1));
	}

	[TestMethod]
	[Description("Внесение в несуществующую конструкцию отклоняется")]
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public async Task ThrowOnCreateIntoUnknownConstruction()
	{
		// Arrange — Act — Assert: конструкция с идентификатором 999 не существует.
		await _service.CreateAsync(Draft("BTCUSDT", "Buy", 1m, 42000m, 0m, constructionId: 999, executedAt: Now.AddMinutes(-1)));
	}

	[TestMethod]
	[DataRow(1)]
	[DataRow(0.5)]
	[Description("Будущая дата сделки отклоняется с человекочитаемой причиной")]
	// Дата сделки позже текущего момента отклоняется системой.
	// Traceability: openspec:domain/manual-trades#scenario-future-date-rejected
	public async Task ThrowOnFutureExecutedAt(double hoursAhead)
	{
		// Arrange: форма с датой в будущем относительно часов сервиса.
		var draft = Draft("BTCUSDT", "Buy", 1m, 42000m, 0m, executedAt: Now.AddHours(hoursAhead));

		// Act
		var interruption = Assert.ThrowsAsync<ManualTradeValidationException>(() => _service.CreateAsync(draft));

		// Assert: сообщение называет причину — недопустимая дата.
		Assert.That(interruption!.Message, Does.Contain("будущем"));

		// Ничего не сохранено: валидация идёт до обращений к хранилищу.
		using var db = new JournalDbContext(CreateOptions());
		Assert.That(db.ManualTrades.ToList(), Is.Empty);
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(-0.5)]
	[Description("Неположительное количество отклоняется")]
	// Количество сделки должно быть больше нуля.
	// Traceability: openspec:domain/manual-trades#scenario-nonpositive-qty-price-rejected
	[ExpectedException(typeof(ManualTradeValidationException))]
	public async Task ThrowOnNonPositiveQuantity(double quantity)
	{
		// Arrange — Act — Assert: ноль и отрицательное количество отклоняются.
		await _service.CreateAsync(Draft("BTCUSDT", "Buy", (decimal)quantity, 42000m, 0m, executedAt: Now.AddMinutes(-1)));
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(-1)]
	[Description("Неположительная цена отклоняется")]
	// Цена сделки должна быть больше нуля.
	// Traceability: openspec:domain/manual-trades#scenario-nonpositive-qty-price-rejected
	[ExpectedException(typeof(ManualTradeValidationException))]
	public async Task ThrowOnNonPositivePrice(double price)
	{
		// Arrange — Act — Assert: ноль и отрицательная цена отклоняются.
		await _service.CreateAsync(Draft("BTCUSDT", "Buy", 1m, (decimal)price, 0m, executedAt: Now.AddMinutes(-1)));
	}

	[TestMethod]
	[Description("Отрицательная комиссия отклоняется")]
	// Комиссия не может быть отрицательной: ноль означает отсутствие комиссии.
	// Traceability: openspec:domain/manual-trades#requirement-manual-trade-validation
	[ExpectedException(typeof(ManualTradeValidationException))]
	public async Task ThrowOnNegativeFee()
	{
		// Arrange — Act — Assert: комиссия меньше нуля нарушает правило формы.
		await _service.CreateAsync(Draft("BTCUSDT", "Buy", 1m, 42000m, -0.5m, executedAt: Now.AddMinutes(-1)));
	}

	[TestMethod]
	[DataRow("buy")]
	[DataRow("Hold")]
	[DataRow("")]
	[Description("Нераспознанная сторона сделки отклоняется")]
	// Сторона хранится биржевым значением: проектная не распознаёт «buy» и «Hold»,
	// а проекция журнала отклонила бы их при чтении.
	// Traceability: openspec:domain/manual-trades#requirement-manual-trade-validation
	[ExpectedException(typeof(ManualTradeValidationException))]
	public async Task ThrowOnUnknownSide(string side)
	{
		// Arrange — Act — Assert: сторона вне {Buy, Sell} отклоняется при внесении.
		await _service.CreateAsync(Draft("BTCUSDT", side, 1m, 42000m, 0m, executedAt: Now.AddMinutes(-1)));
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	[Description("Пустой инструмент отклоняется без обращения к хранилищу и сети")]
	[ExpectedException(typeof(ArgumentException))]
	public async Task ThrowOnBlankSymbol(bool blankSymbol)
	{
		// Arrange — Act — Assert: символ без значения не позволяет ни сверки
		// со справочником, ни сохранения сделки.
		await _service.CreateAsync(Draft(blankSymbol ? string.Empty : "  ", "Buy", 1m, 42000m, 0m, executedAt: Now.AddMinutes(-1)));
	}

	[TestMethod]
	[Description("Отказ разрешения символа оставляет журнал без новой сделки")]
	// Отказ сети или биржи отменяет создание сделки человекочитаемой причиной.
	// Traceability: openspec:domain/manual-trades#scenario-offline-new-symbol-rejected
	public async Task ThrowOnSymbolResolutionFailureWithoutSaving()
	{
		// Arrange: неизвестный символ, биржа недоступна.
		_source.Fail("BTC-29DEC23-45000-C", new HttpRequestException("Соединение с биржей не установлено."));

		// Act
		var interruption = Assert.ThrowsAsync<ManualSymbolResolutionException>(
			() => _service.CreateAsync(Draft("BTC-29DEC23-45000-C", "Buy", 1m, 100m, 0m, executedAt: Now.AddMinutes(-1))));

		// Assert: сообщение человекочитаемо и сделка не сохранена.
		Assert.That(interruption!.Message, Does.Contain("Сделка не сохранена"));

		using var db = new JournalDbContext(CreateOptions());
		Assert.That(db.ManualTrades.ToList(), Is.Empty);
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	[Description("Null-зависимости конструктора отклоняются")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullConstructorDependency(bool nullOptions)
	{
		// Arrange — Act — Assert: опции контекста и разрешитель символа обязательны.
		if (nullOptions)
		{
			new ManualTradeService(null!, new ManualInstrumentResolver(_source, _store));
		}
		else
		{
			new ManualTradeService(CreateOptions(), null!);
		}
	}

	#region Помощники

	/// <summary>Черновик формы ручного ввода с общими значениями по умолчанию.</summary>
	private static ManualTradeDraft Draft(
		string symbol,
		string side,
		decimal quantity,
		decimal price,
		decimal feeUsdt,
		long? constructionId = null,
		DateTimeOffset? executedAt = null) => new()
	{
		ExecutedAt = executedAt ?? Now.AddMinutes(-1),
		Symbol = symbol,
		Side = side,
		Quantity = quantity,
		Price = price,
		FeeUsdt = feeUsdt,
		ConstructionId = constructionId,
	};

	/// <summary>Спецификация линейного инструмента для сеяния справочника.</summary>
	private static BybitInstrumentInfo LinearInstrument(string symbol) => new()
	{
		Symbol = symbol,
		BaseCoin = "BTC",
		OptionsType = null,
		DeliveryTimeMs = null,
	};

	/// <summary>Спецификация опционного инструмента для сеяния справочника.</summary>
	private static BybitInstrumentInfo OptionInstrument(string symbol) => new()
	{
		Symbol = symbol,
		BaseCoin = "BTC",
		OptionsType = "Call",
		DeliveryTimeMs = 1_703_836_800_000L,
	};

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>
	/// Фиктивный источник спецификаций: раздаёт заготовленные записи по фильтру символа,
	/// помнит все запросы и умеет отказывать заданным символом. Незнакомый символ
	/// получает пустой ответ — как биржа на фильтр несуществующего инструмента.
	/// </summary>
	private sealed class ScriptedInstrumentSource : IBybitInstrumentSource
	{
		private readonly Dictionary<string, BybitInstrumentInfo> _instruments = new(StringComparer.Ordinal);
		private readonly Dictionary<string, Exception> _failures = new(StringComparer.Ordinal);

		/// <summary>Все запросы источника в порядке отправления.</summary>
		public List<BybitInstrumentInfoQuery> Queries { get; } = [];

		public void Add(string category, BybitInstrumentInfo instrument)
		{
			_instruments[instrument.Symbol] = instrument;
		}

		/// <summary>Настраивает отказ источника на запрос спецификации конкретного символа.</summary>
		public void Fail(string symbol, Exception error)
		{
			_failures[symbol] = error;
		}

		public Task<BybitPagedResponse<BybitInstrumentInfo>> GetInstrumentInfoAsync(
			BybitInstrumentInfoQuery query,
			CancellationToken cancellationToken = default)
		{
			Queries.Add(query);
			if (query.Symbol is not null && _failures.TryGetValue(query.Symbol, out var error))
			{
				throw error;
			}

			IReadOnlyList<BybitInstrumentInfo> list = query.Symbol is not null && _instruments.TryGetValue(query.Symbol, out var instrument)
				? [instrument]
				: [];
			return Task.FromResult(new BybitPagedResponse<BybitInstrumentInfo> { List = list });
		}
	}

	#endregion
}
