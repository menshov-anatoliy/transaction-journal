using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Domain.Bybit;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Application.Sync;
using TransactionJournal.Application.ManualTrades;
using TransactionJournal.Tests.Infrastructure.Bybit;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Application.ManualTrades;

/// <summary>
/// Проверки разрешения символа ручной сделки на реальном SQLite-хранилище
/// и фиктивном источнике спецификаций: известный справочнику символ разрешается
/// без сетевых запросов, неизвестный запрашивается у биржи фильтром по символу
/// и сохраняется тем же путём, что и при синхронизации, отказ сети или биржи
/// оборачивается человекочитаемой ошибкой без сохранения сделки.
/// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
/// Traceability: change:manual-journal-management/specs/sync/bybit-history/spec#scenario-manual-symbol-populates-catalog
/// </summary>
[TestClass]
public class ManualInstrumentResolverTests
{
	private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;
	private JournalSyncStore _store = null!;
	private ScriptedInstrumentSource _source = null!;
	private ManualInstrumentResolver _resolver = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-manual-symbol-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		_store = new JournalSyncStore(CreateOptions(), new ManualTimeProvider());
		_source = new ScriptedInstrumentSource();
		_resolver = new ManualInstrumentResolver(_source, _store);
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
	[Description("Известный справочнику символ разрешается без сетевых запросов")]
	public async Task TryIfKnownSymbolResolvesWithoutNetworkCalls()
	{
		// Arrange: спецификация BTCUSDT уже сохранена в справочнике синхронизацией.
		// Требование: сверка символа идёт со справочником, известный символ не зовёт сеть.
		await _store.WriteAsync("linear", [Instrument("linear", "BTCUSDT")]);

		// Act
		var resolution = await _resolver.ResolveAsync("BTCUSDT");

		// Assert: категория взята из формы символа, пополнения не было, биржа не опрошена.
		Assert.That(resolution.Symbol, Is.EqualTo("BTCUSDT"));
		Assert.That(resolution.Category, Is.EqualTo("linear"));
		Assert.That(resolution.RegisteredNow, Is.False);
		Assert.That(_source.Queries, Is.Empty);
	}

	[TestMethod]
	[Description("Неизвестный опционный символ запрашивается фильтром по символу и сохраняется в справочник")]
	public async Task TryIfUnknownOptionSymbolIsFetchedAndSaved()
	{
		// Arrange: опционный символ отсутствует в справочнике, биржа отдаёт его
		// спецификацию. Требование: символ ручного ввода пополняет справочник
		// публичным эндпоинтом до принятия сделки.
		// Traceability: change:manual-journal-management/specs/sync/bybit-history/spec#scenario-manual-symbol-populates-catalog
		var symbol = "BTC-29DEC23-45000-C";
		_source.Add("option", Instrument("option", symbol));

		// Act
		var resolution = await _resolver.ResolveAsync(symbol);

		// Assert: спецификация вставлена в сырое хранилище справочника с категорией
		// option и отметкой загрузки хранилища.
		Assert.That(resolution.RegisteredNow, Is.True);
		Assert.That(resolution.Category, Is.EqualTo("option"));

		using (var db = new JournalDbContext(CreateOptions()))
		{
			var saved = db.RawInstruments.Single();
			Assert.That(saved.Symbol, Is.EqualTo(symbol));
			Assert.That(saved.Category, Is.EqualTo("option"));
			Assert.That(saved.FetchedAt, Is.EqualTo(Now));
			Assert.That(saved.PayloadJson, Does.Contain("optionsType"));
		}

		// Повторное разрешение известного символа не зовёт сеть второй раз.
		var repeated = await _resolver.ResolveAsync(symbol);
		Assert.That(repeated.RegisteredNow, Is.False);
		Assert.That(_source.Queries, Has.Count.EqualTo(1));
		Assert.That(_source.Queries.Single().Category, Is.EqualTo("option"));
		Assert.That(_source.Queries.Single().Symbol, Is.EqualTo(symbol));
	}

	[TestMethod]
	[Description("Символ вне формата опциона запрашивается линейной категорией и сохраняется как linear")]
	public async Task TryIfNonOptionSymbolIsFetchedUnderLinearCategory()
	{
		// Arrange: линейный символ BTCUSDT не разбирается форматом опциона, поэтому
		// адресуется категории linear — как их хранит справочник синхронизации.
		_source.Add("linear", Instrument("linear", "BTCUSDT"));

		// Act
		var resolution = await _resolver.ResolveAsync("BTCUSDT");

		// Assert
		Assert.That(resolution.RegisteredNow, Is.True);
		Assert.That(_source.Queries.Single().Category, Is.EqualTo("linear"));

		using var db = new JournalDbContext(CreateOptions());
		var saved = db.RawInstruments.Single();
		Assert.That(saved.Symbol, Is.EqualTo("BTCUSDT"));
		Assert.That(saved.Category, Is.EqualTo("linear"));
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	[Description("Отказ сети или биржи на запросе спецификации возвращает человекочитаемую ошибку без сохранения")]
	public async Task ThrowOnSourceFailureDuringSpecRequest(bool networkFailure)
	{
		// Arrange: источник отвечает отказом сети либо отказом биржи на запрос
		// спецификации неизвестного символа. Требование: отказ сети или биржи —
		// отказ создания сделки с человекочитаемой причиной.
		// Traceability: change:manual-journal-management/design#d3
		_source.Fail(
			"BTC-29DEC23-45000-C",
			networkFailure
				? new HttpRequestException("Соединение с биржей не установлено.")
				: new BybitApiException(110023, "The contract is not available for trades"));

		// Act
		var interruption = Assert.ThrowsAsync<ManualSymbolResolutionException>(
			() => _resolver.ResolveAsync("BTC-29DEC23-45000-C"));

		// Assert: сообщение человекочитаемо — называет символ и объясняет, что сделка
		// не сохранена; исходная ошибка сохранена внутренней; справочник не пополнен.
		Assert.That(interruption!.Symbol, Is.EqualTo("BTC-29DEC23-45000-C"));
		Assert.That(interruption.Message, Does.Contain("BTC-29DEC23-45000-C"));
		Assert.That(interruption.Message, Does.Contain("Сделка не сохранена"));
		Assert.That(interruption.InnerException, Is.Not.Null);

		using var db = new JournalDbContext(CreateOptions());
		Assert.That(db.RawInstruments.ToList(), Is.Empty);
	}

	[TestMethod]
	[Description("Биржа без спецификации символа возвращает человекочитаемую ошибку без сохранения")]
	public async Task ThrowOnExchangeWithoutSpecification()
	{
		// Arrange: биржа отвечает пустым списком на фильтр несуществующего символа —
		// пополнять справочник нечем, сделка отклоняется человекочитаемой причиной.
		// Act
		var interruption = Assert.ThrowsAsync<ManualSymbolResolutionException>(
			() => _resolver.ResolveAsync("BAD-SYMBOL"));

		// Assert: сообщение объясняет, что спецификации нет и сделка не сохранена;
		// справочник остался пустым.
		Assert.That(interruption!.Message, Does.Contain("BAD-SYMBOL"));
		Assert.That(interruption.Message, Does.Contain("Сделка не сохранена"));
		Assert.That(interruption.InnerException, Is.Null);

		using var db = new JournalDbContext(CreateOptions());
		Assert.That(db.RawInstruments.ToList(), Is.Empty);
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	[Description("Пустой символ отклоняется без обращения к хранилищу и бирже")]
	[ExpectedException(typeof(ArgumentException))]
	public async Task ThrowOnBlankSymbol(bool blankSymbol)
	{
		// Arrange — Act — Assert: символ без значения не позволяет адресовать
		// ни сверку со справочником, ни запрос спецификации бирже.
		await _resolver.ResolveAsync(blankSymbol ? string.Empty : "  ");
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	[Description("Null-зависимость конструктора отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullConstructorDependency(bool nullSource)
	{
		// Arrange — Act — Assert: источник спецификаций и хранилище справочника
		// обязательны.
		if (nullSource)
		{
			new ManualInstrumentResolver(null!, _store);
		}
		else
		{
			new ManualInstrumentResolver(_source, null!);
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
