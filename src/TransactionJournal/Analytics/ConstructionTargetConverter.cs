namespace TransactionJournal.Analytics;

using TransactionJournal.Domain.Data;

/// <summary>
/// Величины плановой границы результата конструкции в обеих единицах: введённая
/// единица первоисточника и вычисленная от капитала вторая единица. Отсутствие
/// величины — null: и незаданный параметр, и невычисленная вторая единица
/// передаются читающему слою отсутствующими, без нулевых подмен.
/// </summary>
/// <param name="Percent">Величина границы в процентах от выделенного капитала; null, если не введена и не вычислена.</param>
/// <param name="Usdt">Величина границы в USDT; null, если не введена и не вычислена.</param>
public sealed record ConstructionTargetAmounts(decimal? Percent, decimal? Usdt)
{
	/// <summary>Величины отсутствующего параметра: обе единицы невычислены.</summary>
	public static readonly ConstructionTargetAmounts None = new(null, null);
}

/// <summary>
/// Чистый вычислитель незаполненной единицы плановой границы результата (риска
/// или профита) от текущего выделенного капитала: значение в процентах
/// переводится в USDT умножением на капитал, значение в USDT — в проценты
/// делением на капитал. Вычислитель без хранилища и состояния: каждая величина —
/// функция введённой пары «значение + единица» и текущего капитала, поэтому
/// правка капитала меняет вычисленную единицу очередным чтением без следов
/// прежнего пересчёта.
/// </summary>
// Незаполненная единица вычисляется при чтении и в базе не хранится: введённая
// единица остаётся первоисточником параметра.
// Traceability: openspec:analytics/performance#requirement-risk-profit-unit-conversion
public static class ConstructionTargetConverter
{
	/// <summary>
	/// Вычисляет величины границы в обеих единицах. Незаданный параметр (без
	/// значения или без единицы) не даёт ни одной величины; незаданный или
	/// нулевой капитал оставляет незаполненную единицу невычисленной —
	/// возвращается только введённая единица первоисточника.
	/// </summary>
	/// <param name="value">Введённое значение границы; положительное число либо null у отсутствующего параметра.</param>
	/// <param name="unit">Единица ввода границы; null у отсутствующего параметра.</param>
	/// <param name="allocatedCapitalUsdt">Текущий выделенный капитал конструкции в USDT; null, когда капитал не задан.</param>
	/// <returns>Величины границы в процентах и USDT; отсутствующие величины — null.</returns>
	public static ConstructionTargetAmounts Convert(decimal? value, TargetUnit? unit, decimal? allocatedCapitalUsdt)
	{
		// Незаданный параметр не даёт ни одной величины: ни процентной, ни USDT.
		// Traceability: openspec:analytics/performance#scenario-unset-param-no-values
		if (value is null || unit is null)
		{
			return ConstructionTargetAmounts.None;
		}

		// Незаданный или нулевой капитал базы конвертации не образует: читающий
		// слой возвращает только введённую единицу первоисточника.
		// Traceability: openspec:analytics/performance#scenario-conversion-needs-capital
		if (allocatedCapitalUsdt is null or 0m)
		{
			return unit.Value == TargetUnit.Percent
				? new ConstructionTargetAmounts(value, null)
				: new ConstructionTargetAmounts(null, value);
		}

		var capital = allocatedCapitalUsdt.Value;
		return unit.Value == TargetUnit.Percent
			? new ConstructionTargetAmounts(value, value.Value / 100m * capital)
			: new ConstructionTargetAmounts(value.Value / capital * 100m, value);
	}
}
