namespace TransactionJournal.Api.Constructions;

using TransactionJournal.Application.Analytics;

/// <summary>
/// Сериализация пары «состояние реального риска + величина» в стабильные
/// строки JSON-контракта API. Единая точка для всех эндпоинтов раздела:
/// строки контракта («finite» / «unbounded» / «unavailable») и инвариант
/// «число только при конечном риске» определены один раз, а не повторяются
/// в каждом файле эндпоинтов.
/// </summary>
// Формат строк и инвариант заданы design-решением D1 change'а
// show-unbounded-finresult-risk и delta-спецификацией analytics/performance.
// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
// Traceability: change:show-unbounded-finresult-risk/design#d1
public static class RealRiskContract
{
	/// <summary>
	/// Сериализует состояние реального риска в строку контракта, проверяя
	/// инвариант пары: конечный риск несёт число, неограниченный хвост и
	/// неполные данные идут без числа. Нарушенная пара read-модели не
	/// просачивается в контракт — сериализация падает с явной ошибкой.
	/// </summary>
	/// <param name="status">Состояние риска из метрик аналитики.</param>
	/// <param name="usdt">Величина риска в USDT из той же метрики.</param>
	/// <returns>Стабильная строка состояния в JSON-контракте API.</returns>
	// Формат строк задан design-решением D1 и delta-спецификацией
	// analytics/performance: число выдаётся только конечному риску.
	// Traceability: openspec:analytics/performance#scenario-real-risk-unbounded-is-null
	// Traceability: openspec:analytics/performance#scenario-real-risk-missing-open-price-unavailable
	// Traceability: change:show-unbounded-finresult-risk/design#d1
	public static string SerializeStatus(RealRiskStatus status, decimal? usdt)
	{
		var pairIsValid = status switch
		{
			RealRiskStatus.Finite => usdt is not null,
			RealRiskStatus.Unbounded => usdt is null,
			RealRiskStatus.Unavailable => usdt is null,
			_ => false,
		};

		if (pairIsValid == false)
		{
			throw new InvalidOperationException(
				$"Нарушен инвариант контракта реального риска: статус {status} несовместим с величиной {(usdt is null ? "null" : usdt.Value.ToString("0.##"))} USDT.");
		}

		return status switch
		{
			RealRiskStatus.Finite => "finite",
			RealRiskStatus.Unbounded => "unbounded",
			RealRiskStatus.Unavailable => "unavailable",
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
		};
	}
}
