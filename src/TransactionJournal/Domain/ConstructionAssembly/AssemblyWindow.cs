namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Окно группировки — подряд идущие исполнения одного базового актива,
/// разделённые не более чем 60 минутами и не разорванные delivery-записью.
/// Окно атомарно: оно целиком ролл, целиком усреднение или целиком открывает
/// новую конструкцию.
// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#requirement-deterministic-option-assembly
/// </summary>
public sealed record AssemblyWindow
{
	/// <summary>Исполнения окна в порядке прогона: по времени исполнения, затем по execId.</summary>
	public required IReadOnlyList<AssemblyExecution> Executions { get; init; }

	/// <summary>Время первого исполнения окна, мс Unix-эпохи — день окна и начало жизни новой конструкции.</summary>
	public long FirstTimeMs => Executions[0].ExecTimeMs;

	/// <summary>Время последнего исполнения окна, мс Unix-эпохи — точка отсчёта зазора к следующему окну.</summary>
	public long LastTimeMs => Executions[^1].ExecTimeMs;
}
