using TransactionJournal.Domain.Data;

namespace TransactionJournal.Application.Analytics;

/// <summary>
/// Расчётные данные метрик времени жизни одной конструкции на входе встраиваемого
/// вычислителя: открытые опционные ноги с разбором символа из справочника
/// инструментов, свежие тикеры текущего чтения и история рыночных снимков.
/// Собирается читающим слоем поверх уже существующих запросов котировок — второй
/// сетевой слой и собственное расписание не вводятся; история передаётся общей
/// для всех конструкций, вычислитель сам выбирает снимки нужных символов.
// Traceability: change:add-option-lifetime-metrics/design#d2
/// </summary>
public sealed record OptionLifetimeInput
{
	/// <summary>Открытые опционные ноги конструкции: символ, страйк, тип, время доставки и чистый остаток.</summary>
	public required IReadOnlyList<OptionLegLifetimeInput> Legs { get; init; }

	/// <summary>Свежие тикеры текущего чтения по символам: котировка, индексная цена и тета; у отсутствующего символа рыночных величин не будет.</summary>
	public required IReadOnlyDictionary<string, InstrumentQuoteSnapshot> Quotes { get; init; }

	/// <summary>История рыночных снимков — материал прироста распада; проверка давности остаётся в вычислителе.</summary>
	public required IReadOnlyList<InstrumentMarketSnapshot> SnapshotHistory { get; init; }
}