// Справочник текстов подсказок метрик: формулировки выводятся из глоссария
// и живых спецификаций — подсказка называет назначение метрики всегда, а
// метод расчёта формулирует только тогда, когда он зафиксирован в источнике;
// метод, отсутствующий в источниках, в текст подсказки не придумывается.
// Traceability: openspec:ui/screens#requirement-metric-tooltip-text-sourced

// Раздел подсказки многопоказательной метрики: по одному разделу
// на каждый показатель ячейки или колонки.
export interface MetricTooltipSection {
	readonly label: string;
	readonly text: string;
}

// Текст подсказки метрики: заголовок метрики, назначение — всегда,
// метод расчёта — только когда он зафиксирован в источнике.
export interface MetricTooltip {
	readonly title: string;
	readonly purpose: string;
	readonly method?: string;
	readonly sections?: readonly MetricTooltipSection[];
}

// Ключи показателей, снабжаемых подсказкой метрики; дат и времён
// (период, «котировки на», открытие/закрытие) в справочнике нет.
export type MetricTooltipKey =
	| "totalPnl"
	| "capitalPercent"
	| "constructionValue"
	| "capitalUsage"
	| "realizedPnl"
	| "unrealizedPnl"
	| "adjustmentsCount"
	| "syntheticLocked"
	| "adjustedTotal"
	| "daysToExpiry"
	| "decayPerDay"
	| "timeValue"
	| "priceChangePercent"
	| "positionValue"
	| "positionRealizedPnl"
	| "positionUnrealizedPnl"
	| "positionTotalPnl"
	| "commissionFees"
	| "tradeQuantity"
	| "tradePrice"
	| "tradeAmount"
	| "tradeCommission"
	| "adjustmentValue"
	| "closingEntriesAmount"
	| "journalTotal"
	| "hintBadge"
	| "warningBadge"
	| "plannedRisk"
	| "plannedProfit"
	| "worstCase"
	| "totalMarker"
	| "listCapital"
	| "listTotal"
	| "constructionCounters"
	| "closingEntryDetails";

export const METRIC_TOOLTIPS: { readonly [K in MetricTooltipKey]: MetricTooltip } = {
	// Итог конструкции в сводке карточки: суммирует реализованный
	// и нереализованный P&L позиций и внешние корректировки PnL.
	// Traceability: openspec:analytics/performance#requirement-construction-metrics
	totalPnl: {
		title: "Общий P&L",
		purpose:
			"Итог конструкции: суммарный результат её позиций с учётом внешних корректировок PnL.",
		method:
			"Сумма реализованного и нереализованного P&L позиций и внешних корректировок PnL.",
	},

	// Ячейка «% капитала (N USDT)» объединяет два показателя — процент
	// результата от капитала и сам выделенный капитал; подсказка даёт
	// раздел на каждый показатель.
	// Traceability: openspec:ui/screens#scenario-multi-indicator-sections
	// Traceability: openspec:analytics/performance#scenario-percent-from-current-capital
	capitalPercent: {
		title: "% от капитала",
		purpose:
			"Процент результата конструкции от выделенного капитала и сам выделенный капитал.",
		sections: [
			{
				label: "% от капитала",
				text: "Результат конструкции в процентах от текущего значения выделенного капитала; считается только при заданном ненулевом капитале.",
			},
			{
				label: "Капитал, USDT",
				text: "Выделенный капитал конструкции — база процентных метрик.",
			},
		],
	},

	// Стоимость открытых остатков конструкции: сумма нетто-стоимостей
	// позиций по текущим маркам; без открытых остатков стоимости нет.
	// Traceability: openspec:analytics/performance#requirement-mark-value-of-position-and-construction
	constructionValue: {
		title: "Стоимость",
		purpose:
			"Стоимость открытых остатков конструкции в USDT; у конструкции без открытых остатков стоимости нет.",
		method:
			"Сумма нетто-стоимостей позиций: текущая марка × знаковый чистый остаток, со знаком.",
	},

	// Процент занятости капитала: отношение стоимости конструкции
	// к текущему выделенному капиталу.
	// Traceability: openspec:analytics/performance#requirement-capital-usage-from-value
	capitalUsage: {
		title: "Занято капитала, %",
		purpose:
			"Какая доля выделенного капитала занята стоимостью открытых остатков конструкции.",
		method:
			"Стоимость конструкции, делённая на текущий выделенный капитал; знак наследует стоимости, вычисляется только при заданном ненулевом капитале и доступной стоимости.",
	},

	// Ячейка реализованного P&L объединяет значение в USDT и процент
	// от капитала; реализованный результат — собственный FIFO-расчёт
	// с комиссиями.
	// Traceability: openspec:ui/screens#scenario-multi-indicator-sections
	// Traceability: openspec:analytics/performance#requirement-realized-pnl-own-fifo
	realizedPnl: {
		title: "Реализованный P&L",
		purpose:
			"Результат закрытых частей позиций конструкции и его доля от выделенного капитала.",
		sections: [
			{
				label: "Значение, USDT",
				text: "Реализованный P&L: FIFO-сопоставление собственных сделок и закрывающих записей; комиссии уменьшают результат.",
			},
			{
				label: "% от капитала",
				text: "Реализованный P&L в процентах от текущего выделенного капитала.",
			},
		],
	},

	// Ячейка нереализованного P&L объединяет оценку открытых остатков
	// и её процент от капитала; оценка — по текущим маркам на момент запроса.
	// Traceability: openspec:ui/screens#scenario-multi-indicator-sections
	// Traceability: openspec:analytics/performance#requirement-unrealized-pnl-current-marks
	unrealizedPnl: {
		title: "Нереализованный P&L",
		purpose:
			"Оценка открытых остатков конструкции и её доля от выделенного капитала.",
		sections: [
			{
				label: "Значение, USDT",
				text: "Нереализованный P&L: оценка открытых остатков по маркам публичных тикеров на момент запроса; при сбое марок величина отсутствует.",
			},
			{
				label: "% от капитала",
				text: "Нереализованный P&L в процентах от текущего выделенного капитала.",
			},
		],
	},

	// Сумма корректировок сводки: внешние корректировки PnL конструкции
	// от робота или ручные; входят в итог конструкции.
	// Traceability: openspec:ui/screens#requirement-adjustments-in-detail
	// Traceability: openspec:analytics/performance#requirement-construction-metrics
	adjustmentsCount: {
		title: "Корректировки",
		purpose:
			"Внешние корректировки PnL конструкции — знаковые суммы в USDT от робота или ручные; входят в итог конструкции.",
	},

	// Строка «зафиксировано синтетикой»: лок связки к экспирации
	// и логический размер закрываемой ноги; формула зафиксирована
	// в спецификации синтетических закрытий.
	// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-locked-value
	syntheticLocked: {
		title: "Зафиксировано синтетикой",
		purpose:
			"Паритетный лок связки синтетического закрытия: платёж, зафиксированный к экспирации, и логический размер ноги после связки.",
		sections: [
			{
				label: "Лок, USDT",
				text: "К экспирации: количество связки × |страйк − цена линейной сделки| + премия встречного опциона − стоимость входа закрываемой части ноги по FIFO − комиссии связки и приходящейся по FIFO доли комиссий входа; от текущих марок не зависит.",
			},
			{
				label: "Логический размер ноги",
				text: "Закрываемая нога показывается сокращённой на количество связки; инструментальные остатки позиций не меняются.",
			},
		],
	},

	// Сводная величина зоны синтетики: «Реализованный результат» плюс
	// сумма локов действующих связок; публикуется только при наличии
	// действующих связок и не меняет инструментальные метрики.
	// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-adjusted-total
	// Traceability: doc:GLOSSARY.md#С учётом зафиксированного синтетикой
	adjustedTotal: {
		title: "С учётом зафиксированного синтетикой",
		purpose:
			"Сводная величина карточки: «Реализованный результат» плюс локи действующих синтетических связок; публикуется только при наличии действующих связок.",
		method:
			"«Реализованный результат» + Σ локов «зафиксировано синтетикой» действующих связок (распознанные минус оспоренные плюс ручные); вычисляется при чтении, от текущих марок не зависит.",
	},

	// Остаток суток до ближайшей экспирации: минимум по открытым
	// опционным ногам конструкции; вычисляется и без рыночных данных.
	// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
	daysToExpiry: {
		title: "До экспирации",
		purpose:
			"Сколько полных суток осталось до ближайшей экспирации среди открытых опционных ног конструкции.",
		method:
			"Минимум остатков суток по открытым опционным ногам; остаток ноги — floor((время доставки − сейчас) / 24ч) по UTC — вычисляется и без рыночных данных.",
	},

	// Нетто-дневной распад конструкции: сумма распадов опционных ног
	// со знаком; распад ноги — биржевая тета на чистый остаток позиции.
	// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
	// Traceability: openspec:analytics/option-lifetime#requirement-leg-decay-and-days-to-expiry
	decayPerDay: {
		title: "Распад в сутки",
		purpose:
			"Чистый распад конструкции за сутки от теты открытых опционных ног.",
		method:
			"Сумма дневных распадов открытых опционных ног со знаком; распад ноги — биржевая тета контракта, умноженная на чистый остаток позиции.",
	},

	// Суммарная временная стоимость открытых опционных остатков:
	// марка минус внутренняя стоимость, на чистый остаток.
	// Traceability: openspec:analytics/option-lifetime#requirement-leg-intrinsic-time-value
	// Traceability: openspec:analytics/option-lifetime#requirement-construction-lifetime-summary
	timeValue: {
		title: "Временная стоимость",
		purpose:
			"Суммарная временная стоимость открытых опционных остатков конструкции.",
		method:
			"Марка минус внутренняя стоимость, на контракт и на чистый остаток, суммарно по открытым опционным ногам; внутренняя стоимость — max(0, цена базового актива − страйк) для колла и max(0, страйк − цена) для пута.",
	},

	// «Изм., %» — движение текущей марки от средней цены открытого остатка
	// со знаком, приведённым к направлению позиции: плюс — «в прибыль».
	// Traceability: openspec:analytics/performance#requirement-open-remainder-price-change-percent
	priceChangePercent: {
		title: "Изм., %",
		purpose:
			"Насколько процентов текущая марка отошла от средней цены открытого остатка позиции.",
		method:
			"Процент изменения текущей марки относительно средней цены открытого остатка со знаком, приведённым к направлению остатка: плюс означает движение цены «в прибыль» позиции.",
	},

	// Стоимость колонки «Стоимость» таблицы позиций — нетто-величина
	// «текущая марка × знаковый остаток» открытой позиции.
	// Traceability: openspec:analytics/performance#requirement-mark-value-of-position-and-construction
	positionValue: {
		title: "Стоимость",
		purpose:
			"Стоимость открытого остатка позиции в USDT; закрытая позиция стоимости не имеет.",
		method:
			"Нетто-величина «текущая марка × знаковый чистый остаток»: длинный остаток даёт плюс, короткий — минус.",
	},

	// Колонка «Реализ. P&L» таблицы позиций: колонка с двумя показателями
	// даёт раздельные разделы о значении в USDT и о проценте от капитала.
	// Traceability: openspec:ui/screens#scenario-column-tooltip-sections-per-indicator
	// Traceability: openspec:analytics/performance#requirement-realized-pnl-own-fifo
	positionRealizedPnl: {
		title: "Реализ. P&L",
		purpose:
			"Результат закрытых частей позиции и его доля от выделенного капитала конструкции.",
		sections: [
			{
				label: "Значение, USDT",
				text: "Реализованный P&L позиции: FIFO-сопоставление её сделок и закрывающих записей; комиссии уменьшают результат.",
			},
			{
				label: "% от капитала",
				text: "Реализованный P&L позиции в процентах от текущего выделенного капитала конструкции.",
			},
		],
	},

	// Колонка «Нереализ. P&L» таблицы позиций: оценка открытого остатка
	// по текущим маркам и процент от капитала раздельными разделами.
	// Traceability: openspec:ui/screens#scenario-column-tooltip-sections-per-indicator
	// Traceability: openspec:analytics/performance#requirement-unrealized-pnl-current-marks
	positionUnrealizedPnl: {
		title: "Нереализ. P&L",
		purpose:
			"Оценка открытого остатка позиции и её доля от выделенного капитала конструкции.",
		sections: [
			{
				label: "Значение, USDT",
				text: "Нереализованный P&L позиции: оценка открытого остатка по маркам публичных тикеров на момент запроса; у закрытой позиции — прочерк.",
			},
			{
				label: "% от капитала",
				text: "Нереализованный P&L позиции в процентах от текущего выделенного капитала конструкции.",
			},
		],
	},

	// Колонка «Общий P&L» таблицы позиций: сумма реализованной
	// и нереализованной частей и процент от капитала.
	// Traceability: openspec:ui/screens#scenario-column-tooltip-sections-per-indicator
	// Traceability: openspec:analytics/performance#scenario-position-total-pnl-includes-unrealized
	positionTotalPnl: {
		title: "Общий P&L",
		purpose:
			"Полный результат позиции и его доля от выделенного капитала конструкции.",
		sections: [
			{
				label: "Значение, USDT",
				text: "Общий P&L позиции: реализованный P&L плюс нереализованная оценка открытого остатка; у закрытой позиции равен реализованному.",
			},
			{
				label: "% от капитала",
				text: "Общий P&L позиции в процентах от текущего выделенного капитала конструкции.",
			},
		],
	},

	// «Всего комиссий» позиции: комиссии записей исполнения, которые
	// уменьшают реализованный результат.
	// Traceability: openspec:analytics/performance#scenario-fees-reduce-result
	commissionFees: {
		title: "Всего комиссий",
		purpose:
			"Накопленные комиссии позиции из записей исполнения; комиссии уменьшают реализованный результат.",
	},

	// Сырая колонка сделок: количество инструмента в сделке — только
	// назначение, формула расчёта не утверждается.
	// Traceability: openspec:ui/screens#requirement-construction-detail-screen
	tradeQuantity: {
		title: "Кол-во",
		purpose:
			"Количество инструмента в сделке, в контрактах.",
	},

	// Сырая колонка сделок: цена исполнения сделки — только назначение.
	// Traceability: openspec:ui/screens#requirement-construction-detail-screen
	tradePrice: {
		title: "Цена",
		purpose:
			"Цена исполнения сделки в USDT за контракт.",
	},

	// Сырая колонка сделок: сумма сделки — только назначение.
	// Traceability: openspec:ui/screens#requirement-construction-detail-screen
	tradeAmount: {
		title: "Сумма",
		purpose:
			"Сумма сделки в USDT.",
	},

	// Сырая колонка сделок: комиссия исполнения — уменьшает результат,
	// но сама формулой не считается.
	// Traceability: openspec:ui/screens#requirement-construction-detail-screen
	// Traceability: openspec:analytics/performance#scenario-fees-reduce-result
	tradeCommission: {
		title: "Комиссия",
		purpose:
			"Комиссия биржи за исполнение сделки; уменьшает реализованный результат.",
	},

	// Сумма корректировки: знаковая величина в USDT; входит в сумму
	// корректировок сводки и в итог конструкции.
	// Traceability: openspec:ui/screens#requirement-adjustments-in-detail
	adjustmentValue: {
		title: "Сумма",
		purpose:
			"Знаковая сумма внешней корректировки PnL в USDT; входит в сумму корректировок сводки и в итог конструкции.",
	},

	// Сумма закрывающей записи (delivery, OTM-экспирация, ручная пометка):
	// входит в FIFO-расчёт результата наравне со встречной сделкой.
	// Traceability: openspec:ui/screens#requirement-construction-detail-screen
	// Traceability: openspec:analytics/performance#requirement-realized-pnl-own-fifo
	closingEntriesAmount: {
		title: "Сумма",
		purpose:
			"Сумма закрывающей записи — поставки, экспирации OTM или ручной пометки; учитывается в FIFO-расчёте результата наравне со встречной сделкой.",
	},

	// Сводка шапки «Итог журнала»: разбивка на реализованный
	// и нереализованный PnL журнала; сбой марок хотя бы одной конструкции
	// гасит нереализованную часть.
	// Traceability: openspec:analytics/performance#requirement-journal-pnl-aggregates
	journalTotal: {
		title: "Итог журнала",
		purpose:
			"Суммарный результат всех конструкций журнала, включая архивные, с разбивкой на реализованный и нереализованный PnL.",
		sections: [
			{
				label: "Реализованный, USDT",
				text: "Сумма реализованных PnL всех конструкций журнала, включая архивные; доступна всегда.",
			},
			{
				label: "Нереализованный, USDT",
				text: "Сумма нереализованных оценок конструкций; отсутствует при сбое марок хотя бы одной конструкции.",
			},
		],
	},

	// Бейдж «✦» в строке списка: число живых подсказок конструкции;
	// отсутствует у конструкций без живых подсказок.
	// Traceability: openspec:ui/screens#requirement-ui-construction-hint-badge
	hintBadge: {
		title: "Живые подсказки",
		purpose:
			"Число живых подсказок конструкции (новые, включая непросмотренные); у конструкций без живых подсказок бейдж отсутствует.",
	},

	// Бейдж «⚠ N» в строке списка: предупреждения конструкции — диагностика
	// незакрытых рисков по текущим открытым остаткам.
	// Traceability: openspec:ui/screens#requirement-construction-warnings-display
	warningBadge: {
		title: "Предупреждения",
		purpose:
			"Число предупреждений конструкции — диагностик незакрытых рисков по текущим открытым остаткам.",
	},

	// Метка планового риска индикатора: плановая оценка потерь к экспирации,
	// задаёт левую границу шкалы; в состояниях не конечного реального риска
	// деление подписывается именно плановым риском.
	// Traceability: doc:GLOSSARY.md#Плановый риск
	// Traceability: openspec:ui/screens#requirement-risk-profit-hint
	plannedRisk: {
		title: "Плановый риск",
		purpose:
			"Расчётный риск конструкции к экспирации по плановым ценам: сумма, которую конструкция может потерять, если все сделки дойдут до экспирации; задаёт левую границу и красную зону шкалы индикатора.",
	},

	// Метка планового профита индикатора: плановая прибыль к экспирации,
	// задаёт зелёную зону шкалы от нуля до +Профит.
	// Traceability: openspec:ui/screens#requirement-risk-profit-hint
	plannedProfit: {
		title: "Плановый профит",
		purpose:
			"Плановая прибыль конструкции к экспирации: задаёт зелёную зону шкалы индикатора от нуля до +Профит; за границей начинается сверхприбыль.",
	},

	// Подпись границы худшего исхода индикатора: знаковая величина рядом
	// с реальным риском, из структуры ног и средних цен открытых остатков.
	// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
	// Traceability: doc:GLOSSARY.md#Худший исход
	// Traceability: doc:GLOSSARY.md#Граница худшего исхода
	worstCase: {
		title: "Худший исход",
		purpose:
			"Наименее благоприятный результат конструкции при дожитии открытых остатков до экспирации; определяет позицию и подпись границы худшего исхода индикатора.",
		method:
			"Реализованный результат плюс суммарный минимум платежа групп открытых остатков (до клэмпа нулём): при остаточном риске — «реализованный минус реальный риск», при гарантированной прибыли остатков — выше реализованного; от текущих марок не зависит.",
	},

	// Маркер итога на шкале индикатора: оценка мгновенной ликвидации
	// остатков по средним маркам, без комиссий закрытия.
	// Traceability: doc:GLOSSARY.md#Маркер итога
	// Traceability: openspec:ui/screens#requirement-risk-profit-hint
	totalMarker: {
		title: "Маркер итога",
		purpose:
			"Точка текущего итога на шкале индикатора: реализованный плюс нереализованный результат — оценка мгновенной ликвидации остатков по средним маркам, без комиссий закрытия.",
	},

	// Колонка «Капитал» списка конструкций: выделенный капитал конструкции;
	// показывается прочерком при незаданном значении.
	// Traceability: openspec:ui/screens#requirement-construction-list-screen
	listCapital: {
		title: "Капитал",
		purpose:
			"Выделенный капитал конструкции — база процентных метрик; показывается прочерком при незаданном значении.",
	},

	// Колонка «Итог» списка конструкций: итог конструкции с разделами
	// реализованного и нереализованного результата.
	// Traceability: openspec:ui/screens#requirement-construction-list-screen
	// Traceability: openspec:analytics/performance#requirement-construction-metrics
	listTotal: {
		title: "Итог",
		purpose:
			"Итог конструкции в строке списка: суммарный результат её позиций с учётом внешних корректировок PnL.",
		sections: [
			{
				label: "Реализованный, USDT",
				text: "Реализованный P&L позиций конструкции.",
			},
			{
				label: "Нереализованный, USDT",
				text: "Нереализованная оценка открытых остатков конструкции по текущим маркам.",
			},
		],
	},

	// Счётчики шапки списка «конструкций N · открыто N · архив N»: полный
	// набор журнала, открытые и архив (все минус открытые); считаются
	// по полному набору независимо от области видимости списка.
	// Traceability: openspec:ui/screens#requirement-construction-list-screen
	constructionCounters: {
		title: "Счётчики конструкций",
		purpose:
			"Полный набор конструкций журнала, из них открытых, и архив — остальные (закрытые и архивные); счётчики считаются по полному набору и не зависят от выбранной области видимости списка.",
	},

	// Колонка «Детали» закрывающих записей: количество закрытых контрактов
	// и цена закрытия — два показателя ячейки, подсказка даёт раздел
	// на каждый; сырые данные записи, без метода расчёта.
	// Traceability: openspec:ui/screens#requirement-column-header-tooltips
	closingEntryDetails: {
		title: "Детали",
		purpose:
			"Количество закрытых контрактов и цена закрытия в закрывающей записи: сколько контрактов закрыто и по какой цене.",
		sections: [
			{
				label: "Количество",
				text: "Сколько контрактов инструмента закрыла эта запись.",
			},
			{
				label: "Цена",
				text: "Цена закрытия, по которой запись вошла в расчёт результата.",
			},
		],
	},
};
