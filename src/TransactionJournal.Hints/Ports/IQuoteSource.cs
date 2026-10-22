namespace TransactionJournal.Hints.Ports;

/// <summary>
/// Порт рыночных котировок для прохода агента: свежие котировки набора инструментов
/// одним запросом. Недоступность источника — управляемый исход, а не исключение:
/// проход при нём пропускается с диагностикой, подсказки на неполных данных
/// не выпускаются.
// Traceability: openspec:hints/engine-pass#requirement-engine-market-unavailable-skips-pass
/// </summary>
public interface IQuoteSource
{
	/// <summary>Запрашивает свежие котировки перечисленных инструментов.</summary>
	/// <param name="symbols">Инструменты, котировки которых нужны проходу.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Партия котировок с признаком доступности источника.</returns>
	Task<QuoteBatch> GetQuotesAsync(IReadOnlyCollection<string> symbols, CancellationToken cancellationToken = default);
}

/// <summary>
/// Партия котировок одного прохода. <see cref="FailureReason"/> отличает недоступность
/// источника (проход пропускается) от отсутствия котировки отдельного инструмента —
/// решение о достаточности данных принимает триггер по своему условию.
// Traceability: openspec:hints/engine-pass#requirement-engine-market-unavailable-skips-pass
/// </summary>
public sealed record QuoteBatch
{
	private static readonly IReadOnlyDictionary<string, decimal> EmptyQuotes =
		new Dictionary<string, decimal>();

	/// <summary>Полученные котировки по символам; при недоступности источника пуст.</summary>
	public required IReadOnlyDictionary<string, decimal> Quotes { get; init; }

	/// <summary>Причина недоступности источника; null — источник доступен.</summary>
	public required string? FailureReason { get; init; }

	/// <summary>Источник доступен и партии можно доверять как данные прохода.</summary>
	public bool IsAvailable => FailureReason is null;

	/// <summary>Создаёт партию недоступного источника с диагностикой причины.</summary>
	/// <param name="reason">Причина недоступности для диагностики прохода.</param>
	public static QuoteBatch Unavailable(string reason) => new()
	{
		Quotes = EmptyQuotes,
		FailureReason = reason,
	};
}
