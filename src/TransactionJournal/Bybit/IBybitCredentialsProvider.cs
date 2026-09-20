namespace TransactionJournal.Bybit;

/// <summary>
/// Поставщик учётных данных API-ключа Bybit для клиента синхронизации.
/// Конкретное место постоянного хранения секрета решается тикетом #4;
/// контракт ограничен выдачей пары ключ/секрет.
/// </summary>
public interface IBybitCredentialsProvider
{
	/// <summary>Возвращает учётные данные ключа; вызывается перед каждым подписываемым запросом.</summary>
	BybitCredentials GetCredentials();
}
