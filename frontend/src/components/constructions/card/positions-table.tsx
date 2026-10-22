import * as React from "react";
import { Info, Pencil } from "lucide-react";
import type { ConstructionCardPosition } from "@/lib/api/construction-card";
import { fetchLastInstrumentQuote } from "@/lib/api/construction-card";
import { Button } from "@/components/ui/button";
import { MetricHint } from "@/components/ui/metric-tooltip";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { METRIC_TOOLTIPS } from "@/lib/metric-tooltips";
import { GridSection } from "./grid-section";
import { MarkdownEditDialog } from "@/components/markdown/markdown-edit-dialog";
import { MarkdownViewer } from "@/components/markdown/markdown-viewer";
import { DASH } from "@/lib/format/degradation";
import { formatMoment } from "@/lib/format/display-time";
import { EXPIRY_WARNING_DAYS } from "@/lib/lifetime";
import { formatAmount, formatSignedAmount, formatSignedPercent } from "@/lib/format/quantity";
import { cn } from "@/lib/utils";
import { useIsMobile } from "@/lib/use-mobile";

// Таблица позиций карточки по концепции §4: картина позиции её записями —
// вход, выход, стоимость, раздельные части результата, текущий результат,
// комиссии, времена, статус и inline-комментарий MD-рендером; единственное действие
// открытой строки — «закрыть пометкой» с пикером даты-времени и дефолтом
// последней котировки.
// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid

/** Пропсы таблицы позиций карточки. */
export interface PositionsTableProps {
	/** Идентификатор конструкции — владелицы позиций. */
	readonly constructionId: number;
	/** Строки позиций из снимка карточки. */
	readonly positions: readonly ConstructionCardPosition[];
	/** Состояние команды добавления пометки (для блокировки кнопок). */
	readonly annotationPending: boolean;
	/** Постановка ручной пометки закрытия позиции; promise разрешается только подтверждением сервера. */
	readonly onAddCloseAnnotation: (annotation: { symbol: string; annotatedAt: string; price: number | null; quantity: number | null }) => void | Promise<unknown>;
	/** Сохранение комментария позиции. */
	readonly onSaveComment: (input: { symbol: string; text: string | null }) => void;
}

/** Тон величины: положительная — зелёная, отрицательная — красная. */
function toneClass(value: number | null): string {
	if (value === null) {
		return "";
	}

	if (value > 0) {
		return "text-[color:var(--fin-positive-strong)]";
	}

	return value < 0 ? "text-[color:var(--fin-negative)]" : "";
}

/** Значение с процентом от капитала в скобках. */
function amountWithPercent(value: number | null, percent: number | null): React.ReactNode {
	const main = value === null ? null : (
		<span className={cn("tabular-nums", toneClass(value))}>{formatSignedAmount(value)}</span>
	);
	if (percent === null || value === null) {
		return main ?? <span className="text-muted-foreground">{DASH}</span>;
	}

	return (
		<span className={cn("tabular-nums", toneClass(value))}>
			{formatSignedAmount(value)}{" "}
			<span className="text-muted-foreground">({formatSignedPercent(percent)})</span>
		</span>
	);
}

/** Локальное значение момента для пикера datetime-local. */
export function toLocalInputValue(iso: string): string {
	const date = new Date(iso);
	const pad = (value: number) => String(value).padStart(2, "0");
	return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** Части опционного символа, нужные подсказке внутренней стоимости. */
interface OptionSymbolParts {
	/** Страйк опциона из строки символа. */
	readonly strike: number;
	/** Признак колла: сторона ноги определяет знак восстановления расчётной цены. */
	readonly isCall: boolean;
}

/** Разбор символа опциона {BASE}-{dMMMyy}-{СТРАЙК}-{C|P}[-{QUOTE}]; null — символ не опцион. */
function parseOptionSymbolParts(symbol: string): OptionSymbolParts | null {
	const segments = symbol.split("-");
	if (segments.length !== 4 && segments.length !== 5) {
		return null;
	}

	const strike = Number(segments[2]);
	if (Number.isFinite(strike) === false) {
		return null;
	}

	if (segments[3] === "C") {
		return { strike, isCall: true };
	}

	return segments[3] === "P" ? { strike, isCall: false } : null;
}

// Текст подсказки внутренней стоимости на расчётной цене экспирации —
// «|расчётная цена − страйк| = величина USDT» из мастера формы пометки.
// Расчётная цена (индекс) и страйк не приходят на клиент отдельными
// величинами: внутреннюю стоимость контракта read-модель считает из индекса
// существующего канала котировок и страйка, поэтому клиент восстанавливает
// компоненты подсказки из пары «внутренняя стоимость позиции ÷ остаток» и
// символа ноги. У OTM-ноги (нулевая внутренняя стоимость) и ног без рыночных
// данных (null) подсказка скрыта — она не блокирует ввод цены.
// Traceability: openspec:ui/screens#scenario-mark-form-intrinsic-hint
function intrinsicValueHint(position: ConstructionCardPosition): string | null {
	if (position.intrinsicValueUsdt === null || position.residual === 0) {
		return null;
	}

	const parts = parseOptionSymbolParts(position.symbol);
	if (parts === null) {
		return null;
	}

	// Внутренняя стоимость позиции — контрактная, масштабированная знаковым
	// остатком: делением на модуль остатка возвращается контрактная величина.
	const intrinsic = Math.abs(position.intrinsicValueUsdt) / Math.abs(position.residual);
	if (intrinsic === 0) {
		return null;
	}

	const settlementPrice = parts.isCall ? parts.strike + intrinsic : parts.strike - intrinsic;
	return `|${formatAmount(settlementPrice)} − ${formatAmount(parts.strike)}| = ${formatAmount(intrinsic)} USDT`;
}

// Подсветка зоны предупреждения: открытая опционная нога с остатком суток
// не более 14 подсвечивается фоном внимания; закрытые позиции и фьючерсные
// ноги (сутки null) подсветки не получают.
// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#scenario-detail-expiry-row-highlight
function isExpiryWarning(position: ConstructionCardPosition): boolean {
	return position.isOpen && position.daysToExpiry !== null && position.daysToExpiry <= EXPIRY_WARNING_DAYS;
}

// Тултип состава метрик времени жизни нативным title на ячейке стоимости
// открытой опционной ноги: внутренняя и временная стоимость позиции и
// динамика распада «вчера → сегодня»; отсутствующие величины выводятся
// прочерком.
// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#scenario-detail-lifetime-columns-and-tooltip
function lifetimeTooltip(position: ConstructionCardPosition): string {
	const intrinsic = position.intrinsicValueUsdt === null ? DASH : formatSignedAmount(position.intrinsicValueUsdt);
	const timeValue = position.timeValueUsdt === null ? DASH : formatSignedAmount(position.timeValueUsdt);
	const decayDayAgo = position.decayDayAgoUsdt === null ? DASH : formatSignedAmount(position.decayDayAgoUsdt);
	const decayNow = position.dailyDecayUsdt === null ? DASH : formatSignedAmount(position.dailyDecayUsdt);
	return [
		`${position.symbol} · состав позиции`,
		`внутренняя ${intrinsic} · временная ${timeValue}`,
		`распад вчера → сегодня: ${decayDayAgo} → ${decayNow}`,
	].join("\n");
}

export function PositionsTable({ constructionId, positions, annotationPending, onAddCloseAnnotation, onSaveComment }: PositionsTableProps) {
	const isMobile = useIsMobile();
	const [annotationSymbol, setAnnotationSymbol] = React.useState<string | null>(null);
	const [quotePrice, setQuotePrice] = React.useState("");
	const [annotationQuantity, setAnnotationQuantity] = React.useState("");
	const [annotationTime, setAnnotationTime] = React.useState("");
	const [commentTarget, setCommentTarget] = React.useState<ConstructionCardPosition | null>(null);
	const [annotationError, setAnnotationError] = React.useState<string | null>(null);
	const annotationRequest = React.useRef(0);

	// Цель формы пометки и производные подсказки: остаток и внутренняя
	// стоимость берутся из строки позиции, открытой в форме.
	const annotationTarget = annotationSymbol === null ? null : positions.find((position) => position.symbol === annotationSymbol) ?? null;
	const annotationRemainder = annotationTarget === null ? 0 : Math.abs(annotationTarget.residual);
	const intrinsicHintText = annotationTarget === null ? null : intrinsicValueHint(annotationTarget);
	const parsedAnnotationQuantity = annotationQuantity.trim() === "" ? null : Number(annotationQuantity.trim().replace(",", "."));
	const quantityHintText = parsedAnnotationQuantity !== null && Number.isFinite(parsedAnnotationQuantity)
		? `частичное закрытие — останется ${formatAmount(Math.max(annotationRemainder - parsedAnnotationQuantity, 0))}`
		: "незаданное количество закрывает весь остаток";

	// Открытие формы пометки: цена предзаполняется последней известной котировкой
	// инструмента, время — текущим моментом; сбой котировки виден у формы,
	// запоздавший ответ отменённой или другой формы не меняет цену.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	const beginAnnotation = async (symbol: string) => {
		const request = ++annotationRequest.current;
		setAnnotationSymbol(symbol);
		setAnnotationTime(toLocalInputValue(new Date().toISOString()));
		setQuotePrice("");
		setAnnotationQuantity("");
		setAnnotationError(null);
		try {
			const lastQuote = await fetchLastInstrumentQuote(constructionId, symbol);
			if (request === annotationRequest.current) {
				setQuotePrice(lastQuote === null ? "" : String(lastQuote));
			}
		} catch (error) {
			if (request === annotationRequest.current) {
				setAnnotationError(`Последняя котировка недоступна: ${error instanceof Error ? error.message : String(error)}. Задайте цену вручную или оставьте её пустой.`);
			}
		}
	};

	// Пометка ставится подтверждением сервера: форма скрывается только после
	// успешной постановки, при отказе остаётся открытой с причиной отказа.
	// Traceability: openspec:ui/screens#requirement-manual-close-mark-from-position
	const submitAnnotation = async () => {
		if (annotationSymbol === null) {
			return;
		}

		const price = quotePrice.trim() === "" ? null : Number(quotePrice.replace(",", "."));
		const quantity = parsedAnnotationQuantity;
		const time = new Date(annotationTime);
		// Ошибочное число не превращается в запрос последней котировки;
		// некорректные время и количество не отправляются и не обрушают форму.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		if ((price !== null && Number.isFinite(price) === false)
			|| (quantity !== null && Number.isFinite(quantity) === false)
			|| Number.isFinite(time.getTime()) === false) {
			setAnnotationError("Введите корректную цену, время и количество пометки.");
			return;
		}
		try {
			await onAddCloseAnnotation({ symbol: annotationSymbol, annotatedAt: time.toISOString(), price, quantity });
			++annotationRequest.current;
			setAnnotationSymbol(null);
		} catch (error) {
			setAnnotationError(`Пометка не поставлена: ${error instanceof Error ? error.message : String(error)}.`);
		}
	};

	return (
		// Секция-карточка сетки позиций по мастеру design.pen: головка с
		// заголовком и счётчиком «17 колонок · N».
		// Traceability: change:rework-spa-shell-and-screens/design#D7
		<GridSection dataSlot="positions-table" title="Позиции" columnCount={17} rowCount={positions.length}>
			{isMobile === false && annotationSymbol !== null && (
				// Форма ручной пометки закрытия по мастеру «Мастер/Пометка закрытия ·
				// форма»: время и цена (котировка подставится при чтении), подсказка
				// внутренней стоимости и опциональное количество.
				<div className="bg-card flex flex-wrap items-end gap-3 rounded-md border p-3">
					<span className="text-sm font-medium">закрыть позицию «{annotationSymbol}» пометкой</span>
					<label className="flex flex-col gap-1 text-sm">
						время пометки
						<input
							type="datetime-local"
							className="border-input bg-background h-9 rounded-md border px-2 text-sm"
							value={annotationTime}
							onChange={(event) => setAnnotationTime(event.target.value)}
						/>
					</label>
					<label className="flex flex-col gap-1 text-sm">
						цена закрытия
						<input
							className="border-input bg-background h-9 w-40 rounded-md border px-2 text-sm"
							placeholder="пусто — последняя котировка"
							value={quotePrice}
							onChange={(event) => {
								++annotationRequest.current;
								setQuotePrice(event.target.value);
							}}
						/>
					</label>
					{intrinsicHintText !== null && (
						// Подсказка внутренней стоимости на расчётной цене экспирации
						// для опционной ноги в деньгах — блок $infoSoft из мастера
						// «Мастер/Пометка закрытия · форма»: ряд с info-иконкой и
						// подписью 11.5, под ним значение 13/600. Клиент восстанавливает
						// компоненты «|расчётная цена − страйк|» из внутренней стоимости
						// позиции — её read-модель считает из индекса существующего канала
						// котировок и страйка — и остатка; у OTM-ноги и без рыночных
						// данных подсказка скрыта и ввод цены не блокирует.
						// Traceability: openspec:ui/screens#scenario-mark-form-intrinsic-hint
						<div data-slot="annotation-intrinsic-hint" className="bg-info-soft flex w-full flex-col gap-[3px] rounded-sm px-2.5 py-2">
							<span className="flex items-center gap-1 text-[11.5px] font-medium text-info">
								<Info aria-hidden className="size-3 shrink-0" />
								внутренняя стоимость на расчётной цене
							</span>
							<span className="text-[13px] font-semibold text-foreground">{intrinsicHintText}</span>
						</div>
					)}
					<label className="flex flex-col gap-1 text-sm">
						{/* Поле опционального количества из мастера: незаданное закрывает
						    весь остаток на момент пометки, заданное клэмпится остатком
						    производным слоем; подсказка показывает, сколько останется
						    после частичного закрытия. */}
						{/* Traceability: openspec:ui/screens#scenario-mark-form-partial-quantity */}
						количество
						<input
							data-slot="annotation-quantity-input"
							aria-label="количество пометки"
							className="border-input bg-background h-9 w-28 rounded-md border px-2 text-sm"
							inputMode="decimal"
							placeholder={`весь остаток — ${formatAmount(annotationRemainder)}`}
							value={annotationQuantity}
							onChange={(event) => setAnnotationQuantity(event.target.value)}
						/>
						<span className="text-[10.5px] font-normal text-text-muted">{quantityHintText}</span>
					</label>
					<Button size="sm" disabled={annotationPending || annotationTime === ""} onClick={() => void submitAnnotation()}>
						Поставить пометку
					</Button>
					<Button size="sm" variant="outline" onClick={() => {
						++annotationRequest.current;
						setAnnotationSymbol(null);
					}}>
						Отмена
					</Button>
					{annotationError !== null && <p className="text-destructive text-sm" role="alert">{annotationError}</p>}
				</div>
			)}

			{/* Плотность Body #2: ячейки 7/6 кегль 11, заголовки 9.5/600 на
			    заливке surface-2; значения вторичным текстом, инструмент —
			    основным. */}
			{/* Traceability: change:reconcile-frontend-with-design/design#D6 */}
			{/* Traceability: change:rework-spa-shell-and-screens/design#D7 */}
			<Table density="compact" className="text-text-secondary">
				{/* Подсказка числовой колонки живёт в её заголовке: триггер
				    оборачивает текст заголовка целиком, механика заголовков
				    не меняется. */}
				{/* Traceability: openspec:ui/screens#requirement-column-header-tooltips */}
				<TableHeader className="bg-surface-2">
					<TableRow className="border-divider">
						<TableHead>Инструмент</TableHead>
						<TableHead>Остаток</TableHead>
						<TableHead>Вх. цена</TableHead>
						<TableHead>Закр. цена</TableHead>
						<TableHead>
							<MetricHint tooltip={METRIC_TOOLTIPS.positionValue}>Стоимость</MetricHint>
						</TableHead>
						<TableHead>
							<MetricHint tooltip={METRIC_TOOLTIPS.priceChangePercent}>Изм., %</MetricHint>
						</TableHead>
						{/* Колонки времени жизни опционной ноги по мастеру таблицы
						    позиций design.pen: сутки и распад между изменением цены
						    и реализованной частью P&L. */}
						{/* Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#scenario-detail-lifetime-columns-and-tooltip */}
						<TableHead>
							<MetricHint tooltip={METRIC_TOOLTIPS.daysToExpiry}>До экспирации</MetricHint>
						</TableHead>
						<TableHead>
							<MetricHint tooltip={METRIC_TOOLTIPS.decayPerDay}>Распад</MetricHint>
						</TableHead>
						{/* Колонки результата позиции двухпоказательные: подсказка
						    заголовка даёт раздельный раздел на каждый показатель —
						    значение в USDT и процент от капитала. */}
						{/* Traceability: openspec:ui/screens#scenario-column-tooltip-sections-per-indicator */}
						<TableHead>
							<MetricHint tooltip={METRIC_TOOLTIPS.positionRealizedPnl}>Реализ. P&amp;L</MetricHint>
						</TableHead>
						<TableHead>
							<MetricHint tooltip={METRIC_TOOLTIPS.positionUnrealizedPnl}>Нереализ. P&amp;L</MetricHint>
						</TableHead>
						{/* Колонка текущего результата позиции: при сбое котировок
						    нереализованная часть недоступна и ячейка помечается
						    признаком сбоя, рыночные части остаются видимыми. */}
						{/* Traceability: change:unify-current-result/specs/ui/screens/spec#scenario-detail-position-total-pnl-marks-failure */}
						<TableHead>
							<MetricHint tooltip={METRIC_TOOLTIPS.positionTotalPnl}>Текущий результат</MetricHint>
						</TableHead>
						<TableHead>
							<MetricHint tooltip={METRIC_TOOLTIPS.commissionFees}>Комиссии</MetricHint>
						</TableHead>
						{/* Колонки дат и времён подсказками в заголовках не снабжаются. */}
						{/* Traceability: openspec:ui/screens#scenario-date-columns-skip-tooltip */}
						<TableHead>Открыта</TableHead>
						<TableHead>Закрыта</TableHead>
						<TableHead>Статус</TableHead>
						<TableHead>Комментарий</TableHead>
						<TableHead>Действия</TableHead>
					</TableRow>
				</TableHeader>
				<TableBody>
					{positions.length === 0 ? (
						<TableRow>
								<TableCell colSpan={17} className="text-muted-foreground py-6 text-center">
								позиций нет
							</TableCell>
						</TableRow>
					) : (
						positions.map((position) => (
								// Разделитель строк — токен divider #EBEBE5 по мастеру Body #2;
								// строка открытой опционной ноги в зоне предупреждения
								// экспирации подсвечивается фоном внимания.
								// Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#scenario-detail-expiry-row-highlight
								<TableRow key={position.symbol} className={cn("border-divider", isExpiryWarning(position) && "bg-risk-soft")}>
								<TableCell className="font-medium text-foreground">{position.symbol}</TableCell>
								<TableCell className="tabular-nums">
									{position.syntheticResidual === null ? (
										position.syntheticClosedQuantity === null ? (
											formatAmount(position.residual)
										) : (
											// Признак закрытия синтетикой опционной ноги: факт
											// действующих связок под неизменным инструментальным
											// остатком; значения приходят из read model по факту
											// связок — фронт ничего не вычисляет.
											// Traceability: openspec:ui/screens#requirement-position-table-synthetic-close-annotation
											<div
												data-slot="option-leg-synthetic-split"
												className="flex flex-col gap-[2px]"
												title="Закрыто синтетикой — сумма действующих связок ноги; открыто — остаток минус закрытое синтетикой. Инструментальный остаток не меняется"
											>
												<span>{formatAmount(position.residual)}</span>
												<span className="text-[9.5px] text-text-muted">
													{`закрыто синтетикой ${formatAmount(position.syntheticClosedQuantity)} · открыто ${formatAmount(position.syntheticOpenQuantity ?? 0)}`}
												</span>
											</div>
										)
									) : (
										// Распад остатка линейной ноги (мастер aU7Nx/LFEkd):
										// синтетический хедж связки и прочий (скальперский)
										// остаток раздельно, инструментальный остаток в
										// главной строке не меняется.
										// Traceability: openspec:ui/screens#scenario-detail-linear-leg-splits-synthetic-and-scalps
										<div
											data-slot="linear-leg-residual-split"
											className="flex flex-col gap-[2px]"
											title="Остаток распадается на синтетический хедж связки и прочий (скальперский) остаток"
										>
											<span>{formatAmount(position.residual)}</span>
											<span className="text-[9.5px] text-text-muted">
												{`синтетика ${formatAmount(position.syntheticResidual)} · прочее ${formatAmount(position.otherResidual ?? 0)}`}
											</span>
										</div>
									)}
								</TableCell>
								<TableCell className="tabular-nums">{position.averageEntryPrice === null ? DASH : formatAmount(position.averageEntryPrice)}</TableCell>
								<TableCell className="tabular-nums">{position.averageClosePrice === null ? DASH : formatAmount(position.averageClosePrice)}</TableCell>
								{/* Тултип состава открытой опционной ноги живёт на ячейке
								    стоимости: внутренняя/временная стоимость и динамика
								    распада «вчера → сегодня»; у сбоя котировок — своё
								    пояснение, закрытая и фьючерсная ноги без тултипа. */}
								{/* Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#scenario-detail-lifetime-columns-and-tooltip */}
								<TableCell>
									{position.isOpen === false ? (
										<span className="text-muted-foreground">{DASH}</span>
									) : position.quoteValue === null ? (
										<span className="text-muted-foreground" title="Провайдер котировок недоступен — стоимость не оценена">
											сбой котировок
										</span>
									) : (
										<span
											className={cn("tabular-nums", toneClass(position.quoteValue))}
											title={position.daysToExpiry === null ? undefined : lifetimeTooltip(position)}
										>
											{formatSignedAmount(position.quoteValue)}
										</span>
									)}
								</TableCell>
								<TableCell>
									{position.isOpen === false ? (
										<span className="text-muted-foreground">{DASH}</span>
									) : position.priceChangePercent === null ? (
										<span className="text-muted-foreground" title="Провайдер котировок недоступен — процент изменения цены не оценён">
											сбой котировок
										</span>
									) : (
										<span className={cn("tabular-nums", toneClass(position.priceChangePercent))}>
											{formatSignedPercent(position.priceChangePercent)}
										</span>
									)}
								</TableCell>
								{/* Ячейки времени жизни открытой опционной ноги: сутки
								    (красные при ≤ 14) и распад со знаком; закрытая позиция
								    и фьючерсная нога показывают прочерк. Тултип состава
								    снят с этих ячеек и живёт на стоимости. */}
								{/* Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#scenario-detail-lifetime-columns-and-tooltip */}
								{/* Traceability: change:add-option-lifetime-metrics/specs/ui/screens/spec#scenario-detail-lifetime-degradation */}
								<TableCell className="tabular-nums">
									{position.daysToExpiry === null ? (
										<span className="text-muted-foreground">{DASH}</span>
									) : (
										<span className={cn("tabular-nums", position.daysToExpiry <= EXPIRY_WARNING_DAYS && "text-destructive")}>
											{position.daysToExpiry}
										</span>
									)}
								</TableCell>
								<TableCell className="tabular-nums">
									{position.dailyDecayUsdt === null ? (
										<span className="text-muted-foreground">{DASH}</span>
									) : (
										<span className={cn("tabular-nums", toneClass(position.dailyDecayUsdt))}>
											{formatSignedAmount(position.dailyDecayUsdt)}
										</span>
									)}
								</TableCell>
								<TableCell>{amountWithPercent(position.realizedPnL, position.realizedPnLPercent)}</TableCell>
								<TableCell>
									{position.isOpen === false ? (
										<span className="text-muted-foreground">{DASH}</span>
									) : position.unrealizedPnL === null ? (
										<span className="text-muted-foreground" title="Провайдер котировок недоступен — нереализованный PnL не оценён">
											сбой котировок
										</span>
									) : (
										amountWithPercent(position.unrealizedPnL, position.unrealizedPnLPercent)
									)}
								</TableCell>
								<TableCell>
									{position.isOpen && position.totalPnL === null ? (
										<span className="text-muted-foreground" title="Провайдер котировок недоступен — текущий результат не оценён">
											сбой котировок
										</span>
									) : (
										amountWithPercent(position.totalPnL, position.totalPnLPercent)
									)}
								</TableCell>
								<TableCell className="tabular-nums">{formatAmount(position.accumulatedFees)}</TableCell>
								<TableCell>{formatMoment(position.openedAt)}</TableCell>
								<TableCell>{position.closedAt === null ? DASH : formatMoment(position.closedAt)}</TableCell>
								<TableCell>
									{/* Статус — нейтральный приглушённый текст в обоих состояниях. */}
									<span className="text-muted-foreground">
										{position.isOpen ? "открыта" : "закрыта"}
									</span>
								</TableCell>
								<TableCell>
									<div className="flex items-center gap-1">
										<div className="max-w-56 truncate" title={position.comment ?? undefined}>
											{position.comment === null ? (
												<span className="text-muted-foreground">{DASH}</span>
											) : (
												<MarkdownViewer text={position.comment} />
											)}
										</div>
										{/* Иконка правки комментария — pencil 12 приглушённым текстом. */}
										<Button
											variant="ghost"
											size="icon"
											className="size-5"
											aria-label={`комментарий позиции ${position.symbol}`}
											title="Править комментарий"
											onClick={() => setCommentTarget(position)}
										>
											<Pencil aria-hidden className="size-3 text-text-muted" />
										</Button>
									</div>
								</TableCell>
								<TableCell>
									{/* Пометки закрытия редактируются на desktop; чтение доступно с телефона. */}
									{/* Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив */}
									{position.isOpen && isMobile === false ? (
										// Мелкая кнопка действия строки: радиус 6, паддинг [3,7], кегль 9.5.
										<Button
											variant="outline"
											size="sm"
											className="h-auto rounded-[6px] px-[7px] py-[3px] text-[9.5px] text-text-secondary"
											disabled={annotationPending}
											onClick={() => void beginAnnotation(position.symbol)}
										>
											закрыть пометкой…
										</Button>
									) : (
										<span className="text-muted-foreground">{DASH}</span>
									)}
								</TableCell>
							</TableRow>
						))
					)}
				</TableBody>
			</Table>

			{/* Модальный split-редактор комментария позиции: тулбар + live-превью,
			    Ctrl+Enter — сохранить (§10). */}
			<MarkdownEditDialog
				open={commentTarget !== null}
				title={`Комментарий позиции ${commentTarget?.symbol ?? ""}`}
				description="Комментарий привязан к ключу «конструкция × инструмент» и не зависит от остатка."
				defaultText={commentTarget?.comment ?? ""}
				onSave={(text) => {
					if (commentTarget !== null) {
						onSaveComment({ symbol: commentTarget.symbol, text: text.trim() === "" ? null : text });
					}

					setCommentTarget(null);
				}}
				onOpenChange={(open) => {
					if (open === false) {
						setCommentTarget(null);
					}
				}}
			/>
		</GridSection>
	);
}
