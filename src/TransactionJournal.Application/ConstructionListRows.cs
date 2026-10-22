using TransactionJournal.Domain;
using TransactionJournal.Application.Analytics;

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
/// <param name="UnrealizedPnL">Нереализованный PnL; null при недоступной оценке котировок.</param>
/// <param name="AdjustmentsPnL">Сумма внешних корректировок PnL конструкции.</param>
/// <param name="TotalPnL">Итог конструкции; null при недоступной оценке котировок.</param>
/// <param name="TotalPnLPercent">Итог в процентах от капитала; null без базы процентов или при недоступном итоге.</param>
/// <param name="OpenedAt">Дата открытия — время первой сделки; null без сделок.</param>
/// <param name="ClosedAt">Дата закрытия — момент обнуления последней позиции; null у открытой конструкции.</param>
/// <param name="QuoteValue">Стоимость открытых позиций конструкции по котировкам; null при закрытой конструкции или недоступной оценке котировок.</param>
/// <param name="CapitalUsagePercent">Занятость капитала — стоимость в процентах от капитала; null без базы процентов или стоимости.</param>
/// <param name="RealRiskUsdt">Реальный риск конструкции в USDT — наихудший результат открытых остатков на экспирации; null, когда худший случай неограничен или символ остатка не разобран — причина различима статусом.</param>
/// <param name="WorstCaseUsdt">Худший исход конструкции в USDT — знаковая величина: реализованный результат плюс неклэмпированный минимум платежа групп открытых остатков; null вместе с реальным риском в состояниях `unbounded`/`unavailable`.</param>
/// <param name="RealRiskStatus">Состояние реального риска: конечный (`finite`), неограниченный (`unbounded`) или нерассчитанный (`unavailable`); незаданный читается как «не рассчитан», а не как конечный.</param>
/// <param name="RiskWarningsCount">Число предупреждений диагностики незакрытых рисков — счётчик бейджа «⚠ N» строки; ноль — предупреждений нет.</param>
/// <param name="DaysToNearestExpiry">Остаток полных суток до ближайшей экспирации открытых опционных ног; null без опционных ног.</param>
/// <param name="NetDailyDecayUsdt">Нетто-дневной распад открытых опционных ног со знаком, USDT/день; null без опционных ног или без тикеров.</param>
// Капитал передаётся незаданным как есть: прочерк вместо значения — решение
// представления, подмена нулём вводила бы ложную базу процентов.
// Traceability: openspec:ui/screens#scenario-list-no-capital-percent-dash
// Величины риска и профита выводятся обеими единицами чистым конвертером:
// введённая единица первоисточника, незаполненная вычисляется от капитала.
// Traceability: openspec:analytics/performance#requirement-risk-profit-unit-conversion
// Стоимость и занятость капитала переносятся из метрик аналитики как есть:
// правила оценки и деградации при сбое котировок остаются в аналитике.
// Traceability: openspec:ui/screens#scenario-list-value-and-capital-usage-columns
// Traceability: openspec:analytics/performance#requirement-mark-value-of-position-and-construction
// Реальный риск публикуется в контракте строки списка: величина переносится
// из метрик аналитики как есть, правила расчёта и null остаются в аналитике.
// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
// Статус проходит рядом с величиной без потери: null без статуса дал бы
// представлению двусмысленность — неограниченный хвост и неполные данные
// надо различать, а конечный ноль не превращать в отсутствие величины.
// Traceability: openspec:analytics/performance#scenario-real-risk-unbounded-is-null
// Traceability: openspec:analytics/performance#scenario-real-risk-unparseable-symbol-is-null
// Худший исход идёт рядом с реальным риском со знаком и тем же статусом:
// насечке индикатора нужна знаковая величина, а не клэмпированные потери.
// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
// Traceability: openspec:analytics/performance#scenario-worst-case-null-with-status
// Счётчик предупреждений переносится из метрик аналитики как есть: расчёт по
// открытым остаткам остаётся в аналитике, строка списка и превью показывают
// число бейджем; ноль означает отсутствие бейджа.
// Traceability: openspec:analytics/risk-diagnostics#requirement-risk-diagnostics-on-open-residuals
// Колонки времени жизни проходят из саммари аналитики как есть: конструкция
// без открытых опционных ног несёт прочерки, деградация без тикеров решена
// в аналитике и список её не повторяет.
// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#scenario-list-lifetime-columns
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
	decimal? QuoteValue = null,
	decimal? CapitalUsagePercent = null,
	decimal? RealRiskUsdt = null,
	decimal? WorstCaseUsdt = null,
	RealRiskStatus RealRiskStatus = RealRiskStatus.Unavailable,
	int RiskWarningsCount = 0,
	int? DaysToNearestExpiry = null,
	decimal? NetDailyDecayUsdt = null);

/// <summary>
/// Данные экрана «Конструкции»: сводка журнала — итог с разбивкой на
/// реализованный и нереализованный PnL, отметка времени котировок и счётчик
/// конструкций с числом открытых, — и строки таблицы конструкций.
/// Счётчик описывает видимые (неархивные) конструкции: скрытые из списка
/// в счётчике не числятся.
/// </summary>
/// <param name="TotalPnL">Итог по журналу; null, пока сбой котировок оставляет его неполным.</param>
/// <param name="RealizedPnL">Реализованный PnL журнала — сумма реализованных частей всех конструкций, включая архивные; сбой котировок его не затрагивает.</param>
/// <param name="UnrealizedPnL">Нереализованный PnL журнала; null при сбое котировок хотя бы одной конструкции.</param>
/// <param name="QuotesAsOf">Отметка времени котировок оценки; null при сбое котировок или без открытых остатков.</param>
/// <param name="HasQuoteFailure">Признак сбоя котировок: провайдер не оценил хотя бы один открытый остаток журнала.</param>
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
	DateTimeOffset? QuotesAsOf,
	bool HasQuoteFailure,
	int ConstructionCount,
	int OpenCount,
	IReadOnlyList<ConstructionListItem> Items);
