namespace TransactionJournal.Domain;

/// <summary>
/// Отказ удаления конструкции, у которой есть привязанные сделки, ручные
/// сделки или внешние корректировки PnL: конструкция и её данные остаются
/// неизменными, а причина отказа объясняется пользователю.
// Traceability: openspec:domain/constructions#scenario-delete-only-when-empty
/// </summary>
public sealed class ConstructionDeletionRefusedException : Exception
{
	/// <summary>Число привязанных к конструкции биржевых сделок, блокирующих удаление.</summary>
	public int BoundTradeCount { get; }

	/// <summary>Число ручных сделок конструкции, блокирующих удаление.</summary>
	public int ManualTradeCount { get; }

	/// <summary>Число внешних корректировок PnL, блокирующих удаление.</summary>
	public int AdjustmentCount { get; }

	/// <summary>Создаёт исключение с перечнем блокирующих записей.</summary>
	/// <param name="boundTradeCount">Число привязанных биржевых сделок.</param>
	/// <param name="manualTradeCount">Число ручных сделок.</param>
	/// <param name="adjustmentCount">Число внешних корректировок PnL.</param>
	public ConstructionDeletionRefusedException(int boundTradeCount, int manualTradeCount, int adjustmentCount)
		: base(ComposeMessage(boundTradeCount, manualTradeCount, adjustmentCount))
	{
		BoundTradeCount = boundTradeCount;
		ManualTradeCount = manualTradeCount;
		AdjustmentCount = adjustmentCount;
	}

	#region Помощники

	/// <summary>
	/// Собирает объяснение отказа: пользователь видит, какие именно записи
	/// блокируют удаление и что снять перед повтором.
	/// </summary>
	private static string ComposeMessage(int boundTradeCount, int manualTradeCount, int adjustmentCount)
	{
		var blockers = new List<string>();
		if (boundTradeCount > 0)
		{
			blockers.Add($"привязанных биржевых сделок — {boundTradeCount}");
		}

		if (manualTradeCount > 0)
		{
			blockers.Add($"ручных сделок — {manualTradeCount}");
		}

		if (adjustmentCount > 0)
		{
			blockers.Add($"внешних корректировок PnL — {adjustmentCount}");
		}

		return
			$"Удаление конструкции невозможно: {string.Join(" и ", blockers)}. " +
			"Верните сделки во «Входящие» и удалите корректировки перед удалением конструкции.";
	}

	#endregion
}
