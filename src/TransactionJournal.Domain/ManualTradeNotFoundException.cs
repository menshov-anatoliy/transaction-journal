namespace TransactionJournal.Domain;

/// <summary>
/// Ручная сделка с указанным идентификатором не найдена: править и удалять
/// можно только существующие записи ручного ввода. Попытка изменить
/// несуществующую запись — ошибка запроса.
/// </summary>
public sealed class ManualTradeNotFoundException : Exception
{
	/// <summary>Создаёт исключение для несуществующего идентификатора ручной сделки.</summary>
	/// <param name="manualTradeId">Идентификатор ручной сделки, которого нет в журнале.</param>
	public ManualTradeNotFoundException(long manualTradeId)
		: base($"Ручная сделка с идентификатором {manualTradeId} не найдена в журнале.")
	{
		ManualTradeId = manualTradeId;
	}

	/// <summary>Идентификатор ручной сделки, который не найден.</summary>
	public long ManualTradeId { get; }
}
