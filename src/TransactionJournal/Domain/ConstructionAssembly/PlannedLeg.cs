using TransactionJournal.Materialization;

namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Нога конструкции в плане сборки: символ опциона с разобранными свойствами
/// (страйк, доска, тип) и итоговый знаковый остаток после прогона. Нога с
/// нулевым остатком сохраняется в плане — она участвует в правиле закрытия
/// «все ноги нулевые» и в именовании.
// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#requirement-deterministic-option-assembly
/// </summary>
public sealed record PlannedLeg
{
	/// <summary>Символ опциона, например ETH-25SEP26-2100-C-USDT.</summary>
	public required string Symbol { get; init; }

	/// <summary>Страйк из символа опциона.</summary>
	public required decimal Strike { get; init; }

	/// <summary>Доска — дата экспирации из символа опциона; только дата, без времени доставки.</summary>
	public required DateTime BoardExpiryDate { get; init; }

	/// <summary>Тип опциона: колл или пут.</summary>
	public required OptionType Type { get; init; }

	/// <summary>Итоговый знаковый остаток ноги после прогона сборки.</summary>
	public required decimal Quantity { get; init; }
}
