namespace TransactionJournal.Domain;

/// <summary>
/// Предупреждение об избыточной закрывающей записи: запись применяется к уже
/// нулевому остатку — ручная пометка при закрытой позиции либо поздно пришедшая
/// delivery, конкурирующая с более ранней закрывающей записью. Избыточная запись
/// не применяется и не блокирует чтение: позиция не переворачивается, а пользователь
/// видит причину расхождения.
// Traceability: change:add-core-domain/design#risks-trade-offs
/// </summary>
public sealed record RedundantClosingEntryWarning
{
	/// <summary>Конструкция, внутри которой запись оказалась избыточной.</summary>
	public required long ConstructionId { get; init; }

	/// <summary>Инструмент избыточной записи.</summary>
	public required string Symbol { get; init; }

	/// <summary>Вид избыточной записи: delivery, OTM-экспирация или ручная пометка.</summary>
	public required PositionClosingKind Kind { get; init; }

	/// <summary>Момент, на который запись претендовала на закрытие позиции.</summary>
	public required DateTimeOffset ClosedAt { get; init; }

	/// <summary>Ключ источника избыточной записи: биржевой ключ либо «manual:{id}».</summary>
	public required string SourceKey { get; init; }
}
