import * as React from "react";
import type { ConstructionCardClosingEntry, ConstructionCardClosingWarning } from "@/lib/api/construction-card";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { DASH } from "@/lib/format/degradation";
import { formatMoment } from "@/lib/format/display-time";
import { formatAmount, formatSignedAmount } from "@/lib/format/quantity";
import { toLocalInputValue } from "./positions-table";
import { cn } from "@/lib/utils";

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
	readonly onEditMark: (input: { markId: number; mark: { symbol: string; markedAt: string; price: number | null } }) => void;
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

export function ClosingEntriesTable({ entries, warnings, actionsPending, onEditMark, onDeleteMark }: ClosingEntriesTableProps) {
	const [editMark, setEditMark] = React.useState<ConstructionCardClosingEntry | null>(null);
	const [editPrice, setEditPrice] = React.useState("");
	const [editTime, setEditTime] = React.useState("");

	const beginEdit = (entry: ConstructionCardClosingEntry) => {
		setEditMark(entry);
		setEditPrice(entry.price === null ? "" : formatAmount(entry.price));
		setEditTime(toLocalInputValue(entry.closedAt));
	};

	const submitEdit = () => {
		if (editMark?.manualMarkId === null || editMark === null) {
			return;
		}

		const price = editPrice.trim() === "" ? null : Number(editPrice.replace(",", "."));
		onEditMark({
			markId: editMark.manualMarkId,
			mark: { symbol: editMark.symbol, markedAt: new Date(editTime).toISOString(), price: Number.isNaN(price) ? null : price },
		});
		setEditMark(null);
	};

	return (
		<section data-slot="closing-entries-table" className="flex flex-col gap-2">
			{warnings.map((warning) => (
				// Избыточная закрывающая запись не применяется и не блокирует
				// чтение: предупреждение объясняет расхождение.
				<p key={`${warning.symbol}-${warning.closedAt}`} className="text-destructive text-sm" role="alert">
					Избыточная закрывающая запись: {kindText(warning.kind)} «{warning.symbol}» на{" "}
					{formatMoment(warning.closedAt)} — остаток позиции уже нулевой, запись не применена.
				</p>
			))}

			{editMark !== null && (
				// Правка ручной пометки: цена и время предзаполнены эффективными
				// значениями записи; пустая цена возвращает пометку к последней марке.
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
				</div>
			)}

			<Table>
				<TableHeader>
					<TableRow>
						<TableHead>Время</TableHead>
						<TableHead>Тип</TableHead>
						<TableHead>Инструмент</TableHead>
						<TableHead>Детали</TableHead>
						<TableHead>Сумма</TableHead>
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
							<TableRow key={`${entry.kind}-${entry.closedAt}-${entry.symbol}-${entry.manualMarkId ?? "x"}`}>
								<TableCell>{formatMoment(entry.closedAt)}</TableCell>
								<TableCell>{kindText(entry.kind)}</TableCell>
								<TableCell className="font-medium">{entry.symbol}</TableCell>
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
									{entry.manualMarkId !== null ? (
										<div className="flex gap-1.5">
											<Button variant="outline" size="sm" disabled={actionsPending} onClick={() => onDeleteMark(entry.manualMarkId!)}>
												удалить
											</Button>
											<Button variant="outline" size="sm" disabled={actionsPending} onClick={() => beginEdit(entry)}>
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
		</section>
	);
}
