namespace TransactionJournal.Domain;

/// <summary>
/// Запись оспаривания с указанной парой execId не найдена: явный возврат
/// возможен только для оспоренной связки, повторный возврат — ошибка.
/// </summary>
public sealed class SyntheticCloseDisputeNotFoundException : Exception
{
	/// <summary>Конструкция, в которой не нашлась запись оспаривания.</summary>
	public long ConstructionId { get; }

	/// <summary>Биржевой идентификатор линейного исполнения связки.</summary>
	public string LinearExecId { get; }

	/// <summary>Биржевой идентификатор встречного опционного исполнения связки.</summary>
	public string CounterOptionExecId { get; }

	/// <summary>Создаёт исключение для неоспоренной связки.</summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="linearExecId">Идентификатор линейного исполнения.</param>
	/// <param name="counterOptionExecId">Идентификатор встречного опционного исполнения.</param>
	public SyntheticCloseDisputeNotFoundException(
		long constructionId,
		string linearExecId,
		string counterOptionExecId)
		: base($"Запись оспаривания связки {linearExecId} + {counterOptionExecId} в конструкции {constructionId} не найдена.")
	{
		ConstructionId = constructionId;
		LinearExecId = linearExecId;
		CounterOptionExecId = counterOptionExecId;
	}
}
