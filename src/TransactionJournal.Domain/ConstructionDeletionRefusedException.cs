namespace TransactionJournal.Domain;

/// <summary>
/// Отказ удаления конструкции, у которой есть привязанные сделки или внешние
/// корректировки PnL: конструкция и её данные остаются неизменными, а причина
/// отказа объясняется пользователю.
// Traceability: openspec:domain/constructions#scenario-delete-only-when-empty
/// </summary>
public sealed class ConstructionDeletionRefusedException : Exception
{
	/// <summary>Число привязанных к конструкции сделок, блокирующих удаление.</summary>
	public int BoundTradeCount { get; }

	/// <summary>Число внешних корректировок PnL, блокирующих удаление.</summary>
	public int AdjustmentCount { get; }

	/// <summary>Создаёт исключение с перечнем блокирующих записей.</summary>
	/// <param name="boundTradeCount">Число привязанных сделок.</param>
	/// <param name="adjustmentCount">Число внешних корректировок PnL.</param>
	public ConstructionDeletionRefusedException(int boundTradeCount, int adjustmentCount)
		: base(ComposeMessage(boundTradeCount, adjustmentCount))
	{
		BoundTradeCount = boundTradeCount;
		AdjustmentCount = adjustmentCount;
	}

	#region Помощники

	/// <summary>
	/// Собирает объяснение отказа: пользователь видит, какие именно записи
	/// блокируют удаление и что снять перед повтором.
	/// </summary>
	private static string ComposeMessage(int boundTradeCount, int adjustmentCount)
	{
		var blockers = new List<string>();
		if (boundTradeCount > 0)
		{
			blockers.Add($"привязанных сделок — {boundTradeCount}");
		}

		if (adjustmentCount > 0)
		{
			blockers.Add($"внешних корректировок PnL — {adjustmentCount}");
		}

		return
			$"Удаление конструкции невозможно: {string.Join(" и ", blockers)}. " +
			"Снимите привязки сделок и удалите корректировки перед удалением конструкции.";
	}

	#endregion
}
