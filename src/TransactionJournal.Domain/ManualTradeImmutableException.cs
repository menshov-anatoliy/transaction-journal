namespace TransactionJournal.Domain;

/// <summary>
/// Синхронизированная биржевая запись неизменяема: правка и удаление сделок,
/// пришедших синхронизацией, отклоняются. Биржевые записи — показания биржи,
/// а не данные пользователя; пользователь правит только записи ручного ввода.
/// </summary>
// Правка и удаление синхронизированной сделки отклоняются системой —
// биржевые записи по построению не редактируются пользователем.
// Traceability: openspec:domain/manual-trades#requirement-manual-trade-edit-delete
public sealed class ManualTradeImmutableException : Exception
{
	/// <summary>Создаёт исключение для попытки изменить биржевую запись по её ключу строки сырья.</summary>
	/// <param name="recordId">Идентификатор записи, который адресует биржевую сделку.</param>
	public ManualTradeImmutableException(long recordId)
		: base($"Запись с идентификатором {recordId} получена синхронизацией и неизменяема: правка и удаление биржевых сделок недоступны.")
	{
		RecordId = recordId;
	}

	/// <summary>Идентификатор записи, которой владеет синхронизация.</summary>
	public long RecordId { get; }
}
