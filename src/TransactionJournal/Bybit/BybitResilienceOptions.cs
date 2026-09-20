namespace TransactionJournal.Bybit;

/// <summary>
/// Параметры устойчивости клиента Bybit: минимальный интервал между запросами
/// и правила повторов для каждого класса сбоев — сети/5xx, retCode 10006 и HTTP 403.
/// Ручная синхронизация не требует агрессивных настроек: запас против лимитов биржи избыточен.
/// Traceability: change:add-bybit-sync/design#d5
/// Traceability: openspec:sync/bybit-history#requirement-api-limits-and-error-handling
/// </summary>
public sealed record BybitResilienceOptions
{
	/// <summary>Интервал по умолчанию — 25 мс (40 req/s): запас против документированных 50 req/s.</summary>
	public static readonly TimeSpan DefaultMinRequestInterval = TimeSpan.FromMilliseconds(25);

	/// <summary>Базовая пауза повтора сети/5xx по умолчанию.</summary>
	public static readonly TimeSpan DefaultNetworkRetryBaseDelay = TimeSpan.FromMilliseconds(200);

	/// <summary>Базовая пауза повтора retCode 10006 по умолчанию — секунда с экспоненциальным нарастанием.</summary>
	public static readonly TimeSpan DefaultRateLimitRetryDelay = TimeSpan.FromSeconds(1);

	/// <summary>Длинная пауза HTTP 403 по умолчанию — примерно десять минут блокировки IP.</summary>
	public static readonly TimeSpan DefaultAccessBlockedPause = TimeSpan.FromMinutes(10);

	/// <summary>Минимальный интервал между отправками запросов к бирже.</summary>
	public TimeSpan MinRequestInterval { get; init; } = DefaultMinRequestInterval;

	/// <summary>Число повторов после сетевых ошибок и HTTP 5xx.</summary>
	public int NetworkRetryCount { get; init; } = 3;

	/// <summary>Базовая пауза экспоненциального бэкоффа сети/5xx.</summary>
	public TimeSpan NetworkRetryBaseDelay { get; init; } = DefaultNetworkRetryBaseDelay;

	/// <summary>Число повторов после retCode 10006 «Too many visits!».</summary>
	public int RateLimitRetryCount { get; init; } = 3;

	/// <summary>Базовая пауза повтора retCode 10006; растёт экспоненциально от попытки к попытке.</summary>
	public TimeSpan RateLimitRetryDelay { get; init; } = DefaultRateLimitRetryDelay;

	/// <summary>Число повторов после HTTP 403 «access too frequent».</summary>
	public int AccessBlockedRetryCount { get; init; } = 1;

	/// <summary>Длинная пауза повтора HTTP 403 — длительность IP-блокировки биржи.</summary>
	public TimeSpan AccessBlockedPause { get; init; } = DefaultAccessBlockedPause;
}
