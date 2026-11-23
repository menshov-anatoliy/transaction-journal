namespace TransactionJournal.Application.Analytics;

using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Analytics;

/// <summary>
/// Вид операции расшифровки маркера и события истории результата конструкции:
/// открытие и закрытие опционных окон, ролл, связки синтетического закрытия,
/// закрытие фьючерсной позиции, корректировка PnL и поставка. Вид
/// вычислительный — выводится из вида записи журнала, а не задаётся
/// пользователем; отдельный вид «Усреднение» не вводится, усредняющее окно
/// неразличимо с открытием.
/// </summary>
/// <remarks>
/// Вид операции — вычислительное свойство записей журнала конструкции.
// Traceability: openspec:analytics/performance#requirement-result-markers-history-events
// Traceability: change:fix-finresult-marker-contributions/design#d3
/// </remarks>
public enum ResultMarkerKind
{
	/// <summary>Окно опционных исполнений без закрывающих сделок — открытие позиции; усреднение входит сюда же.</summary>
	Opening,

	/// <summary>Окно опционных исполнений с закрывающими сделками и без открывающих — закрытие позиций окна.</summary>
	Closing,

	/// <summary>Окно опционных исполнений с закрывающими и открывающими сделками — ролл целиком одна операция.</summary>
	Roll,

	/// <summary>Активация связки синтетического закрытия — вклад «зафиксировано синтетикой».</summary>
	SyntheticClose,

	/// <summary>Снятие связки синтетического закрытия спором — вклад с обратным знаком.</summary>
	SyntheticRelease,

	/// <summary>Закрытие фьючерсной позиции до нулевого остатка — финрезультат цикла по FIFO с комиссиями.</summary>
	FuturesClose,

	/// <summary>Внешняя корректировка PnL.</summary>
	PnLAdjustment,

	/// <summary>Закрывающая запись — поставка delivery по внутренней стоимости.</summary>
	Delivery,
}

/// <summary>
/// Строка детализации операции расшифровки маркера сделкой: инструмент,
/// сторона, количество × цена и индивидуальная сумма вклада сделки. Сумма
/// стоит только у сделок окна — там она складывается во вклад операции;
/// у записей фьючерсного цикла и закрывающих записей вклад не раскладывается
/// и остаётся null.
/// </summary>
/// <remarks>
/// Детализация задаётся матрицей видов операций: окна раскрывают вклад
/// сделками, фьючерсный цикл перечисляет записи без сумм, поставка и ручное
/// закрытие показывают инструмент и количество × эффективную цену.
// Traceability: openspec:analytics/performance#requirement-result-markers-contract
// Traceability: openspec:analytics/performance#scenario-marker-operations-sum-to-change
/// </remarks>
/// <param name="Symbol">Инструмент сделки либо закрывающей записи.</param>
/// <param name="Side">Сторона сделки по знаку количества: buy — покупка, sell — продажа; null — сторона не выводится (закрывающая запись).</param>
/// <param name="Quantity">Количество по модулю.</param>
/// <param name="Price">Цена сделки либо эффективная цена закрывающей записи.</param>
/// <param name="ContributionUsdt">Знаковый вклад сделки в USDT; null — вид операции не раскладывает вклад по сделкам.</param>
public sealed record ResultMarkerTradeDetail(
	string Symbol,
	string? Side,
	decimal Quantity,
	decimal Price,
	decimal? ContributionUsdt);

/// <summary>
/// Операция расшифровки маркера: вид, момент события и знаковый вклад
/// в накопленный зафиксированный результат. Перечень операций маркера
/// охватывает все события после предыдущего выведенного маркера, включая
/// подавленные порогом, — результат сверяется сложением строк расшифровки.
/// </summary>
/// <remarks>
/// Сумма вкладов перечня равняется изменению маркера, а накопленный итог
/// предыдущего выведенного маркера плюс сумма перечня — накопленному итогу
/// маркера; оба инварианта проверяются тестами вычислителя. Каждая операция
/// детализируется своим материалом: окна — сделками окна с индивидуальными
/// суммами, складывающимися во вклад операции, фьючерсный цикл — записями
/// цикла без индивидуальных сумм, поставка и ручное закрытие — инструментом
/// и количеством × эффективной ценой, связка — периодом действия;
/// корректировка детализации не несёт.
// Traceability: openspec:analytics/performance#requirement-result-markers-contract
// Traceability: openspec:analytics/performance#scenario-marker-operations-sum-to-change
/// </remarks>
public sealed record ResultMarkerOperation
{
	/// <summary>Вид операции: те же виды, что у событий истории результата.</summary>
	public required ResultMarkerKind Kind { get; init; }

	/// <summary>Момент события журнала, породившего операцию.</summary>
	public required DateTimeOffset EventTime { get; init; }

	/// <summary>Знаковый вклад операции в накопленный результат в USDT.</summary>
	public required decimal AmountUsdt { get; init; }

	/// <summary>
	/// Детализация операции сделками: у окон — все исполнения окна со
	/// знаковыми суммами вкладов, складывающимися во вклад операции;
	/// у фьючерсного цикла — записи цикла без индивидуальных сумм (вклад
	/// принадлежит циклу целиком); у поставки и ручного закрытия — одна строка
	/// «инструмент · количество × эффективная цена». Пустой список — вид
	/// операции не раскладывает вклад по сделкам (связки, корректировки).
	/// </summary>
	public IReadOnlyList<ResultMarkerTradeDetail> Trades { get; init; } = [];

	/// <summary>Момент, с которого действует связка синтетического закрытия; null — операция не является связкой.</summary>
	public DateTimeOffset? PeriodSince { get; init; }

	/// <summary>Момент снятия связки спором; null — связка действует без конца либо операция не является связкой.</summary>
	public DateTimeOffset? PeriodUntil { get; init; }
}

/// <summary>
/// Маркер изменения результата — выведенная точка истории накопленного
/// зафиксированного денежного результата конструкции. Номер присвоен только
/// выведенным маркерам и идёт подряд без пропусков; изменение считается
/// относительно предыдущего выведенного маркера (у первого — от нуля) и
/// равняется сумме вкладов перечня операций; накопленный итог — сумма вкладов
/// всех операций до события включительно. Значение выводится из вкладов
/// операций, а не из среза сводной формулы.
/// </summary>
/// <remarks>
/// Номер, изменение, итог и перечень операций вычисляются на сервере:
/// порог проявления и перенумерация применены до публикации контракта.
// Traceability: openspec:analytics/performance#requirement-result-marker-value
// Traceability: openspec:analytics/performance#requirement-result-marker-threshold
/// </remarks>
public sealed record ResultMarker
{
	/// <summary>Порядковый номер выведенного маркера — сквозной, без пропусков.</summary>
	public required int Number { get; init; }

	/// <summary>Время события журнала, породившего маркер.</summary>
	public required DateTimeOffset EventTime { get; init; }

	/// <summary>Вид операции, породившей маркер.</summary>
	public required ResultMarkerKind Kind { get; init; }

	/// <summary>Изменение накопленного результата относительно предыдущего выведенного маркера; у первого — от нуля.</summary>
	public required decimal ChangeUsdt { get; init; }

	/// <summary>Накопленный зафиксированный результат на момент события в USDT.</summary>
	public required decimal TotalUsdt { get; init; }

	/// <summary>
	/// Перечень операций расшифровки — все события после предыдущего
	/// выведенного маркера, включая подавленные порогом; у первого маркера —
	/// все события от начала истории. Сумма вкладов перечня равна изменению.
	/// </summary>
	public required IReadOnlyList<ResultMarkerOperation> Operations { get; init; }
}

/// <summary>
/// История маркеров изменения результата конструкции: выведенные маркеры
/// после порога проявления и перенумерации. Последний маркер выводится
/// безусловно и равняется текущему накопленному зафиксированному результату;
/// у конструкции без событий истории маркеров нет.
/// </summary>
/// <remarks>
/// Нумерация сплошная по выведенным маркерам, подряд без пропусков.
// Traceability: openspec:analytics/performance#requirement-result-marker-threshold
/// </remarks>
public sealed record ConstructionResultMarkers
{
	/// <summary>Выведенные маркеры в порядке событий журнала; пустой список, когда событий нет.</summary>
	public required IReadOnlyList<ResultMarker> Markers { get; init; }

	/// <summary>Последний выведенный маркер — текущий накопленный зафиксированный результат; null без событий.</summary>
	public ResultMarker? LastMarker => Markers.Count == 0 ? null : Markers[^1];
}

/// <summary>
/// Итоговая сводка последнего маркера — накопленный зафиксированный
/// результат, время события и перечень операций расшифровки для контрактов
/// списка и превью, где полная история не публикуется. Значение то же, что
/// у последнего выведенного маркера карточки: накопленный зафиксированный
/// результат конструкции — сумма вкладов операций перечня.
/// </summary>
/// <remarks>
/// Сводка публикуется без номера: компактные виды рисуют последний маркер
/// без бейджа нумерации; перечень операций совпадает с перечнем последнего
/// маркера карточки, сумма вкладов равна изменению маркера.
// Traceability: openspec:analytics/performance#requirement-result-markers-contract
// Traceability: openspec:analytics/performance#scenario-list-preview-publish-last-marker
/// </remarks>
public sealed record ResultMarkerSummary(
	decimal TotalUsdt,
	DateTimeOffset EventAt,
	IReadOnlyList<ResultMarkerOperation> Operations);

/// <summary>
/// Интервал действия связки синтетического закрытия в истории маркеров:
/// связка действует от момента, когда существуют обе её сделки, до момента
/// спора; неоспоренные связки действуют без конца. Величина лока — та же
/// «зафиксировано синтетикой», что в строках связок карточки.
/// </summary>
/// <remarks>
/// Интервалы связок — материал событий истории: активация даёт вклад лока,
/// снятие спором — тот же лок с обратным знаком.
// Traceability: openspec:analytics/performance#requirement-result-markers-history-events
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
/// Каждая корректировка образует отдельную операцию с вкладом — суммой.
// Traceability: openspec:analytics/performance#requirement-result-markers-history-events
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
/// Вход вычислителя маркеров изменения результата — материал истории одной
/// конструкции: потоки позиций для фьючерсных циклов, привязанные сделки,
/// действующие закрывающие записи, интервалы связок, корректировки и
/// плановый риск для порога проявления. Все данные производные, хранением
/// не живут и пересчитываются при каждом чтении.
/// </summary>
/// <remarks>
/// Вклады операций обязаны сходиться с итогами сводки и метрик позиций:
/// сделки и закрывающие записи — те же, что питают аналитику.
// Traceability: openspec:analytics/performance#requirement-result-marker-value
/// </remarks>
public sealed record ResultMarkersInput
{
	/// <summary>
	/// Потоки FIFO-записей позиций конструкции по инструментам — те же записи,
	/// из которых аналитика считает реализованный результат; по линейным
	/// инструментам из них выводятся события закрытия фьючерсных циклов
	/// с комиссиями. Потоки опционных символов вычислителем не читаются:
	/// вклады окон считаются по сделкам.
	/// </summary>
	public required IReadOnlyDictionary<string, IReadOnlyList<PositionFifoEntry>> EntriesBySymbol { get; init; }

	/// <summary>Привязанные сделки конструкции обоих источников — материал окон, их классификации и вкладов.</summary>
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
