namespace TransactionJournal.Hints.Ports;

/// <summary>
/// Порт рыночных марок для прохода агента: свежие марки набора инструментов
/// одним запросом. Недоступность источника — управляемый исход, а не исключение:
/// проход при нём пропускается с диагностикой, подсказки на неполных данных
/// не выпускаются.
// Traceability: openspec:hints/engine-pass#requirement-engine-market-unavailable-skips-pass
/// </summary>
public interface IMarkSource
{
	/// <summary>Запрашивает свежие марки перечисленных инструментов.</summary>
	/// <param name="symbols">Инструменты, марки которых нужны проходу.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Партия марок с признаком доступности источника.</returns>
	Task<MarkBatch> GetMarksAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken = default);
}

/// <summary>
/// Партия марок одного прохода. <see cref="FailureReason"/> отличает недоступность
/// источника (проход пропускается) от отсутствия марки отдельного инструмента —
/// решение о достаточности данных принимает триггер по своему условию.
// Traceability: openspec:hints/engine-pass#requirement-engine-market-unavailable-skips-pass
/// </summary>
public sealed record MarkBatch
{
	private static readonly IReadOnlyDictionary<string, decimal> EmptyMarks =
		new Dictionary<string, decimal>();

	/// <summary>Полученные марки по символам; при недоступности источника пуст.</summary>
	public required IReadOnlyDictionary<string, decimal> Marks { get; init; }

	/// <summary>Причина недоступности источника; null — источник доступен.</summary>
	public required string? FailureReason { get; init; }

	/// <summary>Источник доступен и партии можно доверять как данные прохода.</summary>
	public bool IsAvailable => FailureReason is null;

	/// <summary>Создаёт партию недоступного источника с диагностикой причины.</summary>
	/// <param name="reason">Причина недоступности для диагностики прохода.</param>
	public static MarkBatch Unavailable(string reason) => new()
	{
		Marks = EmptyMarks,
		FailureReason = reason,
	};
}
