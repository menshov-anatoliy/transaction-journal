namespace TransactionJournal.Data;

/// <summary>Источник внешней корректировки PnL: результат торгового робота или ручная поправка.</summary>
public enum PnLAdjustmentSource
{
	/// <summary>Корректировка перенесена из PnL торгового робота.</summary>
	Robot,

	/// <summary>Корректировка внесена пользователем вручную.</summary>
	Manual,
}

/// <summary>
/// Внешняя корректировка PnL: слагаемое результата конструкции без сделки.
/// Хранит дату, источник, знаковую сумму в USDT и комментарий; правка и
/// удаление свободны, журнал аудита изменений в MVP не ведётся.
// Traceability: openspec:domain/constructions#requirement-external-pnl-adjustments
/// </summary>
public sealed class PnLAdjustment
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>Конструкция, в результат которой входит корректировка.</summary>
	public Construction Construction { get; set; } = null!;

	/// <summary>Внешний ключ конструкции — владельца корректировки.</summary>
	public long ConstructionId { get; set; }

	/// <summary>Дата корректировки.</summary>
	public required DateTimeOffset Date { get; set; }

	/// <summary>Источник корректировки: «робот» или «ручная».</summary>
	public PnLAdjustmentSource Source { get; set; }

	/// <summary>Знаковая сумма корректировки в USDT: положительная увеличивает результат, отрицательная — уменьшает.</summary>
	public decimal AmountUsdt { get; set; }

	/// <summary>Свободный комментарий пользователя к корректировке.</summary>
	public string? Comment { get; set; }
}
