import * as React from "react";
import type { AdjustmentInput, ConstructionCardAdjustment } from "@/lib/api/construction-card";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
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
		<section data-slot="adjustments-table" className="flex flex-col gap-2">
			{/* Изменение корректировок — desktop-операция, суммы доступны на мобильном. */}
			{/* Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив */}
			{isMobile ? null : addForm === null ? (
				<Button variant="outline" size="sm" onClick={() => setAddForm(emptyForm())}>
					Добавить корректировку…
				</Button>
			) : (
				// Форма добавления: дата-пикер с дефолтом «сегодня», источник,
				// знаковая сумма и необязательное описание.
				<div className="bg-card flex flex-wrap items-end gap-3 rounded-md border p-3">
					<label className="flex flex-col gap-1 text-sm">
						дата
						<input
							type="date"
							className="border-input bg-background h-9 rounded-md border px-2 text-sm"
							value={addForm.date}
							onChange={(event) => setAddForm({ ...addForm, date: event.target.value })}
						/>
					</label>
					<label className="flex flex-col gap-1 text-sm">
						источник
						<select
							className="border-input bg-background h-9 rounded-md border px-2 text-sm"
							value={addForm.source}
							onChange={(event) => setAddForm({ ...addForm, source: event.target.value as ConstructionCardAdjustment["source"] })}
						>
							<option value="manual">ручная</option>
							<option value="robot">робот</option>
						</select>
					</label>
					<label className="flex flex-col gap-1 text-sm">
						сумма, USDT
						<input
							className="border-input bg-background h-9 w-32 rounded-md border px-2 text-sm"
							placeholder="+87.4 / -12"
							value={addForm.amount}
							onChange={(event) => setAddForm({ ...addForm, amount: event.target.value })}
						/>
					</label>
					<label className="flex flex-col gap-1 text-sm">
						описание
						<input
							className="border-input bg-background h-9 w-64 rounded-md border px-2 text-sm"
							placeholder="PnL робота grid-ETH за сентябрь"
							value={addForm.description}
							onChange={(event) => setAddForm({ ...addForm, description: event.target.value })}
						/>
					</label>
					<Button size="sm" disabled={actionsPending} onClick={submitAdd}>
						Добавить корректировку
					</Button>
					<Button size="sm" variant="outline" onClick={() => setAddForm(null)}>
						Отмена
					</Button>
					{invalid && <span className="text-destructive text-sm" role="alert">Введите корректную дату и сумму со знаком, например +87.4 или -12</span>}
				</div>
			)}

			{/* Плотность Body #2 (yHC4d): все таблицы карточки — ячейки 7/12, шрифт 12. */}
			{/* Traceability: change:reconcile-frontend-with-design/design#D6 */}
			<Table density="compact">
				<TableHeader>
					<TableRow>
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
								// Inline-правка корректировки в строке таблицы.
								<TableRow key={adjustment.adjustmentId}>
									<TableCell>
										<input
											type="date"
											className="border-input bg-background h-8 rounded-md border px-2 text-sm"
											value={editForm.date}
											onChange={(event) => setEditForm({ ...editForm, date: event.target.value })}
										/>
									</TableCell>
									<TableCell>
										<input
											className="border-input bg-background h-8 w-56 rounded-md border px-2 text-sm"
											placeholder="описание"
											value={editForm.description}
											onChange={(event) => setEditForm({ ...editForm, description: event.target.value })}
										/>
									</TableCell>
									<TableCell>
										<select
											className="border-input bg-background h-8 rounded-md border px-2 text-sm"
											value={editForm.source}
											onChange={(event) => setEditForm({ ...editForm, source: event.target.value as ConstructionCardAdjustment["source"] })}
										>
											<option value="manual">ручная</option>
											<option value="robot">робот</option>
										</select>
									</TableCell>
									<TableCell>
										<input
											className="border-input bg-background h-8 w-28 rounded-md border px-2 text-sm"
											placeholder="+87.4 / -12"
											value={editForm.amount}
											onChange={(event) => setEditForm({ ...editForm, amount: event.target.value })}
										/>
									</TableCell>
									<TableCell>
										<div className="flex gap-1.5">
											<Button size="sm" disabled={actionsPending} onClick={submitEdit}>
												Сохранить
											</Button>
											<Button
												size="sm"
												variant="outline"
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
								<TableRow key={adjustment.adjustmentId}>
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
											<Button
												variant="outline"
												size="sm"
												disabled={actionsPending}
												onClick={() => {
													setEditTarget(adjustment);
													setEditForm(formFromAdjustment(adjustment));
													setInvalid(false);
												}}
											>
												править
											</Button>
											<Button variant="outline" size="sm" disabled={actionsPending} onClick={() => onDelete(adjustment.adjustmentId)}>
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
		</section>
	);
}
