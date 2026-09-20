using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using TransactionJournal.Bybit;
using TransactionJournal.Data;

namespace TransactionJournal.Materialization;

/// <summary>
/// Справочник инструментов, построенный из сырых записей RawInstrument (спецификации
/// эндпоинта instruments-info) как неизменяемый снимок: материализатор выводит
/// доменные представления из сырья без сетевых запросов, а обновление справочника —
/// это новая загрузка сырых записей и пересборка снимка.
/// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
/// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
/// Traceability: change:add-bybit-sync/design#d2
/// </summary>
public sealed class InstrumentCatalog
{
	private readonly Dictionary<string, InstrumentCatalogEntry> _entries;

	/// <summary>
	/// Создаёт справочник из сырых спецификаций инструментов, разбирая JSON каждой записи.
	/// </summary>
	/// <param name="rawInstruments">Сырые записи справочника из хранилища журнала.</param>
	/// <exception cref="ArgumentNullException">Исходные записи не заданы.</exception>
	/// <exception cref="FormatException">JSON-спецификация какой-либо записи некорректен.</exception>
	public InstrumentCatalog(IEnumerable<RawInstrument> rawInstruments)
	{
		ArgumentNullException.ThrowIfNull(rawInstruments);
		_entries = new Dictionary<string, InstrumentCatalogEntry>(StringComparer.Ordinal);
		foreach (var rawInstrument in rawInstruments)
		{
			// Последняя запись символа выигрывает: уникальный индекс хранилища и так
			// исключает дубли, а снимок строится из упорядоченного источника.
			_entries[rawInstrument.Symbol] = ParseEntry(rawInstrument);
		}
	}

	/// <summary>Количество инструментов в справочнике.</summary>
	public int Count => _entries.Count;

	/// <summary>Ищет запись справочника по символу инструмента.</summary>
	/// <param name="symbol">Символ инструмента.</param>
	/// <param name="entry">Найденная запись или null.</param>
	public bool TryGet(string symbol, [NotNullWhen(true)] out InstrumentCatalogEntry? entry)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
		return _entries.TryGetValue(symbol, out entry!);
	}

	#region Вспомогательные методы

	/// <summary>Разбирает JSON-спецификацию сырой записи в элемент справочника.</summary>
	/// <exception cref="FormatException">JSON-спецификация некорректна.</exception>
	private static InstrumentCatalogEntry ParseEntry(RawInstrument rawInstrument)
	{
		BybitInstrumentInfo info;
		try
		{
			info = JsonSerializer.Deserialize<BybitInstrumentInfo>(rawInstrument.PayloadJson, BybitJson.Options)
				?? throw new FormatException(
					$"Спецификация инструмента {rawInstrument.Symbol} оказалась пустой после разбора JSON.");
		}
		catch (JsonException exception)
		{
			throw new FormatException(
				$"Спецификация инструмента {rawInstrument.Symbol} содержит некорректный JSON: {exception.Message}",
				exception);
		}

		return new InstrumentCatalogEntry
		{
			Symbol = rawInstrument.Symbol,
			Category = rawInstrument.Category,
			BaseCoin = string.IsNullOrWhiteSpace(info.BaseCoin) ? null : info.BaseCoin,
			OptionsType = ParseOptionsType(info.OptionsType),
			// deliveryTime = 0 у бессрочных контрактов означает отсутствие доставки.
			DeliveryTime = info.DeliveryTimeMs is > 0
				? DateTimeOffset.FromUnixTimeMilliseconds(info.DeliveryTimeMs.Value)
				: null,
		};
	}

	// Тип опциона приходит строкой "Call"/"Put"; пустое или незнакомое значение остаётся
	// null — сверка со справочником это не пройдёт, и рассинхрон станет явным.
	private static OptionType? ParseOptionsType(string? optionsType)
	{
		if (string.IsNullOrWhiteSpace(optionsType))
		{
			return null;
		}

		return Enum.TryParse(optionsType, ignoreCase: true, out OptionType parsed) ? parsed : null;
	}

	#endregion
}
