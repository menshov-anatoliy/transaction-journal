namespace TransactionJournal.Domain;

/// <summary>
/// Сделка с указанным execId не найдена в сыром хранилище журнала:
/// привязать или вернуть во «Входящие» несуществующую сделку нельзя.
/// </summary>
public sealed class TradeNotFoundException : Exception
{
	/// <summary>Биржевой идентификатор исполнения, который не найден.</summary>
	public string ExecId { get; }

	/// <summary>Создаёт исключение для несуществующего идентификатора исполнения.</summary>
	/// <param name="execId">Идентификатор исполнения, которого нет в журнале.</param>
	public TradeNotFoundException(string execId)
		: base($"Сделка с execId «{execId}» не найдена в журнале.")
	{
		ExecId = execId;
	}
}
