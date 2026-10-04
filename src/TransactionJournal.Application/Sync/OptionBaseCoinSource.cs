using TransactionJournal.Application.Bybit;

using TransactionJournal.Domain.Sync;
namespace TransactionJournal.Application.Sync;

/// <summary>
/// Список базовых активов опционной доски как объединение независимых источников:
/// живое перечисление instruments-info?category=option (безфильтровый ответ покрывает
/// только доску по умолчанию), активы сырьевого хранилища — префиксы символов опционных
/// записей исполнения и delivery-записей — и конфигурируемый список дополнительных
/// активов для делистнутых досок с торговой историей. Повторные проходы активов
/// дедуплицируются хранилищем по execId.
/// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
/// </summary>
public sealed class OptionBaseCoinSource : IOptionBaseCoinSource
{
	/// <summary>Размер страницы справочника инструментов: предел эндпоинта instruments-info.</summary>
	public const int PageSize = 1000;

	private readonly IBybitInstrumentSource _source;
	private readonly IOptionRawBaseCoinReader? _rawBaseCoins;
	private readonly IReadOnlyList<string> _extraBaseCoins;

	/// <summary>Создаёт источник активов над справочником инструментов, читателем сырья и списком дополнений.</summary>
	/// <param name="source">Источник спецификаций instruments-info.</param>
	/// <param name="rawBaseCoins">Читатель активов сырьевого хранилища; null — сырья в перечне нет.</param>
	/// <param name="extraBaseCoins">Дополнительные базовые активы из конфигурации приложения; null — дополнений нет.</param>
	/// <exception cref="ArgumentNullException">Источник не задан.</exception>
	public OptionBaseCoinSource(
		IBybitInstrumentSource source,
		IOptionRawBaseCoinReader? rawBaseCoins = null,
		IEnumerable<string>? extraBaseCoins = null)
	{
		_source = source ?? throw new ArgumentNullException(nameof(source));
		_rawBaseCoins = rawBaseCoins;
		_extraBaseCoins = (extraBaseCoins ?? [])
			.Where(coin => string.IsNullOrWhiteSpace(coin) == false)
			.Select(Normalize)
			.ToList();
	}

	/// <summary>
	/// Собирает базовые активы опционной доски: активы текущего справочника биржи,
	/// сырьевого хранилища и конфигурационных дополнений объединяются в отсортированном
	/// множестве — порядок списка стабилен между запусками, поэтому логи проходов
	/// сравнимы, а порядок запросов воспроизводим.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой после всех повторов.</exception>
	public async Task<IReadOnlyList<string>> GetBaseCoinsAsync(CancellationToken cancellationToken = default)
	{
		// Отсортированное множество само дедуплицирует повторы: одна страница справочника
		// содержит тысячи инструментов одного актива, активы сырья пересекаются со
		// справочником, а конфигурация может называть уже листингованный актив вторым разом.
		var baseCoins = new SortedSet<string>(StringComparer.Ordinal);

		string? cursor = null;
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// Страница запрашивается предельным размером без фильтров: задача — вся доска,
			// поэтому фильтры baseCoin и expDate здесь только размывали бы перечисление.
			var page = await _source.GetInstrumentInfoAsync(
				new BybitInstrumentInfoQuery { Category = "option", Limit = PageSize, Cursor = cursor },
				cancellationToken).ConfigureAwait(false);

			foreach (var instrument in page.List)
			{
				if (string.IsNullOrWhiteSpace(instrument.BaseCoin) == false)
				{
					baseCoins.Add(Normalize(instrument.BaseCoin));
				}
			}

			// Пустой nextPageCursor — биржа исчерпала страницы справочника.
			if (page.HasNextPage == false)
			{
				break;
			}

			cursor = page.NextPageCursor;
		}

		// Активы сырья дополняют справочник: безфильтровое перечисление покрывает только
		// доску по умолчанию, а префиксы уже загруженных опционных записей называют
		// и остальные доски, по которым у аккаунта есть история.
		// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
		// Traceability: openspec:sync/bybit-history#scenario-new-base-coin-picked-up
		if (_rawBaseCoins is not null)
		{
			foreach (var rawCoin in await _rawBaseCoins.GetRawBaseCoinsAsync(cancellationToken).ConfigureAwait(false))
			{
				baseCoins.Add(Normalize(rawCoin));
			}
		}

		// Дополнительные активы из конфигурации покрывают делистнутые доски: их нет
		// в текущем справочнике, но торговая история по ним у биржи запрашивается.
		// Traceability: openspec:sync/bybit-history#scenario-delisted-base-coin-from-config
		foreach (var extraCoin in _extraBaseCoins)
		{
			baseCoins.Add(extraCoin);
		}

		return baseCoins.ToList();
	}

	#region Вспомогательные методы

	/// <summary>Приводит имя актива к каноничной форме: trim и верхний регистр.</summary>
	private static string Normalize(string baseCoin) => baseCoin.Trim().ToUpperInvariant();

	#endregion
}
