using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Bybit;
using TransactionJournal.Sync;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Sync;

/// <summary>
/// Проверки прохода 30-дневного окна delivery-истории на фиктивном шлюзе:
/// курсорная пагинация до исчерпания страниц и дедуп известных записей по ключу
/// symbol + deliveryTime на пересекающихся окнах.
/// </summary>
[TestClass]
public class DeliveryWindowPassTests
{
	private static readonly long WindowStartMs = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
	private static readonly long WindowEndMs = WindowStartMs + DeliveryWindowPass.MaxWindowMs;

	private ScriptedDeliveryGateway _gateway = null!;
	private FakeDeliveryKeyProbe _knownKeyProbe = null!;
	private DeliveryWindowPass _pass = null!;

	[TestInitialize]
	public void Initialize()
	{
		_gateway = new ScriptedDeliveryGateway();
		_knownKeyProbe = new FakeDeliveryKeyProbe();
		_pass = new DeliveryWindowPass(_gateway, _knownKeyProbe);
	}

	[TestMethod]
	[Description("Проход листает окно delivery курсорной пагинацией до исчерпания страниц биржи")]
	public async Task TryIfWindowPassPagesWithCursorUntilExhaustion()
	{
		// Arrange: три страницы — две с записями и курсорами, третья пустая без курсора.
		// Требование: delivery-записи читаются 30-дневными окнами с курсорной пагинацией.
		// Traceability: change:add-bybit-sync/design#d4
		// Traceability: openspec:sync/bybit-history#requirement-expiry-delivery-closing-entries
		_gateway.Enqueue(new BybitPagedResponse<BybitDeliveryRecord>
		{
			List = [Delivery("BTC-2JAN26-100000-C", WindowEndMs - 1), Delivery("ETH-2JAN26-4000-P", WindowEndMs - 2)],
			NextPageCursor = "cursor-2",
		});
		_gateway.Enqueue(new BybitPagedResponse<BybitDeliveryRecord>
		{
			List = [Delivery("BTC-2JAN26-100000-P", WindowStartMs + 1)],
			NextPageCursor = "cursor-3",
		});
		_gateway.Enqueue(new BybitPagedResponse<BybitDeliveryRecord>());

		// Act
		var result = await _pass.RunAsync(Window());

		// Assert: проход запросил ровно три страницы, перенося курсор ответа в следующий запрос.
		Assert.That(_gateway.Queries, Has.Count.EqualTo(3));
		Assert.That(_gateway.Queries[0].Cursor, Is.Null);
		Assert.That(_gateway.Queries[1].Cursor, Is.EqualTo("cursor-2"));
		Assert.That(_gateway.Queries[2].Cursor, Is.EqualTo("cursor-3"));

		// Assert: каждый запрос несёт категорию, границы окна и размер страницы биржи.
		Assert.That(_gateway.Queries[0].Category, Is.EqualTo("option"));
		Assert.That(_gateway.Queries[0].StartTimeMs, Is.EqualTo(WindowStartMs));
		Assert.That(_gateway.Queries[0].EndTimeMs, Is.EqualTo(WindowEndMs));
		Assert.That(_gateway.Queries[0].Limit, Is.EqualTo(50));

		// Assert: все записи окна новые и возвращены в порядке выдачи.
		Assert.That(result.NewDeliveries.Select(delivery => delivery.Symbol),
			Is.EqualTo(new[] { "BTC-2JAN26-100000-C", "ETH-2JAN26-4000-P", "BTC-2JAN26-100000-P" }));
		Assert.That(result.AllDeliveries, Has.Count.EqualTo(3));
		Assert.That(result.PagesFetched, Is.EqualTo(3));

		// Assert: известность спрашивалась пачками только по непустым страницам.
		Assert.That(_knownKeyProbe.AskedBatches, Has.Count.EqualTo(2));
	}

	[TestMethod]
	[Description("Пересекающееся окно отсеивает известные записи по ключу symbol + deliveryTime")]
	public async Task TryIfOverlappingWindowFiltersKnownDeliveriesBySymbolAndDeliveryTime()
	{
		// Arrange: предыдущий прогон уже сохранил запись BTC с временем доставки T1 и
		// запись ETH с временем T2. Повторное окно перекрывает прошлое и возвращает
		// обе снова, но ETH — уже с другим временем доставки (новая запись).
		// Требование: перекрытие окна не создаёт дублей — известные записи пропускаются,
		// обрабатываются только новые.
		// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
		// Traceability: openspec:sync/bybit-history#scenario-window-overlap-no-duplicates
		var btcTimeMs = WindowEndMs - 3;
		var ethOldTimeMs = WindowEndMs - 2;
		var ethNewTimeMs = WindowEndMs - 1;
		_knownKeyProbe.Know(
			new DeliveryRecordKey("BTC-2JAN26-100000-C", btcTimeMs),
			new DeliveryRecordKey("ETH-2JAN26-4000-P", ethOldTimeMs));
		_gateway.Enqueue(new BybitPagedResponse<BybitDeliveryRecord>
		{
			List =
			[
				Delivery("ETH-2JAN26-4000-P", ethNewTimeMs),
				Delivery("BTC-2JAN26-100000-C", btcTimeMs),
				Delivery("ETH-2JAN26-4000-P", ethOldTimeMs),
			],
		});

		// Act
		var result = await _pass.RunAsync(Window());

		// Assert: все три записи получены от биржи, но новой признана только пара
		// ETH с новым временем — совпадение символа без совпадения времени не дубль.
		Assert.That(result.AllDeliveries, Has.Count.EqualTo(3));
		Assert.That(result.NewDeliveries.Select(delivery => new DeliveryRecordKey(delivery.Symbol, delivery.DeliveryTimeMs)),
			Is.EqualTo(new[] { new DeliveryRecordKey("ETH-2JAN26-4000-P", ethNewTimeMs) }));

		// Assert: известность спрашивалась одной пачкой по всей странице.
		Assert.That(_knownKeyProbe.AskedBatches, Has.Count.EqualTo(1));
		Assert.That(_knownKeyProbe.AskedBatches[0], Has.Count.EqualTo(3));
	}

	[TestMethod]
	[DataRow(0L)]
	[DataRow(-1L)]
	[DataRow(2592000001L)]
	[Description("Пустое, перевёрнутое или длиннее тридцати дней окно отклоняется до запросов")]
	[ExpectedException(typeof(ArgumentOutOfRangeException))]
	public void ThrowOnWindowOutsideThirtyDayLimit(long spanMs)
	{
		// Arrange: эндпоинт delivery-record ограничивает окно тридцатью днями и требует конец позже начала.
		var window = new DeliveryWindow { Category = "option", StartMs = WindowStartMs, EndMs = WindowStartMs + spanMs };

		// Act — некорректное окно прерывается до обращения к шлюзу.
		try
		{
			_pass.RunAsync(window).GetAwaiter().GetResult();
		}
		catch (ArgumentOutOfRangeException)
		{
			// Assert: шлюз не запрашивался.
			Assert.That(_gateway.Queries, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Окно без категории отклоняется до запросов")]
	[ExpectedException(typeof(ArgumentException))]
	public void ThrowOnWindowWithoutCategory()
	{
		// Arrange: категория — обязательный параметр эндпоинта delivery-record.
		var window = new DeliveryWindow { Category = " ", StartMs = WindowStartMs, EndMs = WindowEndMs };

		// Act — пустая категория прерывается до обращения к шлюзу.
		try
		{
			_pass.RunAsync(window).GetAwaiter().GetResult();
		}
		catch (ArgumentException)
		{
			// Assert: шлюз не запрашивался.
			Assert.That(_gateway.Queries, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Null-окно отклоняется до запросов")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullWindow()
	{
		// Arrange — Act: окно с границами — обязательный вход прохода.
		try
		{
			_pass.RunAsync(null!).GetAwaiter().GetResult();
		}
		catch (ArgumentNullException)
		{
			// Assert: шлюз не запрашивался.
			Assert.That(_gateway.Queries, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[DataRow(0)]
	[DataRow(51)]
	[Description("Размер страницы вне диапазона [1..50] отклоняется до запросов")]
	[ExpectedException(typeof(ArgumentOutOfRangeException))]
	public void ThrowOnPageSizeOutsideDocumentedRange(int pageSize)
	{
		// Arrange: биржа ограничивает размер страницы delivery-record диапазоном [1..50].
		var options = new DeliveryWindowPassOptions { PageSize = pageSize };

		// Act — выход за диапазон прерывается до обращения к шлюзу.
		try
		{
			_pass.RunAsync(Window(), options).GetAwaiter().GetResult();
		}
		catch (ArgumentOutOfRangeException)
		{
			// Assert: шлюз не запрашивался.
			Assert.That(_gateway.Queries, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	[Description("Нулевая зависимость конструктора отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullConstructorDependency(bool nullGateway)
	{
		// Arrange — Act: шлюз и проверка известных delivery-ключей обязательны для прохода.
		if (nullGateway)
		{
			new DeliveryWindowPass(null!, _knownKeyProbe);
		}
		else
		{
			new DeliveryWindowPass(_gateway, null!);
		}
	}

	#region Помощники

	private static DeliveryWindow Window(string category = "option") => new()
	{
		Category = category,
		StartMs = WindowStartMs,
		EndMs = WindowEndMs,
	};

	private static BybitDeliveryRecord Delivery(string symbol, long deliveryTimeMs) => new()
	{
		Symbol = symbol,
		DeliveryTimeMs = deliveryTimeMs,
		Side = "Sell",
		Position = 0.1m,
		Strike = 100000m,
	};

	#endregion

	#region Фиктивные зависимости

	/// <summary>
	/// Фиктивный шлюз биржи для delivery-запросов: раздаёт заготовленные страницы по
	/// порядку и помнит все запросы прохода. При исчерпании сценария отвечает пустой
	/// страницей без курсора.
	/// </summary>
	private sealed class ScriptedDeliveryGateway : IBybitHistoryGateway
	{
		private readonly Queue<BybitPagedResponse<BybitDeliveryRecord>> _pages = new();

		/// <summary>Все delivery-запросы прохода в порядке отправления.</summary>
		public List<BybitDeliveryRecordQuery> Queries { get; } = [];

		public void Enqueue(BybitPagedResponse<BybitDeliveryRecord> page)
		{
			_pages.Enqueue(page);
		}

		public Task<BybitPagedResponse<BybitExecution>> GetExecutionListAsync(
			BybitExecutionListQuery query,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException("Проход delivery-истории не запрашивает историю исполнения.");
		}

		public Task<BybitPagedResponse<BybitDeliveryRecord>> GetDeliveryRecordAsync(
			BybitDeliveryRecordQuery query,
			CancellationToken cancellationToken = default)
		{
			Queries.Add(query);
			var page = _pages.Count > 0 ? _pages.Dequeue() : new BybitPagedResponse<BybitDeliveryRecord>();
			return Task.FromResult(page);
		}
	}

	/// <summary>
	/// Фиктивная проверка известных delivery-ключей: помнит сохранённые пары
	/// symbol + deliveryTime и все пачки, о которых спрашивал проход.
	/// </summary>
	private sealed class FakeDeliveryKeyProbe : IDeliveryKnownKeyProbe
	{
		private readonly HashSet<DeliveryRecordKey> _knownKeys = new();

		/// <summary>Все пачки ключей, о которых спрашивал проход, в порядке обращений.</summary>
		public List<IReadOnlyCollection<DeliveryRecordKey>> AskedBatches { get; } = [];

		public void Know(params DeliveryRecordKey[] keys)
		{
			_knownKeys.UnionWith(keys);
		}

		public Task<IReadOnlySet<DeliveryRecordKey>> FindKnownAsync(
			IReadOnlyCollection<DeliveryRecordKey> keys,
			CancellationToken cancellationToken = default)
		{
			AskedBatches.Add(keys);
			var known = new HashSet<DeliveryRecordKey>(keys.Where(_knownKeys.Contains));
			return Task.FromResult<IReadOnlySet<DeliveryRecordKey>>(known);
		}
	}

	#endregion
}
