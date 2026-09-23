namespace TransactionJournal.Sync;

/// <summary>
/// Порт списка базовых активов опционной доски для подсистемы синхронизации: перед
/// проходом категории option сообщает, по каким активам запрашивать историю исполнения.
/// Список — объединение живого перечисления справочника (доска по умолчанию), активов
/// сырьевого хранилища и конфигурационных дополнений; собирается заново при каждом
/// запуске — новая доска биржи подхватывается очередным синком без правок кода.
/// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
/// </summary>
public interface IOptionBaseCoinSource
{
	/// <summary>
	/// Собирает базовые активы опционной доски: листает публичный справочник инструментов
	/// страницами с курсорной пагинацией до исчерпания, объединяет найденные активы
	/// с активами сырьевого хранилища и конфигурируемым списком дополнительных
	/// и возвращает их в детерминированном порядке.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой retCode или неудачным HTTP-статусом после всех повторов.</exception>
	Task<IReadOnlyList<string>> GetBaseCoinsAsync(CancellationToken cancellationToken = default);
}
