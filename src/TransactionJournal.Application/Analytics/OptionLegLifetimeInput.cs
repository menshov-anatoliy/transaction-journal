using TransactionJournal.Domain.Materialization;

namespace TransactionJournal.Application.Analytics;

/// <summary>
/// Открытая опционная нога на входе вычислителя метрик времени жизни: символ,
/// страйк и тип опциона, момент доставки инструмента и чистый остаток позиции.
/// Исходные данные собирает вызывающий слой из справочника инструментов
/// синхронизации и метрик позиций — вычислитель сам символы не разбирает,
/// остатков не агрегирует и сетевых запросов не делает.
// Traceability: change:add-option-lifetime-metrics/design#d2
/// </summary>
public sealed record OptionLegLifetimeInput
{
	/// <summary>Символ опционного инструмента, например BTC-27DEC24-2800-C.</summary>
	public required string Symbol { get; init; }

	/// <summary>Страйк опциона из справочника инструментов.</summary>
	public required decimal Strike { get; init; }

	/// <summary>Тип опциона: колл или пут.</summary>
	public required OptionType Type { get; init; }

	/// <summary>Момент доставки (экспирации) инструмента из справочника, UTC.</summary>
	public required DateTimeOffset DeliveryTime { get; init; }

	/// <summary>Чистый остаток позиции: положителен у лонга, отрицателен у шорта, ноль у закрытой.</summary>
	public required decimal Residual { get; init; }
}
