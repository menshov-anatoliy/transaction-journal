namespace TransactionJournal.Application.Analytics;

/// <summary>
/// Метрики позиции «конструкция × инструмент»: агрегаты её записей и производные
/// временные характеристики. Метрики вычисляются при чтении из текущего набора
/// записей — сделок и закрывающих записей — и хранением не живут: правка состава
/// записей отражается очередным чтением без следов прежнего расчёта.
// Traceability: openspec:analytics/performance#requirement-position-metrics
// Traceability: change:add-analytics/design#d5
/// </summary>
public sealed record PositionMetrics
{
	/// <summary>Конструкция, которой принадлежит позиция.</summary>
	public required long ConstructionId { get; init; }

	/// <summary>Инструмент позиции.</summary>
	public required string Symbol { get; init; }

	/// <summary>Чистый остаток: положителен для длинной позиции, отрицателен для короткой; ноль — позиция закрыта.</summary>
	public required decimal Residual { get; init; }

	/// <summary>Реализованный PnL: FIFO-результат встречных частей, уменьшенный на комиссии записей потока.</summary>
	public required decimal RealizedPnL { get; init; }

	/// <summary>Накопленные комиссии записей позиции: уплаченные складываются, rebate снижает сумму.</summary>
	public required decimal AccumulatedFees { get; init; }

	/// <summary>Средняя цена открытого остатка — количество-взвешенная цена непокрытых FIFO-слоёв; у закрытой позиции не вычисляется.</summary>
	public required decimal? AverageOpenPrice { get; init; }

	/// <summary>
	/// Средняя цена входа — количество-взвешенная цена всех открывающих частей
	/// FIFO-потока, включая впоследствии закрытые; null, если открывающих частей
	/// нет. Не путать со средней ценой открытого остатка (AverageOpenPrice):
	/// вход — вся открывающая часть потока и «цена, по которой входили»,
	/// остаток — база нереализованной оценки текущей позиции.
	// Traceability: openspec:analytics/performance#scenario-position-average-entry-from-opening-parts
	/// </summary>
	public required decimal? AverageEntryPrice { get; init; }

	/// <summary>
	/// Средняя цена закрытия — количество-взвешенная эффективная цена всех
	/// закрывающих частей потока; null, если закрывающих частей нет.
	// Traceability: openspec:analytics/performance#scenario-position-average-close-from-closing-parts
	/// </summary>
	public required decimal? AverageClosePrice { get; init; }

	/// <summary>
	/// Общий PnL позиции — реализованный плюс нереализованная оценка: у закрытой
	/// позиции равен реализованному и марок не требует; у открытой собирается
	/// слоем марок после оценки остатка и до неё, как и при сбое марок, остаётся
	/// null.
	// Traceability: openspec:analytics/performance#scenario-position-total-pnl-includes-unrealized
	// Traceability: openspec:analytics/performance#scenario-position-total-pnl-mark-failure
	/// </summary>
	public required decimal? TotalPnL { get; init; }

	/// <summary>
	/// Текущая марка инструмента при открытом остатке — заполняется слоем марок
	/// при запросе из кэша провайдера. Закрытой позиции марка не нужна и не вычисляется.
	// Traceability: openspec:analytics/performance#scenario-open-position-average-and-mark
	/// </summary>
	public required decimal? MarkPrice { get; init; }

	/// <summary>
	/// Нереализованный PnL: у закрытой позиции равен нулю и марок не требует;
	/// у открытой оценивается марками на момент запроса — до подключения слоя
	/// марок остаётся null.
	// Traceability: openspec:analytics/performance#scenario-closed-position-has-no-unrealized
	/// </summary>
	public required decimal? UnrealizedPnL { get; init; }

	/// <summary>
	/// Текущая стоимость позиции по маркам — нетто-величина «марка × знаковый
	/// остаток»: длинный остаток даёт положительную стоимость, короткий —
	/// отрицательную. Закрытая позиция стоимости не имеет; до оценки марками
	/// и при сбое марок стоимость остаётся null, остальные метрики не меняются.
	// Traceability: openspec:analytics/performance#requirement-mark-value-of-position-and-construction
	/// </summary>
	public decimal? MarkValue { get; init; }

	/// <summary>
	/// Процент изменения цены открытого остатка — движение текущей марки от
	/// средней цены остатка, приведённое к направлению позиции: плюс всегда
	/// означает движение «в прибыль» (у лонга рост цены, у шорта — падение).
	/// Закрытая позиция процента не имеет; до оценки марками и при сбое марок
	/// процент остаётся null, остальные метрики не меняются.
	// Traceability: openspec:analytics/performance#requirement-open-remainder-price-change-percent
	/// </summary>
	public decimal? PriceChangePercent { get; init; }

	/// <summary>Дата открытия — время первой записи позиции.</summary>
	public required DateTimeOffset OpenedAt { get; init; }

	/// <summary>Дата закрытия — момент последнего обнуления остатка; null, пока позиция открыта.</summary>
	public required DateTimeOffset? ClosedAt { get; init; }

	/// <summary>Длительность между открытием и закрытием; null у открытой позиции.</summary>
	public required TimeSpan? Duration { get; init; }
}
