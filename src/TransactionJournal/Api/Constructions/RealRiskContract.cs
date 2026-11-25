namespace TransactionJournal.Api.Constructions;

using TransactionJournal.Application.Analytics;

/// <summary>
/// Сериализация пары «состояние реального риска + величина риска» в стабильные
/// строки JSON-контракта API. Единая точка для всех эндпоинтов раздела: строки
/// контракта («finite» / «unavailable») и инвариант «число только
/// при конечном риске» определены один раз, а не повторяются в каждом файле
/// эндпоинтов.
/// </summary>
// Статусная пара вырождена в finite/unavailable: неограниченный хвост по
// маркам невозможен, строка «unbounded» удалена из контракта (ADR-0025,
// осознанный breaking — единственный потребитель контрактов, SPA репозитория).
// Traceability: issue:#90
// Traceability: adr:docs/adr/0025-real-risk-negative-part-of-current-result.md#consequences
public static class RealRiskContract
{
	/// <summary>
	/// Сериализует состояние реального риска в строку контракта, проверяя
	/// инвариант пары: конечный риск несёт величину, нерассчитанный риск
	/// идёт без числа. Нарушенная пара read-модели
	/// не просачивается в контракт — сериализация падает с явной ошибкой.
	/// </summary>
	/// <param name="status">Состояние риска из метрик аналитики.</param>
	/// <param name="usdt">Величина риска в USDT из той же метрики.</param>
	/// <returns>Стабильная строка состояния в JSON-контракте API.</returns>
	// Число выдаётся только конечному риску: инвариант пары сохранён при
	// сужении перечня состояний (ADR-0025).
	// Traceability: issue:#90
	public static string SerializeStatus(RealRiskStatus status, decimal? usdt)
	{
		var pairIsValid = status switch
		{
			RealRiskStatus.Finite => usdt is not null,
			RealRiskStatus.Unavailable => usdt is null,
			_ => false,
		};

		if (pairIsValid == false)
		{
			throw new InvalidOperationException(
				$"Нарушен инвариант контракта реального риска: статус {status} несовместим с величиной риска {(usdt is null ? "null" : usdt.Value.ToString("0.##"))} USDT.");
		}

		return status switch
		{
			RealRiskStatus.Finite => "finite",
			RealRiskStatus.Unavailable => "unavailable",
			_ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
		};
	}
}
