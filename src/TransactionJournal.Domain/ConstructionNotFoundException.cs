namespace TransactionJournal.Domain;

/// <summary>
/// Конструкция с указанным идентификатором не найдена в журнале:
/// операция над несуществующей конструкцией невозможна.
/// </summary>
public sealed class ConstructionNotFoundException : Exception
{
	/// <summary>Идентификатор конструкции, который не найден.</summary>
	public long ConstructionId { get; }

	/// <summary>Создаёт исключение для несуществующего идентификатора конструкции.</summary>
	/// <param name="constructionId">Идентификатор конструкции, которого нет в журнале.</param>
	public ConstructionNotFoundException(long constructionId)
		: base($"Конструкция с идентификатором {constructionId} не найдена.")
	{
		ConstructionId = constructionId;
	}
}
