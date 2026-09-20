namespace TransactionJournal.Bybit;

/// <summary>
/// Учётные данные API-ключа Bybit для подписи запросов синхронизации.
/// Ключ выдаётся только с правами на чтение.
/// </summary>
/// <param name="ApiKey">Значение заголовка X-BAPI-API-KEY.</param>
/// <param name="ApiSecret">Секрет ключа; участвует только в HMAC-подписи и по сети не передаётся.</param>
public sealed record BybitCredentials(string ApiKey, string ApiSecret);
