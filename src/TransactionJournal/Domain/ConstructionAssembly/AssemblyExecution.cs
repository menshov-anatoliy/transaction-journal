namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Исполнение сделки во входной модели сборки конструкций: минимум полей,
/// необходимый правилам группировки, — знаковое количество уже сведено из
/// стороны биржи (покупка положительна, продажа отрицательна), арифметика
/// ведётся на <c>decimal</c>. Снимок исполнений подготавливает use-case
/// пересбора из сырых записей хранилища.
// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#requirement-deterministic-option-assembly
/// </summary>
public sealed record AssemblyExecution
{
	/// <summary>Биржевой идентификатор исполнения — ключ привязки execId → конструкция.</summary>
	public required string ExecId { get; init; }

	/// <summary>Торговая категория записи: option — группируется в конструкции, linear — привязывается как сделка робота.</summary>
	public required string Category { get; init; }

	/// <summary>Символ инструмента (например, ETH-25SEP26-2100-C-USDT или ETHUSDT).</summary>
	public required string Symbol { get; init; }

	/// <summary>Время исполнения в мс Unix-эпохи; первичный ключ упорядочения прогона.</summary>
	public required long ExecTimeMs { get; init; }

	/// <summary>Знаковое количество исполнения: покупка положительна, продажа отрицательна.</summary>
	public required decimal SignedQuantity { get; init; }
}
