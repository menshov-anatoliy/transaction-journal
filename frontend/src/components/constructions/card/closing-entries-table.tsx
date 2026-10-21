import * as React from "react";
import type { ConstructionCardClosingEntry, ConstructionCardClosingWarning } from "@/lib/api/construction-card";
import { Button } from "@/components/ui/button";
import { MetricHint } from "@/components/ui/metric-tooltip";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { GridSection } from "./grid-section";
import { DASH } from "@/lib/format/degradation";
import { formatMoment } from "@/lib/format/display-time";
import { formatAmount, formatSignedAmount } from "@/lib/format/quantity";
import { METRIC_TOOLTIPS } from "@/lib/metric-tooltips";
import { toLocalInputValue } from "./positions-table";
import { cn } from "@/lib/utils";
import { useIsMobile } from "@/lib/use-mobile";

// Таблица закрывающих записей карточки по концепции §4: единый поток
// delivery, экспираций OTM и ручных пометок; предупреждения об избыточных
// записях видны над таблицей; ручные пометки правятся и удаляются по месту
// с пикерами даты-времени и цены.
// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid

/** Пропсы таблицы закрывающих записей. */
export interface ClosingEntriesTableProps {
	/** Строки закрывающих записей из снимка карточки. */
	readonly entries: readonly ConstructionCardClosingEntry[];
	/** Предупреждения об избыточных закрывающих записях. */
	readonly warnings: readonly ConstructionCardClosingWarning[];
	/** Состояние команд пометки (для блокировки кнопок). */
	readonly actionsPending: boolean;
	/** Правка ручной пометки закрытия. */
	readonly onEditMark: (input: { markId: number; mark: { symbol: string; markedAt: string; price: number | null; quantity: number | null } }) => void;
	/** Удаление ручной пометки закрытия. */
	readonly onDeleteMark: (markId: number) => void;
}

/** Подпись вида закрывающей записи. */
function kindText(kind: ConstructionCardClosingEntry["kind"]): string {
	switch (kind) {
		case "delivery":
			return "delivery";
		case "otm-expiry":
			return "экспирация OTM";
		case "manual-mark":
			return "ручная пометка";
	}
}

/** Тон величины: положительная — зелёная, отрицательная — красная. */
function toneClass(value: number): string {
	if (value > 0) {
		return "text-[color:var(--fin-positive-strong)]";
	}

	return value < 0 ? "text-[color:var(--fin-negative)]" : "";
}

/** Полное пояснение предупреждения об избыточной записи (для подсказки чипа). */
function warningText(warning: ConstructionCardClosingWarning): string {
	return `Избыточная закрывающая запись: ${kindText(warning.kind)} «${warning.symbol}» на ${formatMoment(warning.closedAt)} — остаток позиции уже нулевой, запись не применена.`;
}

/** Склонение слова «избыточная» для чипа: 1 избыточная, 2 избыточные, 5 избыточных. */
function excessiveLabel(count: number): string {
	const mod10 = count % 10;
	const mod100 = count % 100;
	if (mod10 === 1 && mod100 !== 11) {
		return "избыточная";
	}

	if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) {
		return "избыточные";
	}

	return "избыточных";
}

export function ClosingEntriesTable({ entries, warnings, actionsPending, onEditMark, onDeleteMark }: ClosingEntriesTableProps) {
	const isMobile = useIsMobile();
	const [editMark, setEditMark] = React.useState<ConstructionCardClosingEntry | null>(null);
	const [editPrice, setEditPrice] = React.useState("");
	const [editQuantity, setEditQuantity] = React.useState("");
	const [editTime, setEditTime] = React.useState("");
	const [editError, setEditError] = React.useState<string | null>(null);

	const beginEdit = (entry: ConstructionCardClosingEntry) => {
		setEditMark(entry);
		setEditPrice(entry.price === null ? "" : String(entry.price));
		// Количество предзаполняется применённым количеством записи: правка
		// цены и времени по умолчанию не меняет охват закрытия.
		setEditQuantity(String(Math.abs(entry.quantity)));
		setEditTime(toLocalInputValue(entry.closedAt));
		setEditError(null);
	};

	const submitEdit = () => {
		if (editMark?.manualMarkId === null || editMark === null) {
			return;
		}

		const price = editPrice.trim() === "" ? null : Number(editPrice.replace(",", "."));
		// Пустое количество возвращает пометку к закрытию всего остатка — та же
		// семантика, что в форме создания; заданное клэмпится остатком на бэке.
		// Traceability: openspec:ui/screens#scenario-mark-form-partial-quantity
		const quantity = editQuantity.trim() === "" ? null : Number(editQuantity.replace(",", "."));
		const time = new Date(editTime);
		// Неизвестная цена допустима только при пустом вводе, а ошибочные число
		// и дата оставляют форму открытой с пояснением.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		if ((price !== null && Number.isFinite(price) === false)
			|| (quantity !== null && Number.isFinite(quantity) === false)
			|| Number.isFinite(time.getTime()) === false) {
			setEditError("Введите корректную цену, время и количество пометки.");
			return;
		}
		onEditMark({
			markId: editMark.manualMarkId,
			// Форма правки меняет цену, время и количество пометки; правка
			// сохраняет частичность закрытия вместо молчаливого расширения
			// до всего остатка.
			// Traceability: openspec:ui/screens#requirement-manual-close-mark-from-position
			mark: { symbol: editMark.symbol, markedAt: time.toISOString(), price, quantity },
		});
		setEditMark(null);
	};

	return (
		// Секция-карточка сетки закрывающих записей по мастеру design.pen:
		// головка с заголовком, счётчиком «6 колонок · N» и чипом предупреждений.
		// Traceability: change:rework-spa-shell-and-screens/design#D7
		<GridSection
			dataSlot="closing-entries-table"
			title="Закрывающие записи"
			columnCount={6}
			rowCount={entries.length}
			headExtra={warnings.length > 0 ? (
				// Избыточная закрывающая запись не применяется и не блокирует
				// чтение: предупреждение объясняет расхождение. По мастеру —
				// чип risk в головке секции, детали в подсказке.
				// Traceability: change:rework-spa-shell-and-screens/specs/ui/screens/spec#scenario-detail-grids-match-design
				<span
					className="bg-risk-soft text-risk rounded-full px-2 py-[3px] text-[10.5px]"
					role="alert"
					title={warnings.map(warningText).join("\n")}
				>
					⚠ {warnings.length} {excessiveLabel(warnings.length)}
				</span>
			) : null}
		>
			{isMobile === false && editMark !== null && (
				// Правка ручной пометки: цена, количество и время предзаполнены
				// значениями записи; пустая цена возвращает пометку к последней
				// марке, пустое количество — к закрытию всего остатка.
				// Traceability: openspec:ui/screens#requirement-manual-close-mark-from-position
				<div className="bg-card flex flex-wrap items-end gap-3 rounded-md border p-3">
					<span className="text-sm font-medium">правка ручной пометки «{editMark.symbol}»</span>
					<label className="flex flex-col gap-1 text-sm">
						цена закрытия
						<input
							className="border-input bg-background h-9 w-40 rounded-md border px-2 text-sm"
							placeholder="пусто — последняя марка"
							value={editPrice}
							onChange={(event) => setEditPrice(event.target.value)}
						/>
					</label>
					<label className="flex flex-col gap-1 text-sm">
						{/* Количество правки с семантикой формы создания: пустое поле
						    закрывает весь остаток, заданное клэмпится остатком. */}
						{/* Traceability: openspec:ui/screens#scenario-mark-form-partial-quantity */}
						количество
						<input
							data-slot="mark-quantity-input"
							aria-label="количество пометки"
							className="border-input bg-background h-9 w-28 rounded-md border px-2 text-sm"
							inputMode="decimal"
							placeholder="пусто — весь остаток"
							value={editQuantity}
							onChange={(event) => setEditQuantity(event.target.value)}
						/>
					</label>
					<label className="flex flex-col gap-1 text-sm">
						время пометки
						<input
							type="datetime-local"
							className="border-input bg-background h-9 rounded-md border px-2 text-sm"
							value={editTime}
							onChange={(event) => setEditTime(event.target.value)}
						/>
					</label>
					<Button size="sm" disabled={actionsPending || editTime === ""} onClick={submitEdit}>
						Сохранить пометку
					</Button>
					<Button size="sm" variant="outline" onClick={() => setEditMark(null)}>
						Отмена
					</Button>
					{editError !== null && <p className="text-destructive text-sm" role="alert">{editError}</p>}
				</div>
			)}

			{/* Плотность Body #2: ячейки 7/6 кегль 11, заголовки 9.5/600 на
			    заливке surface-2; значения вторичным текстом, инструмент —
			    основным. */}
			{/* Traceability: change:reconcile-frontend-with-design/design#D6 */}
			{/* Traceability: change:rework-spa-shell-and-screens/design#D7 */}
			<Table density="compact" className="text-text-secondary">
				{/* Подсказка метрики в заголовке колонки суммы закрывающей записи:
				    заголовок оборачивается один раз, триггер наследует типографику. */}
				{/* Traceability: openspec:ui/screens#requirement-column-header-tooltips */}
				<TableHeader className="bg-surface-2">
					<TableRow className="border-divider">
						{/* Колонка времени записи — дата/время: подсказка метрики
						    у неё не показывается. */}
						{/* Traceability: openspec:ui/screens#scenario-date-columns-skip-tooltip */}
						<TableHead>Время</TableHead>
						<TableHead>Тип</TableHead>
						<TableHead>Инструмент</TableHead>
						{/* Колонка «Детали» объединяет два показателя записи — количество
						    и цену закрытия: подсказка даёт раздел на каждый показатель.
						    Traceability: openspec:ui/screens#requirement-column-header-tooltips */}
						<TableHead>
							<MetricHint tooltip={METRIC_TOOLTIPS.closingEntryDetails}>Детали</MetricHint>
						</TableHead>
						<TableHead>
							<MetricHint tooltip={METRIC_TOOLTIPS.closingEntriesAmount}>Сумма</MetricHint>
						</TableHead>
						<TableHead>Действия</TableHead>
					</TableRow>
				</TableHeader>
				<TableBody>
					{entries.length === 0 ? (
						<TableRow>
							<TableCell colSpan={6} className="text-muted-foreground py-6 text-center">
								закрывающих записей нет
							</TableCell>
						</TableRow>
					) : (
						entries.map((entry) => (
							// Разделитель строк — токен divider #EBEBE5 по мастеру Body #2.
							<TableRow key={`${entry.kind}-${entry.closedAt}-${entry.symbol}-${entry.manualMarkId ?? "x"}`} className="border-divider">
								<TableCell>{formatMoment(entry.closedAt)}</TableCell>
								<TableCell>{kindText(entry.kind)}</TableCell>
								<TableCell className="font-medium text-foreground">{entry.symbol}</TableCell>
								<TableCell>
									{entry.price === null ? (
										<span className="text-muted-foreground" title="Марка инструмента неизвестна — сумма закрытия не вычислена">
											{DASH}
										</span>
									) : (
										<span>
											{formatAmount(entry.quantity)} × {formatAmount(entry.price)}
										</span>
									)}
								</TableCell>
								<TableCell>
									{entry.amountUsdt === null ? (
										<span className="text-muted-foreground">{DASH}</span>
									) : (
										<span className={cn("tabular-nums", toneClass(entry.amountUsdt))}>{formatSignedAmount(entry.amountUsdt)}</span>
									)}
								</TableCell>
								<TableCell>
									{/* Правка ручных пометок относится к desktop-операциям. */}
									{/* Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив */}
									{entry.manualMarkId !== null && isMobile === false ? (
										// Мелкие кнопки действия строки: радиус 6, паддинг [3,7], кегль 9.5.
										<div className="flex gap-1.5">
											<Button
												variant="outline"
												size="sm"
												className="h-auto rounded-[6px] px-[7px] py-[3px] text-[9.5px] text-text-secondary"
												disabled={actionsPending}
												onClick={() => onDeleteMark(entry.manualMarkId!)}
											>
												удалить
											</Button>
											<Button
												variant="outline"
												size="sm"
												className="h-auto rounded-[6px] px-[7px] py-[3px] text-[9.5px] text-text-secondary"
												disabled={actionsPending}
												onClick={() => beginEdit(entry)}
											>
												править
											</Button>
										</div>
									) : (
										<span className="text-muted-foreground">{DASH}</span>
									)}
								</TableCell>
							</TableRow>
						))
					)}
				</TableBody>
			</Table>
		</GridSection>
	);
}
