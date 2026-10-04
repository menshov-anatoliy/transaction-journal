using TransactionJournal.Application.Analytics;
using TransactionJournal.Domain;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Materialization;

namespace TransactionJournal.Application;

/// <summary>
/// Строка таблицы позиций деталей конструкции: итоговая картина позиции
/// в терминах её записей — вход, выход, результат, общий P&L,
/// комиссии, времена — с комментарием позиции; ровно те столбцы, что выводит
/// экран, без служебных величин оценки открытого остатка.
/// </summary>
/// <param name="Symbol">Инструмент позиции.</param>
/// <param name="Residual">Чистый остаток: положителен для длинной, отрицателен для короткой; ноль — закрыта.</param>
/// <param name="AverageEntryPrice">Средняя цена входа — количество-взвешенная цена всех открывающих частей FIFO-потока; null, если открывающих частей нет.</param>
/// <param name="AverageClosePrice">Средняя цена закрытия — количество-взвешенная цена всех закрывающих частей потока; null, если закрывающих частей нет.</param>
/// <param name="RealizedPnL">Реализованный PnL — FIFO-результат встречных частей минус комиссии записей; виден всегда, в том числе при сбое марок.</param>
/// <param name="RealizedPnLPercent">Реализованный P&L процентом от выделенного капитала конструкции; null при нулевом капитале.</param>
/// <param name="UnrealizedPnL">Нереализованный PnL открытой позиции — оценка марками на момент запроса; null при сбое марок, у закрытой позиции нереализованной части нет.</param>
/// <param name="UnrealizedPnLPercent">Нереализованный P&L процентом от выделенного капитала конструкции; null вместе с нереализованным P&L или при нулевом капитале.</param>
/// <param name="TotalPnL">Общий PnL позиции: реализованный плюс нереализованная оценка; null у открытой позиции при сбое марок.</param>
/// <param name="TotalPnLPercent">Общий P&L процентом от выделенного капитала конструкции; null вместе с общим P&L или при нулевом капитале.</param>
/// <param name="AccumulatedFees">Накопленные комиссии записей позиции: уплаченные складываются, rebate снижает сумму.</param>
/// <param name="OpenedAt">Время открытия — время первой записи позиции.</param>
/// <param name="ClosedAt">Время закрытия — момент обнуления остатка; null, пока позиция открыта.</param>
/// <param name="IsOpen">Позиция открыта, пока остаток не нулевой.</param>
/// <param name="Comment">Комментарий позиции по ключу «конструкция × инструмент»; null — комментария нет.</param>
// Строка показывает картину позиции её записями: вход, выход,
// раздельные части реализованного и нереализованного результата
// с процентом от капитала, общий P&L с процентом, комиссии и времена.
// Traceability: openspec:ui/screens#scenario-detail-position-row-entry-close-total
// Раздельные части выводятся колонками «Реализ. P&L» и «Нереализ. P&L»:
// реализованная часть видна всегда, нереализованная деградирует вместе с марками.
// Traceability: openspec:ui/screens#scenario-detail-position-pnl-parts
public sealed record ConstructionPositionRow(
	string Symbol,
	decimal Residual,
	decimal? AverageEntryPrice,
	decimal? AverageClosePrice,
	decimal RealizedPnL,
	decimal? RealizedPnLPercent,
	decimal? UnrealizedPnL,
	decimal? UnrealizedPnLPercent,
	decimal? TotalPnL,
	decimal? TotalPnLPercent,
	decimal AccumulatedFees,
	DateTimeOffset OpenedAt,
	DateTimeOffset? ClosedAt,
	bool IsOpen,
	string? Comment);

/// <summary>
/// Строка таблицы сделок деталей конструкции: сделка с атрибутами биржевой
/// записи и комментарием пользователя, привязанная к этой конструкции.
/// </summary>
/// <param name="ExecId">Биржевой идентификатор исполнения — ключ сделки.</param>
/// <param name="Symbol">Инструмент сделки.</param>
/// <param name="ExecutedAt">Время исполнения сделки.</param>
/// <param name="IsBuy">true — покупка, false — продажа.</param>
/// <param name="Quantity">Количество без знака; направление несёт отдельное поле.</param>
/// <param name="Price">Цена исполнения из биржевой записи.</param>
/// <param name="AmountUsdt">Сумма сделки — количество, умноженное на цену, в USDT.</param>
/// <param name="Fee">Комиссия со знаком биржевой записи: положительная уплачена, отрицательная — rebate.</param>
/// <param name="Comment">Комментарий сделки; null — комментария нет.</param>
public sealed record ConstructionTradeRow(
	string ExecId,
	string Symbol,
	DateTimeOffset ExecutedAt,
	bool IsBuy,
	decimal Quantity,
	decimal Price,
	decimal AmountUsdt,
	decimal Fee,
	string? Comment);

/// <summary>
/// Строка таблицы закрывающих записей деталей конструкции: запись единого
/// потока закрывающих записей — delivery, экспирация OTM или ручная пометка —
/// с количеством, эффективной ценой и суммой закрытия.
/// </summary>
/// <param name="ClosedAt">Момент закрытия: время биржевой записи либо время ручной пометки.</param>
/// <param name="Kind">Вид записи: delivery, экспирация OTM или ручная пометка.</param>
/// <param name="Symbol">Инструмент закрываемой позиции.</param>
/// <param name="Quantity">Знаковое количество, обнулившее остаток на момент применения.</param>
/// <param name="Price">Эффективная цена закрытия; null — марка инструмента неизвестна.</param>
/// <param name="AmountUsdt">Сумма закрытия — денежный поток записи со знаком; null при неизвестной цене.</param>
/// <param name="ManualMarkId">Идентификатор ручной пометки для правки и удаления из таблицы; null у биржевых записей.</param>
public sealed record ConstructionClosingEntryRow(
	DateTimeOffset ClosedAt,
	PositionClosingKind Kind,
	string Symbol,
	decimal Quantity,
	decimal? Price,
	decimal? AmountUsdt,
	long? ManualMarkId = null);

/// <summary>
/// Строка таблицы внешних корректировок PnL деталей конструкции.
/// </summary>
/// <param name="AdjustmentId">Идентификатор корректировки — ключ будущих правок и удаления.</param>
/// <param name="Date">Дата корректировки.</param>
/// <param name="Description">Описание корректировки; null — описания нет.</param>
/// <param name="Source">Источник корректировки: «робот» или «ручная».</param>
/// <param name="AmountUsdt">Знаковая сумма корректировки в USDT.</param>
public sealed record ConstructionAdjustmentRow(
	long AdjustmentId,
	DateTimeOffset Date,
	string? Description,
	PnLAdjustmentSource Source,
	decimal AmountUsdt);

/// <summary>
/// Данные экрана деталей конструкции: заголовок с комментарием, сводка метрик
/// с периодом и отметкой марок и четыре таблицы записей — позиции, сделки,
/// закрывающие записи и внешние корректировки PnL. Пустая таблица передаётся
/// пустым списком — экран показывает явное сообщение об отсутствии записей.
/// </summary>
/// <param name="ConstructionId">Идентификатор конструкции.</param>
/// <param name="Name">Имя конструкции.</param>
/// <param name="Status">Ручной статус конструкции.</param>
/// <param name="AllocatedCapitalUsdt">Выделенный капитал конструкции в USDT; null, когда капитал не задан.</param>
/// <param name="RiskPercent">Риск в процентах от капитала: введённые проценты либо вычисленные из введённых USDT; null, когда величины нет.</param>
/// <param name="RiskUsdt">Риск в USDT: введённые USDT либо вычисленные из введённых процентов; null, когда величины нет.</param>
/// <param name="ProfitPercent">Профит в процентах от капитала: введённые проценты либо вычисленные из введённых USDT; null, когда величины нет.</param>
/// <param name="ProfitUsdt">Профит в USDT: введённые USDT либо вычисленные из введённых процентов; null, когда величины нет.</param>
/// <param name="RiskUnit">Единица ввода риска — первоисточник параметра; null, когда риск не задан.</param>
/// <param name="ProfitUnit">Единица ввода профита — первоисточник параметра; null, когда профит не задан.</param>
/// <param name="Comment">Комментарий конструкции; null — комментария нет.</param>
/// <param name="Metrics">Метрики конструкции: итог, разбивка, проценты, период и длительность.</param>
/// <param name="HasOpenResidual">У конструкции есть открытый остаток — марки нужны её нереализованной оценке.</param>
/// <param name="HasMarkFailure">Сбой марок оставил нереализованную оценку конструкции непостроенной.</param>
/// <param name="MarksAsOf">Отметка времени марок оценки из аналитики журнала.</param>
/// <param name="Positions">Строки таблицы позиций, упорядоченные по инструменту.</param>
/// <param name="Trades">Строки таблицы сделок в хронологическом порядке.</param>
/// <param name="ClosingEntries">Строки таблицы закрывающих записей в хронологическом порядке.</param>
/// <param name="ClosingWarnings">Предупреждения об избыточных закрывающих записях конструкции.</param>
/// <param name="Adjustments">Строки таблицы корректировок, упорядоченные по дате.</param>
// Капитал передаётся незаданным как есть: скрытие процентов — решение
// представления, подмена нулём вводила бы ложную базу процентов.
// Traceability: openspec:ui/screens#scenario-detail-no-capital-no-percent
// Величины риска и профита сводки выводятся обеими единицами чистым
// конвертером: введённая единица первоисточник, вторая вычисляется от капитала.
// Traceability: openspec:analytics/performance#requirement-risk-profit-unit-conversion
public sealed record ConstructionDetailData(
	long ConstructionId,
	string Name,
	ConstructionStatus Status,
	decimal? AllocatedCapitalUsdt,
	decimal? RiskPercent,
	decimal? RiskUsdt,
	decimal? ProfitPercent,
	decimal? ProfitUsdt,
	string? Comment,
	ConstructionMetrics Metrics,
	bool HasOpenResidual,
	bool HasMarkFailure,
	DateTimeOffset? MarksAsOf,
	IReadOnlyList<ConstructionPositionRow> Positions,
	IReadOnlyList<ConstructionTradeRow> Trades,
	IReadOnlyList<ConstructionClosingEntryRow> ClosingEntries,
	IReadOnlyList<RedundantClosingEntryWarning> ClosingWarnings,
	IReadOnlyList<ConstructionAdjustmentRow> Adjustments,
	TargetUnit? RiskUnit = null,
	TargetUnit? ProfitUnit = null);
