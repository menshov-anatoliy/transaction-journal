namespace TransactionJournal.Sync;

/// <summary>
/// Порт читателя базовых активов сырьевого хранилища: перед проходом категории option
/// сообщает, префиксы каких опционных символов уже встречались в записях исполнения
/// и delivery-записях. Доски этих активов могли не попасть в безфильтровое перечисление
/// справочника — сырьё не даёт потерять их историю.
/// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
/// </summary>
public interface IOptionRawBaseCoinReader
{
	/// <summary>
	/// Собирает базовые активы сырья: distinct-символы опционных записей исполнения
	/// и delivery-записей разбираются на префикс до первого дефиса, нормализуются
	/// и возвращаются в детерминированном порядке.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	Task<IReadOnlyList<string>> GetRawBaseCoinsAsync(CancellationToken cancellationToken = default);
}
