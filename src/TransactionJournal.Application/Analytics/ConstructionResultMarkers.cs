namespace TransactionJournal.Application.Analytics;

using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Analytics;

/// <summary>
/// Тип события истории результата конструкции, образующего маркер изменения
/// результата: окно опционных исполнений, фьючерсная сделка робота, изменение
/// набора действующих связок синтетического закрытия, внешняя корректировка
/// PnL и закрывающие записи трёх видов. Перечисление вычислительное — тип
/// выводится из вида записи журнала, а не задаётся пользователем.
/// </summary>
/// <remarks>
/// Тип события — вычислительное свойство записей журнала конструкции.
// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-markers-history-events
/// </remarks>
public enum ResultMarkerKind
{
	/// <summary>Окно опционных исполнений по правилам сборки v5 — ролл целиком одно событие.</summary>
	OptionWindow,

	/// <summary>Фьючерсная сделка робота — каждая сделка отдельное событие.</summary>
	FuturesTrade,

	/// <summary>Изменение набора действующих связок синтетического закрытия — появление или снятие.</summary>
	SyntheticLink,

	/// <summary>Внешняя корректировка PnL.</summary>
	PnLAdjustment,

	/// <summary>Закрывающая запись — поставка delivery.</summary>
	ClosingDelivery,

	/// <summary>Закрывающая запись — экспирация вне денег.</summary>
	ClosingOtmExpiry,

	/// <summary>Закрывающая запись — ручная пометка пользователя.</summary>
	ClosingManualMark,
}

/// <summary>
/// Маркер изменения результата — выведенная точка истории накопленного
/// зафиксированного денежного результата конструкции. Номер присвоен только
/// выведенным маркерам и идёт подряд без пропусков; изменение считается
/// относительно предыдущего выведенного маркера (у первого — от нуля),
/// накопленный итог — значение сводной «с учётом зафиксированного
/// синтетикой» плюс внешние корректировки минус входная стоимость ещё
/// открытых остатков на момент события: премия открытия фиксируется сразу,
/// при закрытии всех остатков итог совпадает со сводной формулой.
/// </summary>
/// <remarks>
/// Номер, изменение и итог вычисляются на сервере: порог проявления и
/// перенумерация применены до публикации контракта.
// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-marker-value
// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-marker-threshold
/// </remarks>
public sealed record ResultMarker
{
	/// <summary>Порядковый номер выведенного маркера — сквозной, без пропусков.</summary>
	public required int Number { get; init; }

	/// <summary>Время события журнала, породившего маркер.</summary>
	public required DateTimeOffset EventTime { get; init; }

	/// <summary>Тип события журнала.</summary>
	public required ResultMarkerKind Kind { get; init; }

	/// <summary>Изменение накопленного результата относительно предыдущего выведенного маркера; у первого — от нуля.</summary>
	public required decimal ChangeUsdt { get; init; }

	/// <summary>Накопленный зафиксированный результат на момент события в USDT.</summary>
	public required decimal TotalUsdt { get; init; }
}

/// <summary>
/// История маркеров изменения результата конструкции: выведенные маркеры
/// после порога проявления и перенумерации. Последний маркер выводится
/// безусловно и равняется текущему накопленному зафиксированному результату;
/// у конструкции без событий истории маркеров нет.
/// </summary>
/// <remarks>
/// Нумерация сплошная по выведенным маркерам, подряд без пропусков.
// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-marker-threshold
/// </remarks>
public sealed record ConstructionResultMarkers
{
	/// <summary>Выведенные маркеры в порядке событий журнала; пустой список, когда событий нет.</summary>
	public required IReadOnlyList<ResultMarker> Markers { get; init; }

	/// <summary>Последний выведенный маркер — текущий накопленный зафиксированный результат; null без событий.</summary>
	public ResultMarker? LastMarker => Markers.Count == 0 ? null : Markers[^1];
}

/// <summary>
/// Итоговая сводка последнего маркера — компактная пара «накопленный итог +
/// время события» для контрактов списка и превью, где полная история не
/// публикуется. Значение то же, что у последнего выведенного маркера карточки:
/// текущий накопленный зафиксированный результат конструкции.
/// </summary>
/// <remarks>
/// Пара публикуется без номера: компактные виды рисуют последний маркер
/// без бейджа нумерации.
// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-markers-contract
/// </remarks>
public sealed record ResultMarkerSummary(decimal TotalUsdt, DateTimeOffset EventAt);

/// <summary>
/// Интервал действия связки синтетического закрытия в истории маркеров:
/// связка действует от момента, когда существуют обе её сделки, до момента
/// спора; неоспоренные связки действуют без конца. Величина лока — та же
/// «зафиксировано синтетикой», что в строках связок карточки.
/// </summary>
/// <remarks>
/// Интервалы связок — материал событий истории: появление и снятие спором
/// дают маркеры с новым набором действующих связок.
// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-markers-history-events
/// </remarks>
public sealed record ResultMarkerLinkInterval
{
	/// <summary>Стабильный идентификатор связки — пара ключей её сделок; тай-брейк порядка событий.</summary>
	public required string LinkId { get; init; }

	/// <summary>Момент, с которого связка действует — время поздней из её сделок.</summary>
	public required DateTimeOffset Since { get; init; }

	/// <summary>Момент снятия связки спором; null — спор не записан, связка действует.</summary>
	public required DateTimeOffset? Until { get; init; }

	/// <summary>Величина «зафиксировано синтетикой» в USDT.</summary>
	public required decimal LockedValueUsdt { get; init; }
}

/// <summary>
/// Внешняя корректировка PnL в истории маркеров: момент, стабильный
/// идентификатор строки и знаковая сумма. Идентификатор упорядочивает
/// корректировки с равной датой между собой и с событиями того же момента.
/// </summary>
/// <remarks>
/// Каждая корректировка образует отдельный маркер изменения результата.
// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-markers-history-events
/// </remarks>
public sealed record ResultMarkerAdjustment
{
	/// <summary>Идентификатор строки корректировки.</summary>
	public required long AdjustmentId { get; init; }

	/// <summary>Дата корректировки.</summary>
	public required DateTimeOffset Date { get; init; }

	/// <summary>Знаковая сумма корректировки в USDT.</summary>
	public required decimal AmountUsdt { get; init; }
}

/// <summary>
/// Вход вычислителя маркеров изменения результата — срез журнала одной
/// конструкции: потоки позиций для значений срезов, привязанные сделки,
/// действующие закрывающие записи, интервалы связок, корректировки и
/// плановый риск для порога проявления. Все данные производные, хранением
/// не живут и пересчитываются при каждом чтении.
/// </summary>
/// <remarks>
/// Значения срезов обязаны сходиться с итогами сводки и метрик позиций:
/// потоки записей — те же, что питают аналитику.
// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-marker-value
/// </remarks>
public sealed record ResultMarkersInput
{
	/// <summary>
	/// Потоки FIFO-записей позиций конструкции по инструментам — те же записи,
	/// из которых аналитика считает реализованный результат; срезы значения
	/// маркера (реализованный результат и входная стоимость открытых слоёв)
	/// обязаны сходиться с итогами сводки и метрик позиций.
	/// </summary>
	public required IReadOnlyDictionary<string, IReadOnlyList<PositionFifoEntry>> EntriesBySymbol { get; init; }

	/// <summary>Привязанные сделки конструкции обоих источников — материал окон и фьючерсных событий.</summary>
	public required IReadOnlyList<MaterializedTrade> Trades { get; init; }

	/// <summary>Действующие закрывающие записи конструкции — delivery, OTM-экспирации и ручные пометки.</summary>
	public required IReadOnlyList<PositionClosingEntry> ClosingEntries { get; init; }

	/// <summary>Интервалы действия связок синтетического закрытия.</summary>
	public required IReadOnlyList<ResultMarkerLinkInterval> Links { get; init; }

	/// <summary>Внешние корректировки PnL конструкции.</summary>
	public required IReadOnlyList<ResultMarkerAdjustment> Adjustments { get; init; }

	/// <summary>Плановый риск конструкции в USDT — база порога проявления; null или ноль порога не образует.</summary>
	public required decimal? PlannedRiskUsdt { get; init; }
}
