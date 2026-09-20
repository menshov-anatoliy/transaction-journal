namespace TransactionJournal.Bybit;

/// <summary>
/// Параметры клиента Bybit API: базовый URL (прод или зеркало) и окно валидности подписи.
/// Базовый URL вынесен в конфигурацию, чтобы при необходимости переключиться на зеркало api.bybit.com.
/// Traceability: change:add-bybit-sync/design#d6
/// </summary>
public sealed record BybitClientOptions
{
	/// <summary>Продакшен-эндпоинт Bybit API по умолчанию.</summary>
	public const string DefaultBaseUrl = "https://api.bybit.com";

	/// <summary>Окно валидности подписи из официального примера Bybit, мс.</summary>
	public const int DefaultRecvWindowMs = 5000;

	/// <summary>Базовый URL API; переопределяется конфигурацией при использовании зеркала.</summary>
	public string BaseUrl { get; init; } = DefaultBaseUrl;

	/// <summary>Значение заголовка X-BAPI-RECV-WINDOW в миллисекундах.</summary>
	public int RecvWindowMs { get; init; } = DefaultRecvWindowMs;
}
