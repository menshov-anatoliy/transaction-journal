using TransactionJournal.Domain.Analytics;

namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Ценовые данные связки для расчёта величины «зафиксировано синтетикой»:
/// цена и комиссия каждой сделки связки и поток сделок входа закрываемой ноги.
/// Цены и комиссии подготавливаются вызывающим слоем из материализованных
/// сделок по execId связки; поток ноги — её Trade-записи, из которых
/// FIFO-примитивы аналитики выводят среднюю цену входа и удельную комиссию
/// входа. Все величины в USDT: комиссии опционных сделок USDC приведены
/// к USDT паритетом 1:1 ещё при материализации.
/// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#requirement-synthetic-close-locked-value
/// Traceability: adr:docs/adr/0001-usdc-usdt-parity.md#usdc-usdt-parity-1-1
/// </summary>
public sealed record SyntheticCloseValuation
{
	/// <summary>Цена линейной сделки связки — база внутренней стоимости к экспирации.</summary>
	public required decimal LinearPrice { get; init; }

	/// <summary>Комиссия линейной сделки со знаком уменьшения результата: уплаченная положительна, rebate отрицателен.</summary>
	public required decimal LinearFee { get; init; }

	/// <summary>Цена встречного опциона — премия за единицу, полученная его продажей в связке.</summary>
	public required decimal CounterOptionPrice { get; init; }

	/// <summary>Комиссия встречной опционной сделки со знаком уменьшения результата.</summary>
	public required decimal CounterOptionFee { get; init; }

	/// <summary>
	/// Поток записей закрываемой ноги — её сделки входа из потока позиции.
	/// Марки и прочие закрывающие записи расчётом лока не читаются:
	/// паритетный лок к экспирации от котировок не зависит.
	/// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-locked-value-marks-failure-independent
	/// </summary>
	public required IReadOnlyCollection<PositionFifoEntry> ClosedLegEntries { get; init; }
}
