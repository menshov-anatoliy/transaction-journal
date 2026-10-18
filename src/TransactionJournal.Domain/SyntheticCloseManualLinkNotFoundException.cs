namespace TransactionJournal.Domain;

/// <summary>
/// Ручная связка с указанным идентификатором в конструкции не найдена:
/// удаление возможно только существующей связки, повторное удаление — ошибка.
/// </summary>
public sealed class SyntheticCloseManualLinkNotFoundException : Exception
{
	/// <summary>Конструкция, в которой не нашлась ручная связка.</summary>
	public long ConstructionId { get; }

	/// <summary>Идентификатор ручной связки.</summary>
	public long LinkId { get; }

	/// <summary>Создаёт исключение для ненайденной ручной связки.</summary>
	/// <param name="constructionId">Идентификатор конструкции.</param>
	/// <param name="linkId">Идентификатор ручной связки.</param>
	public SyntheticCloseManualLinkNotFoundException(long constructionId, long linkId)
		: base($"Ручная связка {linkId} в конструкции {constructionId} не найдена.")
	{
		ConstructionId = constructionId;
		LinkId = linkId;
	}
}
