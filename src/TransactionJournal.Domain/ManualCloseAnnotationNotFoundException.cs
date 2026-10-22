namespace TransactionJournal.Domain;

/// <summary>
/// Ручная пометка закрытия с указанным идентификатором не найдена:
/// править или удалять несуществующую пометку нельзя.
/// </summary>
public sealed class ManualCloseAnnotationNotFoundException : Exception
{
	/// <summary>Идентификатор пометки, которая не найдена.</summary>
	public long AnnotationId { get; }

	/// <summary>Создаёт исключение для несуществующей ручной пометки закрытия.</summary>
	/// <param name="annotationId">Идентификатор пометки, которой нет в журнале.</param>
	public ManualCloseAnnotationNotFoundException(long annotationId)
		: base($"Ручная пометка закрытия с идентификатором {annotationId} не найдена.")
	{
		AnnotationId = annotationId;
	}
}
