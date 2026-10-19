import * as React from "react";
import type { AdjustmentInput, ConstructionCardAdjustment } from "@/lib/api/construction-card";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { Plus, X } from "lucide-react";
import { GridSection } from "./grid-section";
import { DASH } from "@/lib/format/degradation";
import { formatDay } from "@/lib/format/display-time";
import { formatSignedAmount } from "@/lib/format/quantity";
import { cn } from "@/lib/utils";
import { useIsMobile } from "@/lib/use-mobile";

// Таблица внешних корректировок PnL карточки по концепции §4: форма
// добавления с пикером даты (дефолт — сегодня), источником «робот»/«ручная»,
// знаковой суммой и описанием; строки правятся и удаляются по месту таблицы.
// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid

/** Пропсы таблицы корректировок. */
export interface AdjustmentsTableProps {
	/** Строки корректировок из снимка карточки. */
	readonly adjustments: readonly ConstructionCardAdjustment[];
	/** Состояние команд (для блокировки кнопок). */
	readonly actionsPending: boolean;
	/** Добавление корректировки. */
	readonly onAdd: (adjustment: AdjustmentInput) => void;
	/** Правка корректировки. */
	readonly onEdit: (input: { adjustmentId: number; adjustment: AdjustmentInput }) => void;
	/** Удаление корректировки. */
	readonly onDelete: (adjustmentId: number) => void;
}

/** Подпись источника корректировки. */
function sourceText(source: ConstructionCardAdjustment["source"]): string {
	return source === "robot" ? "робот" : "ручная";
}

// Сегментированный переключатель источника по образцу «Стороны» формы
// ручной сделки: активный сегмент $accentSoft/$accentStrong, неактивный —
// кайма $border и $textSecondary; кегль и паддинги задаёт вызов.
function SourceSegmented(props: {
	/** Доступное имя группы для скринридеров. */
	readonly ariaLabel: string;
	/** Текущий источник. */
	readonly value: ConstructionCardAdjustment["source"];
	/** Блокировка на время команды. */
	readonly disabled: boolean;
	/** Размер сегментов: крупные в форме, компактные в строке таблицы. */
	readonly itemClassName: string;
	/** Смена источника. */
	readonly onChange: (source: ConstructionCardAdjustment["source"]) => void;
}) {
	return (
		<div className="flex gap-1.5" role="radiogroup" aria-label={props.ariaLabel}>
			{(["manual", "robot"] as const).map((source) => {
				const active = props.value === source;
				return (
					<button
						key={source}
						type="button"
						role="radio"
						aria-checked={active}
						disabled={props.disabled}
						onClick={() => props.onChange(source)}
						className={cn(
							"rounded-[8px] font-medium transition-colors",
							active ? "bg-accent-soft text-accent-strong" : "border text-text-secondary",
							props.itemClassName,
						)}
					>
						{source === "manual" ? "ручная" : "робот"}
					</button>
				);
			})}
		</div>
	);
}

/** Тон суммы: положительная — зелёная, отрицательная — красная. */
function toneClass(value: number): string {
	if (value > 0) {
		return "text-[color:var(--fin-positive-strong)]";
	}

	return value < 0 ? "text-[color:var(--fin-negative)]" : "";
}

/** Локальный день для пикера даты. */
function toLocalDateInput(iso: string): string {
	return toDayValue(new Date(iso));
}

function toDayValue(date: Date): string {
	const pad = (value: number) => String(value).padStart(2, "0");
	return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

/** Форма корректировки: локальное состояние полей добавления или правки. */
interface AdjustmentFormState {
	date: string;
	source: ConstructionCardAdjustment["source"];
	amount: string;
	description: string;
}

function emptyForm(): AdjustmentFormState {
	return { date: toDayValue(new Date()), source: "manual", amount: "", description: "" };
}

function formFromAdjustment(adjustment: ConstructionCardAdjustment): AdjustmentFormState {
	return {
		date: toLocalDateInput(adjustment.date),
		source: adjustment.source,
		amount: String(adjustment.amountUsdt),
		description: adjustment.description ?? "",
	};
}

function formToInput(form: AdjustmentFormState): AdjustmentInput | null {
	const amount = Number(form.amount.replace(",", "."));
	const date = new Date(`${form.date}T00:00:00`);
	// Пустая дата и нечисловая сумма не превращаются в валидную корректировку;
	// точность исходной суммы сохраняется при открытии формы правки.
	// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
	if (Number.isFinite(amount) === false || form.amount.trim() === "" || Number.isFinite(date.getTime()) === false) {
		return null;
	}

	return {
		date: date.toISOString(),
		source: form.source,
		amountUsdt: amount,
		description: form.description.trim() === "" ? null : form.description,
	};
}

export function AdjustmentsTable({ adjustments, actionsPending, onAdd, onEdit, onDelete }: AdjustmentsTableProps) {
	const isMobile = useIsMobile();
	const [addForm, setAddForm] = React.useState<AdjustmentFormState | null>(null);
	const [editTarget, setEditTarget] = React.useState<ConstructionCardAdjustment | null>(null);
	const [editForm, setEditForm] = React.useState<AdjustmentFormState | null>(null);
	const [invalid, setInvalid] = React.useState(false);

	const submitAdd = () => {
		if (addForm === null) {
			return;
		}

		const input = formToInput(addForm);
		if (input === null) {
			setInvalid(true);
			return;
		}

		setInvalid(false);
		onAdd(input);
		setAddForm(null);
	};

	const submitEdit = () => {
		if (editTarget === null || editForm === null) {
			return;
		}

		const input = formToInput(editForm);
		if (input === null) {
			setInvalid(true);
			return;
		}

		setInvalid(false);
		onEdit({ adjustmentId: editTarget.adjustmentId, adjustment: input });
		setEditTarget(null);
		setEditForm(null);
	};

	return (
		// Секция-карточка сетки корректировок по мастеру design.pen: головка
		// с заголовком и счётчиком «5 колонок · N»; кнопка добавления живёт
		// в головке секции — в стиле кнопки «Ручная сделка», форма остаётся
		// транзиентным контентом над таблицей.
		// Traceability: change:rework-spa-shell-and-screens/design#D7
		<GridSection
			dataSlot="adjustments-table"
			title="Корректировки PnL"
			columnCount={5}
			rowCount={adjustments.length}
			headExtra={isMobile === false && addForm === null ? (
				// Кнопка добавления стилистически повторяет кнопку «Ручная сделка»
				// головки секции «Сделки»: outline, иконка плюс, правый край головки.
				<Button
					variant="outline"
					size="sm"
					className="ml-auto h-auto gap-1.5 px-2.5 py-[5px] text-[11.5px]"
					onClick={() => setAddForm(emptyForm())}
				>
					<Plus aria-hidden className="size-3" />
					Добавить корректировку…
				</Button>
			) : null}
		>
			{/* Изменение корректировок — desktop-операция, суммы доступны на мобильном. */}
			{/* Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив */}
			{isMobile === false && addForm !== null ? (
				// Форма добавления повторяет мастер формы ручной сделки: карточка
				// с шапкой «Новая корректировка» и крестиком, подписи полей
				// 11px $textMuted над примитивом Input, сегмент источника по
				// образцу «Стороны», футер Ghost-«Отмена» + Primary-действие;
				// отступы рамки симметричны со всех сторон секции.
				<div className="p-2.5">
					<div className="flex flex-col gap-2.5 rounded-lg border bg-card p-4">
						<div className="flex items-center gap-2">
							<h4 className="text-[14px] font-semibold text-foreground">Новая корректировка</h4>
							<span className="h-px flex-1" aria-hidden="true" />
							<Button
								variant="ghost"
								size="icon"
								className="size-6"
								aria-label="Закрыть форму корректировки"
								onClick={() => setAddForm(null)}
							>
								<X aria-hidden className="size-3.5 text-text-muted" />
							</Button>
						</div>

						<div className="flex gap-2.5">
							<label className="flex min-w-0 flex-1 flex-col gap-1">
								<span className="text-[11px] font-medium text-text-muted">дата</span>
								<Input
									type="date"
									value={addForm.date}
									onChange={(event) => setAddForm({ ...addForm, date: event.target.value })}
								/>
							</label>
							<div className="flex min-w-0 flex-1 flex-col gap-1">
								<span className="text-[11px] font-medium text-text-muted">источник</span>
								<SourceSegmented
									ariaLabel="Источник корректировки"
									value={addForm.source}
									disabled={actionsPending}
									itemClassName="px-3.5 py-[6px] text-[12.5px]"
									onChange={(source) => setAddForm({ ...addForm, source })}
								/>
							</div>
						</div>

						<div className="flex gap-2.5">
							<label className="flex min-w-0 flex-1 flex-col gap-1">
								<span className="text-[11px] font-medium text-text-muted">сумма, USDT</span>
								<Input
									inputMode="decimal"
									placeholder="+87.4 / -12"
									value={addForm.amount}
									onChange={(event) => setAddForm({ ...addForm, amount: event.target.value })}
								/>
							</label>
							<label className="flex min-w-0 flex-1 flex-col gap-1">
								<span className="text-[11px] font-medium text-text-muted">описание</span>
								<Input
									placeholder="PnL робота grid-ETH за сентябрь"
									value={addForm.description}
									onChange={(event) => setAddForm({ ...addForm, description: event.target.value })}
								/>
							</label>
						</div>

						<div className="flex items-center gap-2 pt-0.5">
							<span className="h-px flex-1" aria-hidden="true" />
							<Button variant="ghost" size="sm" onClick={() => setAddForm(null)}>
								Отмена
							</Button>
							<Button size="sm" disabled={actionsPending} onClick={submitAdd}>
								Добавить корректировку
							</Button>
						</div>
						{invalid && <p className="text-destructive text-sm" role="alert">Введите корректную дату и сумму со знаком, например +87.4 или -12</p>}
						</div>
				</div>
			) : null}

			{/* Плотность Body #2: ячейки 7/6 кегль 11, заголовки 9.5/600 на
			    заливке surface-2; значения вторичным текстом, описание — тоже. */}
			{/* Traceability: change:reconcile-frontend-with-design/design#D6 */}
			{/* Traceability: change:rework-spa-shell-and-screens/design#D7 */}
			<Table density="compact" className="text-text-secondary">
				<TableHeader className="bg-surface-2">
					<TableRow className="border-divider">
						<TableHead>Дата</TableHead>
						<TableHead>Описание</TableHead>
						<TableHead>Источник</TableHead>
						<TableHead>Сумма</TableHead>
						<TableHead>Действия</TableHead>
					</TableRow>
				</TableHeader>
				<TableBody>
					{adjustments.length === 0 ? (
						<TableRow>
							<TableCell colSpan={5} className="text-muted-foreground py-6 text-center">
								корректировок нет
							</TableCell>
						</TableRow>
					) : (
						adjustments.map((adjustment) =>
							isMobile === false && editTarget?.adjustmentId === adjustment.adjustmentId && editForm !== null ? (
								// Inline-правка корректировки в строке таблицы: те же
								// примитивы, что и в форме добавления, в компактном
								// размере; футер — Primary-«Сохранить» и Ghost-«Отмена».
								<TableRow key={adjustment.adjustmentId}>
									<TableCell>
										<Input
											type="date"
											value={editForm.date}
											onChange={(event) => setEditForm({ ...editForm, date: event.target.value })}
										/>
									</TableCell>
									<TableCell>
										<Input
											className="w-56"
											placeholder="описание"
											value={editForm.description}
											onChange={(event) => setEditForm({ ...editForm, description: event.target.value })}
										/>
									</TableCell>
									<TableCell>
										<SourceSegmented
											ariaLabel="Источник правимой корректировки"
											value={editForm.source}
											disabled={actionsPending}
											itemClassName="px-2.5 py-[3px] text-[11px]"
											onChange={(source) => setEditForm({ ...editForm, source })}
										/>
									</TableCell>
									<TableCell>
										<Input
											className="w-28"
											inputMode="decimal"
											placeholder="+87.4 / -12"
											value={editForm.amount}
											onChange={(event) => setEditForm({ ...editForm, amount: event.target.value })}
										/>
									</TableCell>
									<TableCell>
										<div className="flex items-center gap-1.5">
											<Button size="sm" disabled={actionsPending} onClick={submitEdit}>
												Сохранить
											</Button>
											<Button
												variant="ghost"
												size="sm"
												onClick={() => {
													setEditTarget(null);
													setEditForm(null);
												}}
											>
												Отмена
											</Button>
										</div>
										{invalid && <p className="text-destructive text-sm" role="alert">Введите корректную дату и сумму.</p>}
									</TableCell>
								</TableRow>
							) : (
								// Разделитель строк — токен divider #EBEBE5 по мастеру Body #2.
								<TableRow key={adjustment.adjustmentId} className="border-divider">
									<TableCell>{formatDay(adjustment.date)}</TableCell>
									<TableCell>{adjustment.description ?? <span className="text-muted-foreground">{DASH}</span>}</TableCell>
									<TableCell>{sourceText(adjustment.source)}</TableCell>
									<TableCell>
										<span className={cn("tabular-nums", toneClass(adjustment.amountUsdt))}>
											{formatSignedAmount(adjustment.amountUsdt)}
										</span>
									</TableCell>
									<TableCell>
										{isMobile === false && <div className="flex gap-1.5">
											{/* Мелкие кнопки действия строки: радиус 6, паддинг [3,7], кегль 9.5. */}
											<Button
												variant="outline"
												size="sm"
												className="h-auto rounded-[6px] px-[7px] py-[3px] text-[9.5px] text-text-secondary"
												disabled={actionsPending}
												onClick={() => {
													setEditTarget(adjustment);
													setEditForm(formFromAdjustment(adjustment));
													setInvalid(false);
												}}
											>
												править
											</Button>
											<Button
												variant="outline"
												size="sm"
												className="h-auto rounded-[6px] px-[7px] py-[3px] text-[9.5px] text-text-secondary"
												disabled={actionsPending}
												onClick={() => onDelete(adjustment.adjustmentId)}
											>
												удалить
											</Button>
										</div>}
									</TableCell>
								</TableRow>
							),
						)
					)}
				</TableBody>
			</Table>
		</GridSection>
	);
}
