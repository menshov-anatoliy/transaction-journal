namespace TransactionJournal.Api.Constructions;

using TransactionJournal.Application.Analytics;

/// <summary>
/// Сериализация тройки «состояние реального риска + величина риска + худший
/// исход» в стабильные строки JSON-контракта API. Единая точка для всех
/// эндпоинтов раздела: строки контракта («finite» / «unbounded» /
/// «unavailable») и инвариант «число только при конечном риске» определены
/// один раз, а не повторяются в каждом файле эндпоинтов.
/// </summary>
// Формат строк и инвариант заданы design-решением D1 change'а
// show-unbounded-finresult-risk и delta-спецификацией analytics/performance.
// Худший исход публикуется рядом с парой риска по design-решению D1 change'а
// rework-finresult-border-worst-case: одно правило числа для обеих величин.
// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
// Traceability: change:show-unbounded-finresult-risk/design#d1
// Traceability: change:rework-finresult-border-worst-case/design#d1
public static class RealRiskContract
{
	/// <summary>
	/// Сериализует состояние реального риска в строку контракта, проверяя
	/// инвариант тройки: конечный риск несёт и величину риска, и знаковый
	/// худший исход, неограниченный хвост и неполные данные идут без обоих
	/// чисел. Нарушенная тройка read-модели не просачивается в контракт —
	/// сериализация падает с явной ошибкой.
	/// </summary>
	/// <param name="status">Состояние риска из метрик аналитики.</param>
	/// <param name="usdt">Величина риска в USDT из той же метрики.</param>
	/// <param name="worstCaseUsdt">Худший исход конструкции в USDT из той же метрики.</param>
	/// <returns>Стабильная строка состояния в JSON-контракте API.</returns>
	// Формат строк задан design-решением D1 и delta-спецификацией
	// analytics/performance: число выдаётся только конечному риску, и то же
	// правило переиспользуется худшим исходом со статусом реального риска.
	// Traceability: openspec:analytics/performance#scenario-real-risk-unbounded-is-null
	// Traceability: openspec:analytics/performance#scenario-real-risk-missing-open-price-unavailable
	// Traceability: openspec:analytics/performance#scenario-worst-case-null-with-status
	// Traceability: change:show-unbounded-finresult-risk/design#d1
	// Traceability: change:rework-finresult-border-worst-case/design#d1
	public static string SerializeStatus(RealRiskStatus status, decimal? usdt, decimal? worstCaseUsdt)
	{
		var pairIsValid = status switch
		{
			RealRiskStatus.Finite => usdt is not null && worstCaseUsdt is not null,
			RealRiskStatus.Unbounded => usdt is null && worstCaseUsdt is null,
			RealRiskStatus.Unavailable => usdt is null && worstCaseUsdt is null,
			_ => false,
		};

		if (pairIsValid == false)
		{
			throw new InvalidOperationException(
				$"Нарушен инвариант контракта реального риска: статус {status} несовместим с величинами риск {(usdt is null ? "null" : usdt.Value.ToString("0.##"))} и худший исход {(worstCaseUsdt is null ? "null" : worstCaseUsdt.Value.ToString("0.##"))} USDT.");
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
