using TransactionJournal.Domain;

namespace TransactionJournal.Application;

/// <summary>
/// Строка конструкции для таблицы экрана «Конструкции»: заголовок конструкции
/// (имя и ручной статус), соединённый с метриками аналитики, — ровно те столбцы,
/// что выводит список, без производных величин деталей.
/// </summary>
/// <param name="ConstructionId">Идентификатор конструкции.</param>
/// <param name="Name">Имя конструкции.</param>
/// <param name="Status">Ручной статус конструкции.</param>
/// <param name="AllocatedCapitalUsdt">Выделенный капитал конструкции в USDT; null, когда капитал не задан.</param>
/// <param name="RiskPercent">Риск в процентах от капитала: введённые проценты либо вычисленные из введённых USDT; null, когда величины нет.</param>
/// <param name="RiskUsdt">Риск в USDT: введённые USDT либо вычисленные из введённых процентов; null, когда величины нет.</param>
/// <param name="ProfitPercent">Профит в процентах от капитала: введённые проценты либо вычисленные из введённых USDT; null, когда величины нет.</param>
/// <param name="ProfitUsdt">Профит в USDT: введённые USDT либо вычисленные из введённых процентов; null, когда величины нет.</param>
/// <param name="RealizedPnL">Реализованный PnL конструкции.</param>
/// <param name="UnrealizedPnL">Нереализованный PnL; null при недоступной оценке марок.</param>
/// <param name="AdjustmentsPnL">Сумма внешних корректировок PnL конструкции.</param>
/// <param name="TotalPnL">Итог конструкции; null при недоступной оценке марок.</param>
/// <param name="TotalPnLPercent">Итог в процентах от капитала; null без базы процентов или при недоступном итоге.</param>
/// <param name="OpenedAt">Дата открытия — время первой сделки; null без сделок.</param>
/// <param name="ClosedAt">Дата закрытия — момент обнуления последней позиции; null у открытой конструкции.</param>
/// <param name="MarkValue">Стоимость открытых позиций конструкции по маркам; null при закрытой конструкции или недоступной оценке марок.</param>
/// <param name="CapitalUsagePercent">Занятость капитала — стоимость в процентах от капитала; null без базы процентов или стоимости.</param>
/// <param name="RealRiskUsdt">Реальный риск конструкции в USDT — наихудший результат открытых остатков на экспирации; null, когда худший случай неограничен или символ остатка не разобран.</param>
// Капитал передаётся незаданным как есть: прочерк вместо значения — решение
// представления, подмена нулём вводила бы ложную базу процентов.
// Traceability: openspec:ui/screens#scenario-list-no-capital-percent-dash
// Величины риска и профита выводятся обеими единицами чистым конвертером:
// введённая единица первоисточника, незаполненная вычисляется от капитала.
// Traceability: openspec:analytics/performance#requirement-risk-profit-unit-conversion
// Стоимость и занятость капитала переносятся из метрик аналитики как есть:
// правила оценки и деградации при сбое марок остаются в аналитике.
// Traceability: openspec:ui/screens#scenario-list-value-and-capital-usage-columns
// Traceability: openspec:analytics/performance#requirement-mark-value-of-position-and-construction
// Реальный риск публикуется в контракте строки списка: величина переносится
// из метрик аналитики как есть, правила расчёта и null остаются в аналитике.
// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
public sealed record ConstructionListItem(
	long ConstructionId,
	string Name,
	ConstructionStatus Status,
	decimal? AllocatedCapitalUsdt,
	decimal? RiskPercent,
	decimal? RiskUsdt,
	decimal? ProfitPercent,
	decimal? ProfitUsdt,
	decimal RealizedPnL,
	decimal? UnrealizedPnL,
	decimal AdjustmentsPnL,
	decimal? TotalPnL,
	decimal? TotalPnLPercent,
	DateTimeOffset? OpenedAt,
	DateTimeOffset? ClosedAt,
	decimal? MarkValue = null,
	decimal? CapitalUsagePercent = null,
	decimal? RealRiskUsdt = null);

/// <summary>
/// Данные экрана «Конструкции»: сводка журнала — итог с разбивкой на
/// реализованный и нереализованный PnL, отметка времени марок и счётчик
/// конструкций с числом открытых, — и строки таблицы конструкций.
/// Счётчик описывает видимые (неархивные) конструкции: скрытые из списка
/// в счётчике не числятся.
/// </summary>
/// <param name="TotalPnL">Итог по журналу; null, пока сбой марок оставляет его неполным.</param>
/// <param name="RealizedPnL">Реализованный PnL журнала — сумма реализованных частей всех конструкций, включая архивные; сбой марок его не затрагивает.</param>
/// <param name="UnrealizedPnL">Нереализованный PnL журнала; null при сбое марок хотя бы одной конструкции.</param>
/// <param name="MarksAsOf">Отметка времени марок оценки; null при сбое марок или без открытых остатков.</param>
/// <param name="HasMarkFailure">Признак сбоя марок: провайдер не оценил хотя бы один открытый остаток журнала.</param>
/// <param name="ConstructionCount">Число видимых конструкций списка.</param>
/// <param name="OpenCount">Число конструкций со статусом «открыта» среди видимых.</param>
/// <param name="Items">Строки таблицы конструкций, упорядоченные по статусу и датам.</param>
// Разбивка итога переносится из метрик аналитики как есть: правила агрегации
// и null-деградации принадлежат аналитике, модель списка их не повторяет.
// Traceability: openspec:analytics/performance#requirement-journal-pnl-aggregates
public sealed record ConstructionListData(
	decimal? TotalPnL,
	decimal RealizedPnL,
	decimal? UnrealizedPnL,
	DateTimeOffset? MarksAsOf,
	bool HasMarkFailure,
	int ConstructionCount,
	int OpenCount,
	IReadOnlyList<ConstructionListItem> Items);
