using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Bybit;
using TransactionJournal.Data;
using TransactionJournal.Domain.Bybit;
using TransactionJournal.Domain.Data;
using TransactionJournal.Sync;
using TransactionJournal.Tests.Bybit;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Sync;

/// <summary>
/// Проверки пополнения справочника инструментов на реальном SQLite-хранилище
/// и фиктивном источнике спецификаций: неизвестные символы записей синхронизации
/// запрашиваются у биржи фильтром по символу и сохраняются в сыром виде,
/// известные не перечитываются.
/// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
/// </summary>
[TestClass]
public class InstrumentReferenceSyncTests
{
	private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;
	private JournalSyncStore _store = null!;
	private ScriptedInstrumentSource _source = null!;
	private InstrumentReferenceSync _sync = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-instrument-ref-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		_store = new JournalSyncStore(CreateOptions(), new ManualTimeProvider());
		_source = new ScriptedInstrumentSource();
		_sync = new InstrumentReferenceSync(_source, _store);
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
	[Description("Неизвестные символы запрашиваются фильтром по символу и сохраняются в справочник со счётчиком запуска")]
	public async Task TryIfUnknownSymbolsAreFetchedBySymbolFilterAndSaved()
	{
		// Arrange: в записях синхронизации встречены три пары — одна повторяется; запуск
		// открыт, чтобы счётчик новых инструментов вёлся строкой SyncRun.
		// Требование: неизвестный инструмент из загружаемых записей пополняет справочник
		// спецификацией биржи до материализации сделок.
		// Traceability: openspec:sync/bybit-history#scenario-new-instrument-registered
		var run = await _store.StartAsync(SyncRunMode.Backfill);
		_source.Add("option", Instrument("option", "BTC-29DEC23-45000-C"));
		_source.Add("linear", Instrument("linear", "BTCUSDT"));

		// Act
		var result = await _sync.SyncAsync(
			[
				("option", "BTC-29DEC23-45000-C"),
				("linear", "BTCUSDT"),
				("option", "BTC-29DEC23-45000-C"),
			],
			run);

		// Assert: обе спецификации вставлены, повтор пары не дал третьей строки.
		Assert.That(result.InsertedCount, Is.EqualTo(2));
		Assert.That(result.UnresolvedSymbols, Is.Empty);

		// Биржа опрошена фильтром по символу: по одному запросу на каждый инструмент,
		// категория передана параметром запроса.
		Assert.That(_source.Queries, Has.Count.EqualTo(2));
		Assert.That(_source.Queries.Count(query => query.Symbol == "BTC-29DEC23-45000-C"), Is.EqualTo(1));
		Assert.That(_source.Queries.Count(query => query.Symbol == "BTCUSDT"), Is.EqualTo(1));
		Assert.That(_source.Queries.Single(query => query.Symbol == "BTCUSDT").Category, Is.EqualTo("linear"));

		// Спецификации сохранены в сыром виде с категорией и отметкой загрузки.
		using (var db = new JournalDbContext(CreateOptions()))
		{
			var saved = db.RawInstruments.OrderBy(instrument => instrument.Symbol).ToList();
			Assert.That(saved.Select(instrument => instrument.Symbol),
				Is.EqualTo(new[] { "BTC-29DEC23-45000-C", "BTCUSDT" }));
			Assert.That(saved.All(instrument => instrument.FetchedAt == Now), Is.True);
			Assert.That(saved.Single(instrument => instrument.Symbol == "BTCUSDT").Category, Is.EqualTo("linear"));
			Assert.That(saved.Single(instrument => instrument.Symbol == "BTC-29DEC23-45000-C").Category, Is.EqualTo("option"));
			Assert.That(saved.Single(instrument => instrument.Symbol == "BTC-29DEC23-45000-C").PayloadJson,
				Does.Contain("optionsType"));
		}

		// Счётчик запуска продвинут той же транзакцией, что и вставка.
		Assert.That(run.NewInstruments, Is.EqualTo(2));

		// Повторный вызов с теми же символами не перечитывает биржу и не дублирует строки.
		var repeated = await _sync.SyncAsync([("option", "BTC-29DEC23-45000-C"), ("linear", "BTCUSDT")], run);
		Assert.That(repeated.InsertedCount, Is.Zero);
		Assert.That(_source.Queries, Has.Count.EqualTo(2));
	}

	[TestMethod]
	[Description("Известные справочнику символы пропускаются без запросов к бирже")]
	public async Task TryIfKnownSymbolsAreSkippedWithoutGatewayCalls()
	{
		// Arrange: спецификация BTCUSDT уже сохранена в справочнике ранее.
		await _store.WriteAsync("linear", [Instrument("linear", "BTCUSDT")]);
		var run = await _store.StartAsync(SyncRunMode.Incremental);

		// Act
		var result = await _sync.SyncAsync([("linear", "BTCUSDT")], run);

		// Assert: источник спецификаций не опрошен, строка не вставлена, счётчик не продвинут.
		Assert.That(result.InsertedCount, Is.Zero);
		Assert.That(result.UnresolvedSymbols, Is.Empty);
		Assert.That(_source.Queries, Is.Empty);
		Assert.That(run.NewInstruments, Is.Zero);
	}

	[TestMethod]
	[Description("Пустая коллекция пар не создаёт запросов к хранилищу и бирже")]
	public async Task TryIfEmptyCollectionIsNoOp()
	{
		// Arrange — Act
		var result = await _sync.SyncAsync([]);

		// Assert
		Assert.That(result.InsertedCount, Is.Zero);
		Assert.That(result.UnresolvedSymbols, Is.Empty);
		Assert.That(_source.Queries, Is.Empty);
	}

	[TestMethod]
	[Description("Отказ 110023 по одному символу не прерывает пополнение остальных инструментов")]
	public async Task TryIfContractUnavailableSkipsSymbolWithoutBreakingOthers()
	{
		// Arrange: биржа отвергает спецификацию делистнутого опциона отказом 110023
		// «контракт недоступен для торговли», спецификации двух других инструментов
		// доступны; запуск открыт для счётчика новых инструментов.
		// Требование: отказ 110023 пропускает спецификацию без ретрая, фиксирует символ
		// в перечне неразрешённых и не прерывает пополнение остальных инструментов.
		// Traceability: openspec:sync/bybit-history#scenario-unavailable-instrument-spec-skipped
		var run = await _store.StartAsync(SyncRunMode.Backfill);
		_source.Add("option", Instrument("option", "BTC-29DEC23-45000-C"));
		_source.Add("linear", Instrument("linear", "BTCUSDT"));
		_source.Fail("ETH-29DEC23-2000-C", new BybitApiException(110023, "The contract is not available for trades"));

		// Act
		var result = await _sync.SyncAsync(
			[
				("option", "BTC-29DEC23-45000-C"),
				("option", "ETH-29DEC23-2000-C"),
				("linear", "BTCUSDT"),
			],
			run);

		// Assert: две доступные спецификации вставлены, счётчик продвинут только ими;
		// делистнутый символ перечислен без повторов.
		Assert.That(result.InsertedCount, Is.EqualTo(2));
		Assert.That(result.UnresolvedSymbols, Is.EqualTo(new[] { "ETH-29DEC23-2000-C" }));
		Assert.That(run.NewInstruments, Is.EqualTo(2));

		// Биржа опрошена по всем трём символам, но отказавший запрос не повторялся.
		Assert.That(_source.Queries, Has.Count.EqualTo(3));
		Assert.That(_source.Queries.Count(query => query.Symbol == "ETH-29DEC23-2000-C"), Is.EqualTo(1));

		// Спецификация делистнутого инструмента в справочник не попала.
		using var db = new JournalDbContext(CreateOptions());
		Assert.That(db.RawInstruments.Select(instrument => instrument.Symbol).ToList(),
			Is.EquivalentTo(new[] { "BTC-29DEC23-45000-C", "BTCUSDT" }));
	}

	[TestMethod]
	[Description("Посторонний код ошибки биржи на запросе спецификации пробрасывается как раньше")]
	public void ThrowOnForeignErrorCodeDuringSpecRequest()
	{
		// Arrange: биржа отвечает отказом с кодом, отличным от 110023, — этот отказ
		// не относится к недоступным контрактам и обязан прервать пополнение.
		_source.Fail("BTC-29DEC23-45000-C", new BybitApiException(10006, "превышение частоты запросов"));

		// Act — Assert
		var interruption = Assert.ThrowsAsync<BybitApiException>(
			() => _sync.SyncAsync([("option", "BTC-29DEC23-45000-C")]));
		Assert.That(interruption!.RetCode, Is.EqualTo(10006));
	}

	[TestMethod]
	[Description("Null-коллекция пар отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public async Task ThrowOnNullInstrumentsCollection()
	{
		// Arrange — Act — Assert
		await _sync.SyncAsync(null!);
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	[Description("Пара с пустой категорией или символом отклоняется")]
	[ExpectedException(typeof(ArgumentException))]
	public async Task ThrowOnBlankCategoryOrSymbol(bool blankCategory)
	{
		// Arrange — Act — Assert: запись без категории или символа не позволяет
		// адресовать запрос спецификации бирже.
		if (blankCategory)
		{
			await _sync.SyncAsync([("", "BTCUSDT")]);
		}
		else
		{
			await _sync.SyncAsync([("linear", " ")]);
		}
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	[Description("Null-зависимость конструктора отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullConstructorDependency(bool nullSource)
	{
		// Arrange — Act — Assert: источник спецификаций и хранилище обязательны.
		if (nullSource)
		{
			new InstrumentReferenceSync(null!, _store);
		}
		else
		{
			new InstrumentReferenceSync(_source, null!);
		}
	}

	#region Помощники

	private static BybitInstrumentInfo Instrument(string category, string symbol) => new()
	{
		Symbol = symbol,
		BaseCoin = "BTC",
		OptionsType = category == "option" ? "Call" : null,
		DeliveryTimeMs = category == "option" ? 1_703_836_800_000L : null,
	};

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>
	/// Фиктивный источник спецификаций: раздаёт заготовленные записи по фильтру символа
	/// и помнит все запросы. Незнакомый символ получает пустой ответ — как биржа
	/// на фильтр несуществующего инструмента.
	/// </summary>
	private sealed class ScriptedInstrumentSource : IBybitInstrumentSource
	{
		private readonly Dictionary<string, BybitInstrumentInfo> _instruments = new(StringComparer.Ordinal);
		private readonly Dictionary<string, BybitApiException> _failures = new(StringComparer.Ordinal);

		/// <summary>Все запросы источника в порядке отправления.</summary>
		public List<BybitInstrumentInfoQuery> Queries { get; } = [];

		public void Add(string category, BybitInstrumentInfo instrument)
		{
			_instruments[instrument.Symbol] = instrument;
		}

		/// <summary>Настраивает отказ биржи на запрос спецификации конкретного символа.</summary>
		public void Fail(string symbol, BybitApiException error)
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
