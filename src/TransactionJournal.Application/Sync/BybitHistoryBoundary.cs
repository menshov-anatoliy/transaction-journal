namespace TransactionJournal.Application.Sync;

/// <summary>
/// Граница хранения истории биржи: общий для execution- и delivery-движков расчёт
/// самой ранней запрашиваемой даты по серверному времени Bybit и clamp пола перебора
/// истории. Хелпер один на оба движка, чтобы константы границы и арифметика clamp
/// не разъехались между режимами перебора.
/// Traceability: openspec:sync/bybit-history#scenario-floor-clamped-to-exchange-boundary
/// </summary>
public static class BybitHistoryBoundary
{
	/// <summary>Число миллисекунд в сутках.</summary>
	private const long DayMs = 86_400_000L;

	/// <summary>
	/// Глубина хранения истории биржи: «2 года» из текста пограничного отказа,
	/// консервативно принятые за 730 дней.
	/// Traceability: openspec:sync/bybit-history#scenario-floor-clamped-to-exchange-boundary
	/// </summary>
	public const long ExchangeStorageDepthMs = 730 * DayMs;

	/// <summary>
	/// Запас до границы — ширина одного execution-окна: покрывает расхождение
	/// календарных «2 лет» (730–731 день в зависимости от високосности периода)
	/// с константой и дрейф серверного времени между замером и запросами окон,
	/// поэтому пол перебора не доходит до границы вплотную.
	/// Traceability: openspec:sync/bybit-history#scenario-floor-clamped-to-exchange-boundary
	/// </summary>
	public const long BoundarySafetyMarginMs = ExecutionWindowPass.MaxWindowMs;

	/// <summary>
	/// Самая ранняя запрашиваемая дата по часам биржи: от серверного «сейчас»
	/// отнимается глубина хранения, а запас возвращает точку внутрь разрешённой зоны.
	/// </summary>
	/// <param name="serverNowMs">Серверное время биржи в миллисекундах.</param>
	/// <returns>Самая ранняя дата, которую перебор истории вправе запросить.</returns>
	public static long GetAllowedEarliestMs(long serverNowMs)
	{
		return serverNowMs - ExchangeStorageDepthMs + BoundarySafetyMarginMs;
	}

	/// <summary>
	/// Clamp пола перебора: берётся позднейший из двух полов — конфигурируемого
	/// и граничного. Глубина из конфигурации сохраняется, пока не пробивает границу
	/// хранения биржи; смещённые локальные часы и завышенная глубина пол не отодвигают.
	/// </summary>
	/// <param name="configuredFloorMs">Пол, вычисленный из конфигурации запуска.</param>
	/// <param name="serverNowMs">Серверное время биржи в миллисекундах.</param>
	/// <returns>Пол перебора, не заходящий за границу хранения истории биржи.</returns>
	public static long ClampFloorMs(long configuredFloorMs, long serverNowMs)
	{
		return Math.Max(configuredFloorMs, GetAllowedEarliestMs(serverNowMs));
	}
}
