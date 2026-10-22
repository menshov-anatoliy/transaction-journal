namespace TransactionJournal.Application.Analytics;

/// <summary>
/// Результат оценки нереализованного PnL по котировкам на момент запроса: метрики
/// позиций с подставленными котировкой и нереализованной оценкой открытых остатков,
/// отметка времени котировок и признак сбоя провайдера. Оценка живёт одно чтение —
/// это производные величины, хранением они не живут и пересчитываются при
/// каждом запросе.
// Traceability: openspec:analytics/performance#requirement-unrealized-pnl-current-marks
/// </summary>
public sealed record UnrealizedPnlEvaluation
{
	/// <summary>Метрики позиций после оценки: открытые остатки несут котировку и нереализованный PnL либо null при сбое котировок.</summary>
	public required IReadOnlyList<PositionMetrics> Positions { get; init; }

	/// <summary>
	/// Отметка времени котировок (quotes_as_of) — момент получения котировок оценки:
	/// старейшая из котировок, по которым оценена нереализованная часть. null при
	/// сбое котировок или когда открытым остаткам котировки были не нужны.
	// Traceability: openspec:analytics/performance#scenario-open-residual-valued-at-request
	/// </summary>
	public required DateTimeOffset? QuotesAsOf { get; init; }

	/// <summary>
	/// Признак сбоя котировок: хотя бы один открытый остаток остался без оценки.
	/// Нереализованная часть и отметка времени котировок деградируют в null только
	/// вместе с этим признаком, реализованные метрики не затрагиваются.
	// Traceability: openspec:analytics/performance#scenario-mark-failure-nulls-unrealized-only
	/// </summary>
	public required bool HasQuoteFailure { get; init; }

	/// <summary>
	/// Свежие тикеры текущего чтения по символам открытых остатков: котировка,
	/// индексная цена и тета. Метрики времени жизни опционных ног строятся
	/// поверх этих же запросов — второй обход публичных тикеров не вводится,
	/// тета и индексная цена остаются nullable: биржа отдаёт их только опционам.
	// Traceability: change:add-option-lifetime-metrics/design#d1
	/// </summary>
	public IReadOnlyDictionary<string, InstrumentQuoteSnapshot> Quotes { get; init; } =
		new Dictionary<string, InstrumentQuoteSnapshot>(StringComparer.Ordinal);
}
