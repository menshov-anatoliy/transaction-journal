namespace TransactionJournal.Analytics;

/// <summary>
/// Внешняя корректировка PnL как вход калькулятора метрик конструкции: знаковое
/// слагаемое результата без сделки — результат торгового робота либо ручная
/// поправка пользователя. Дата переносится вместе с суммой для полноты доменной
/// записи; в арифметику итога входит только сумма.
// Traceability: openspec:domain/constructions#requirement-external-pnl-adjustments
/// </summary>
public sealed record ConstructionPnLAdjustment
{
	/// <summary>Дата корректировки.</summary>
	public required DateTimeOffset Date { get; init; }

	/// <summary>Знаковая сумма корректировки в USDT: положительная увеличивает результат, отрицательная — уменьшает.</summary>
	public required decimal AmountUsdt { get; init; }
}
