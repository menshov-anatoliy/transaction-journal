namespace TransactionJournal.Application.Analytics;

/// <summary>
/// Саммари времени жизни конструкции по её открытым опционным ногам: минимум
/// полных суток до ближайшей экспирации, нетто-дневной распад со знаком,
/// суммарная временная и суммарная внутренняя стоимость открытых опционных
/// остатков. Нога без
/// рыночных данных исключается из распада и стоимости прочерком, не подменяя
/// остальные слагаемые нулями; без открытых опционных ног саммари отсутствует
/// целиком — сводка по фьючерсным остаткам не строится.
// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
/// </summary>
public sealed record ConstructionLifetimeSummary
{
	/// <summary>Остаток полных суток до ближайшей экспирации среди открытых опционных ног.</summary>
	public required int DaysToNearestExpiry { get; init; }

	/// <summary>Нетто-дневной распад — сумма распадов опционных ног со знаком; null, пока ни у одной ноги распад недоступен.</summary>
	public decimal? NetDailyDecayUsdt { get; init; }

	/// <summary>Суммарная временная стоимость открытых опционных остатков; null, пока ни у одной ноги стоимость недоступна.</summary>
	public decimal? TotalTimeValueUsdt { get; init; }

	// Внутренняя стоимость публикуется рядом с временной по той же схеме
	// деградации: нога без тикеров исключается прочерком, полное отсутствие
	// величины — null, а не нулевая сумма.
	// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
	/// <summary>Суммарная внутренняя стоимость открытых опционных остатков; null, пока ни у одной ноги стоимость недоступна.</summary>
	public decimal? TotalIntrinsicValueUsdt { get; init; }
}
