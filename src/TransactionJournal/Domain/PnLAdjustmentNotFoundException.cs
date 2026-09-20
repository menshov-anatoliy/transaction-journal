namespace TransactionJournal.Domain;

/// <summary>
/// Внешняя корректировка PnL с указанным идентификатором не найдена:
/// править или удалять несуществующую корректировку нельзя.
/// </summary>
public sealed class PnLAdjustmentNotFoundException : Exception
{
	/// <summary>Идентификатор корректировки, которая не найдена.</summary>
	public long AdjustmentId { get; }

	/// <summary>Создаёт исключение для несуществующей внешней корректировки PnL.</summary>
	/// <param name="adjustmentId">Идентификатор корректировки, которой нет в журнале.</param>
	public PnLAdjustmentNotFoundException(long adjustmentId)
		: base($"Внешняя корректировка PnL с идентификатором {adjustmentId} не найдена.")
	{
		AdjustmentId = adjustmentId;
	}
}
