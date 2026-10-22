namespace TransactionJournal.Hints.Engine;

using TransactionJournal.Domain.Materialization;
using TransactionJournal.Hints.Ports;

/// <summary>Нога конструкции — открытая позиция опциона с частями, разобранными из символа.</summary>
internal sealed record OptionLeg
{
	/// <summary>Символ опциона.</summary>
	public required string Symbol { get; init; }

	/// <summary>Базовый актив из символа, например BTC.</summary>
	public required string BaseCoin { get; init; }

	/// <summary>Дата экспирации из символа; только дата, без времени доставки.</summary>
	public required DateTime ExpiryDate { get; init; }

	/// <summary>Страйк из символа.</summary>
	public required decimal Strike { get; init; }

	/// <summary>Тип опциона из символа.</summary>
	public required OptionType Type { get; init; }

	/// <summary>Знаковый остаток позиции: покупка положительна, продажа отрицательна.</summary>
	public required decimal Residual { get; init; }
}

/// <summary>
/// Разбор позиций конструкции на опционные ноги для триггеров: символ опциона
/// разбирается парсером Domain; неразбираемые позиции в ноги не попадают —
/// триггер оценивает только то, что однозначно распознано.
/// Traceability: openspec:hints/engine-pass#requirement-engine-deterministic-selection
/// </summary>
internal static class ConstructionLegs
{
	/// <summary>Опционные ноги конструкции: открытые позиции с разбираемым символом опциона.</summary>
	/// <param name="construction">Конструкция снимка журнала.</param>
	public static IReadOnlyList<OptionLeg> Options(ConstructionView construction)
		=> construction.Positions
			.Select(position => position.Symbol is null
				? null
				: OptionSymbolParser.TryParse(position.Symbol, out var parts) && parts is not null
					? new OptionLeg
					{
						Symbol = position.Symbol,
						BaseCoin = parts.BaseCoin,
						ExpiryDate = parts.ExpiryDate,
						Strike = parts.Strike,
						Type = parts.Type,
						Residual = position.Residual,
					}
					: null)
			.OfType<OptionLeg>()
			.ToArray();

	/// <summary>Котировка спота базового актива: линейный фьючерс «{база}USDT» из партии котировок.</summary>
	/// <param name="quotes">Партия котировок прохода.</param>
	/// <param name="baseCoin">Базовый актив опционной ноги.</param>
	/// <param name="spot">Котировка спота или null, когда котировки нет.</param>
	public static bool TrySpot(QuoteBatch quotes, string baseCoin, out decimal spot)
		=> quotes.Quotes.TryGetValue(baseCoin + "USDT", out spot);
}
