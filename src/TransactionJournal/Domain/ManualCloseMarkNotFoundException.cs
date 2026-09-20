namespace TransactionJournal.Domain;

/// <summary>
/// Ручная пометка закрытия с указанным идентификатором не найдена:
/// править или удалять несуществующую пометку нельзя.
/// </summary>
public sealed class ManualCloseMarkNotFoundException : Exception
{
	/// <summary>Идентификатор пометки, которая не найдена.</summary>
	public long MarkId { get; }

	/// <summary>Создаёт исключение для несуществующей ручной пометки закрытия.</summary>
	/// <param name="markId">Идентификатор пометки, которой нет в журнале.</param>
	public ManualCloseMarkNotFoundException(long markId)
		: base($"Ручная пометка закрытия с идентификатором {markId} не найдена.")
	{
		MarkId = markId;
	}
}
