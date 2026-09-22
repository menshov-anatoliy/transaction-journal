using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Bybit;
using TransactionJournal.Sync;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Sync;

/// <summary>
/// Проверки списка базовых активов опционной доски на фиктивном источнике спецификаций:
/// курсорная пагинация до исчерпания страниц, дедупликация повторных активов и
/// дополнение списка конфигурируемыми делистнутыми активами.
/// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
/// </summary>
[TestClass]
public class OptionBaseCoinSourceTests
{
	private PagedInstrumentSource _source = null!;

	[TestInitialize]
	public void Initialize()
	{
		_source = new PagedInstrumentSource();
	}

	[TestMethod]
	[Description("Список активов листается страницами курсорной пагинацией до исчерпания")]
	public async Task TryIfBaseCoinsArePagedWithCursorUntilExhaustion()
	{
		// Arrange: три страницы — две с курсорами продолжения и последняя без курсора;
		// активы раскиданы по страницам вперемешку, чтобы собрать весь перечисленной доски.
		// Требование: список активов определяется по полному справочнику инструментов.
		// Traceability: openspec:sync/bybit-history#scenario-new-base-coin-picked-up
		_source.Enqueue(Page("cursor-2", ("BTC-24JUN23-56000-C", "BTC"), ("SOL-24JUN23-100-P", "SOL")));
		_source.Enqueue(Page("cursor-3", ("ETH-24JUN23-3000-C", "ETH")));
		_source.Enqueue(Page(null, ("ETH-25DEC23-4000-P", "ETH")));

		// Act
		var baseCoins = await new OptionBaseCoinSource(_source).GetBaseCoinsAsync();

		// Assert: источнику ушло ровно три запроса, курсор предыдущего ответа перенесён
		// в следующий; страница запрашивается предельным размером без фильтров.
		Assert.That(_source.Queries, Has.Count.EqualTo(3));
		Assert.That(_source.Queries[0].Category, Is.EqualTo("option"));
		Assert.That(_source.Queries[0].Limit, Is.EqualTo(OptionBaseCoinSource.PageSize));
		Assert.That(_source.Queries[0].Cursor, Is.Null);
		Assert.That(_source.Queries[0].BaseCoin, Is.Null);
		Assert.That(_source.Queries[1].Cursor, Is.EqualTo("cursor-2"));
		Assert.That(_source.Queries[2].Cursor, Is.EqualTo("cursor-3"));

		// Assert: все активы доски собраны в детерминированном порядке сортировки.
		Assert.That(baseCoins, Is.EqualTo(new[] { "BTC", "ETH", "SOL" }));
	}

	[TestMethod]
	[Description("Пустая опционная доска даёт пустой список активов без лишних запросов")]
	public async Task TryIfEmptyBoardYieldsEmptyList()
	{
		// Arrange: биржа отвечает одной пустой страницей без курсора — доска пуста.
		_source.Enqueue(Page(null));

		// Act
		var baseCoins = await new OptionBaseCoinSource(_source).GetBaseCoinsAsync();

		// Assert: источник опрошен один раз, список активов пуст.
		Assert.That(_source.Queries, Has.Count.EqualTo(1));
		Assert.That(baseCoins, Is.Empty);
	}

	[TestMethod]
	[Description("Повторные активы одной и разных страниц дедуплицируются")]
	public async Task TryIfRepeatedBaseCoinsAreDeduplicated()
	{
		// Arrange: тысячи инструментов доски относятся к одному активу, поэтому имя актива
		// встречается в списке многократно и внутри страницы, и между страницами;
		// требование — distinct активов без дублей в проходах.
		// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
		_source.Enqueue(Page("cursor-2", ("BTC-24JUN23-56000-C", "BTC"), ("BTC-24JUN23-57000-C", "BTC")));
		_source.Enqueue(Page(null, ("btc-25dec23-58000-p", "btc"), ("ETH-24JUN23-3000-C", "ETH")));

		// Act
		var baseCoins = await new OptionBaseCoinSource(_source).GetBaseCoinsAsync();

		// Assert: актив встречается в списке один раз независимо от регистра имени.
		Assert.That(baseCoins, Is.EqualTo(new[] { "BTC", "ETH" }));
	}

	[TestMethod]
	[Description("Дополнительный актив из конфигурации попадает в список")]
	public async Task TryIfExtraBaseCoinFromConfigurationIsIncluded()
	{
		// Arrange: доска листинговых активов не содержит делистнутый LUNA — он приходит
		// только из конфигурации; повтор конфигурационного актива в справочнике не создаёт дубля.
		// Требование: история делистнутых досок догружается конфигурационным списком.
		// Traceability: openspec:sync/bybit-history#scenario-delisted-base-coin-from-config
		_source.Enqueue(Page(null, ("BTC-24JUN23-56000-C", "BTC")));

		// Act
		var baseCoins = await new OptionBaseCoinSource(_source, ["LUNA", "btc"]).GetBaseCoinsAsync();

		// Assert: конфигурационные активы дополнены к доске в общем отсортированном порядке.
		Assert.That(baseCoins, Is.EqualTo(new[] { "BTC", "LUNA" }));
	}

	[TestMethod]
	[Description("Null-источник спецификаций в конструкторе отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullInstrumentSource()
	{
		// Arrange — Act — Assert: источник спецификаций обязателен источнику активов;
		// null-список дополнений легален и означает «дополнений нет».
		new OptionBaseCoinSource(null!);
	}

	#region Помощники

	private static BybitInstrumentInfo Instrument(string symbol, string baseCoin) => new()
	{
		Symbol = symbol,
		BaseCoin = baseCoin,
		OptionsType = "Call",
	};

	private static BybitPagedResponse<BybitInstrumentInfo> Page(
		string? nextCursor,
		params (string Symbol, string BaseCoin)[] instruments)
	{
		return new BybitPagedResponse<BybitInstrumentInfo>
		{
			List = instruments.Select(pair => Instrument(pair.Symbol, pair.BaseCoin)).ToList(),
			NextPageCursor = nextCursor,
		};
	}

	#endregion

	#region Фиктивные зависимости

	/// <summary>
	/// Фиктивный источник спецификаций: раздаёт заготовленные страницы по порядку
	/// и помнит все запросы. При исчерпании сценария отвечает пустой страницей без курсора.
	/// </summary>
	private sealed class PagedInstrumentSource : IBybitInstrumentSource
	{
		private readonly Queue<BybitPagedResponse<BybitInstrumentInfo>> _pages = new();

		/// <summary>Все запросы источника в порядке отправления.</summary>
		public List<BybitInstrumentInfoQuery> Queries { get; } = [];

		public void Enqueue(BybitPagedResponse<BybitInstrumentInfo> page)
		{
			_pages.Enqueue(page);
		}

		public Task<BybitPagedResponse<BybitInstrumentInfo>> GetInstrumentInfoAsync(
			BybitInstrumentInfoQuery query,
			CancellationToken cancellationToken = default)
		{
			Queries.Add(query);
			var page = _pages.Count > 0 ? _pages.Dequeue() : new BybitPagedResponse<BybitInstrumentInfo>();
			return Task.FromResult(page);
		}
	}

	#endregion
}
