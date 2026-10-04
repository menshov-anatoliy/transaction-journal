namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Закрывающее событие экспирации во входной модели сборки: реальная
/// delivery-запись биржи либо выведенная из справочника OTM-закрывающая
/// (ADR-0002 — закрывающие записи экспираций). Событие обрывает окно
/// группировки и погашает остаток ноги у конструкции-владельца.
// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#requirement-deterministic-option-assembly
/// Traceability: adr:docs/adr/0002-option-expiry-closing-entries.md#option-expiry-closing-entries
/// </summary>
public sealed record AssemblyDelivery
{
	/// <summary>Символ опциона (например, ETH-25SEP26-2100-C-USDT).</summary>
	public required string Symbol { get; init; }

	/// <summary>Время доставки в мс Unix-эпохи.</summary>
	public required long DeliveryTimeMs { get; init; }
}
