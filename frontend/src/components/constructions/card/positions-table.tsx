import * as React from "react";
import { Pencil } from "lucide-react";
import type { ConstructionCardPosition } from "@/lib/api/construction-card";
import { fetchLastInstrumentMark } from "@/lib/api/construction-card";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { MarkdownEditDialog } from "@/components/markdown/markdown-edit-dialog";
import { MarkdownViewer } from "@/components/markdown/markdown-viewer";
import { DASH } from "@/lib/format/degradation";
import { formatMoment } from "@/lib/format/display-time";
import { formatAmount, formatSignedAmount, formatSignedPercent } from "@/lib/format/quantity";
import { cn } from "@/lib/utils";

// Таблица позиций карточки по концепции §4: картина позиции её записями —
// вход, выход, стоимость, раздельные части результата, общий P&L, комиссии,
// времена, статус и inline-комментарий MD-рендером; единственное действие
// открытой строки — «закрыть пометкой» с пикером даты-времени и дефолтом
// последней марки.
// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid

/** Пропсы таблицы позиций карточки. */
export interface PositionsTableProps {
	/** Идентификатор конструкции — владелицы позиций. */
	readonly constructionId: number;
	/** Строки позиций из снимка карточки. */
	readonly positions: readonly ConstructionCardPosition[];
	/** Состояние команды добавления пометки (для блокировки кнопок). */
	readonly markPending: boolean;
	/** Постановка ручной пометки закрытия позиции. */
	readonly onAddCloseMark: (mark: { symbol: string; markedAt: string; price: number | null }) => void;
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

export function PositionsTable({ constructionId, positions, markPending, onAddCloseMark, onSaveComment }: PositionsTableProps) {
	const [markSymbol, setMarkSymbol] = React.useState<string | null>(null);
	const [markPrice, setMarkPrice] = React.useState("");
	const [markTime, setMarkTime] = React.useState("");
	const [commentTarget, setCommentTarget] = React.useState<ConstructionCardPosition | null>(null);

	// Открытие формы пометки: цена предзаполняется последней известной маркой
	// инструмента, время — текущим моментом; неудача марки оставляет поле
	// пустым — цену задаст домен при чтении.
	const beginMark = async (symbol: string) => {
		setMarkSymbol(symbol);
		setMarkTime(toLocalInputValue(new Date().toISOString()));
		setMarkPrice("");
		try {
			const lastMark = await fetchLastInstrumentMark(constructionId, symbol);
			setMarkPrice(lastMark === null ? "" : formatAmount(lastMark));
		} catch {
			// Марка недоступна — форма остаётся с пустой ценой.
		}
	};

	const submitMark = () => {
		if (markSymbol === null) {
			return;
		}

		const price = markPrice.trim() === "" ? null : Number(markPrice.replace(",", "."));
		onAddCloseMark({ symbol: markSymbol, markedAt: new Date(markTime).toISOString(), price: Number.isNaN(price) ? null : price });
		setMarkSymbol(null);
	};

	return (
		<section data-slot="positions-table" className="flex flex-col gap-2">
			{markSymbol !== null && (
				// Форма ручной пометки закрытия: цена — последняя марка или пусто
				// (марка подставится при чтении), время — пикер даты-времени.
				<div className="bg-card flex flex-wrap items-end gap-3 rounded-md border p-3">
					<span className="text-sm font-medium">закрыть позицию «{markSymbol}» пометкой</span>
					<label className="flex flex-col gap-1 text-sm">
						цена закрытия
						<input
							className="border-input bg-background h-9 w-40 rounded-md border px-2 text-sm"
							placeholder="пусто — последняя марка"
							value={markPrice}
							onChange={(event) => setMarkPrice(event.target.value)}
						/>
					</label>
					<label className="flex flex-col gap-1 text-sm">
						время пометки
						<input
							type="datetime-local"
							className="border-input bg-background h-9 rounded-md border px-2 text-sm"
							value={markTime}
							onChange={(event) => setMarkTime(event.target.value)}
						/>
					</label>
					<Button size="sm" disabled={markPending || markTime === ""} onClick={submitMark}>
						Поставить пометку
					</Button>
					<Button size="sm" variant="outline" onClick={() => setMarkSymbol(null)}>
						Отмена
					</Button>
				</div>
			)}

			<Table>
				<TableHeader>
					<TableRow>
						<TableHead>Инструмент</TableHead>
						<TableHead>Остаток</TableHead>
						<TableHead>Сред. цена входа</TableHead>
						<TableHead>Сред. цена закрытия</TableHead>
						<TableHead>Стоимость</TableHead>
						<TableHead>Изм. цены, %</TableHead>
						<TableHead>Реализ. P&L</TableHead>
						<TableHead>Нереализ. P&L</TableHead>
						<TableHead>Общий P&L</TableHead>
						<TableHead>Всего комиссий</TableHead>
						<TableHead>Время открытия</TableHead>
						<TableHead>Время закрытия</TableHead>
						<TableHead>Статус</TableHead>
						<TableHead>Комментарий</TableHead>
						<TableHead>Действия</TableHead>
					</TableRow>
				</TableHeader>
				<TableBody>
					{positions.length === 0 ? (
						<TableRow>
							<TableCell colSpan={15} className="text-muted-foreground py-6 text-center">
								позиций нет
							</TableCell>
						</TableRow>
					) : (
						positions.map((position) => (
							<TableRow key={position.symbol}>
								<TableCell className="font-medium">{position.symbol}</TableCell>
								<TableCell className="tabular-nums">{formatAmount(position.residual)}</TableCell>
								<TableCell className="tabular-nums">{position.averageEntryPrice === null ? DASH : formatAmount(position.averageEntryPrice)}</TableCell>
								<TableCell className="tabular-nums">{position.averageClosePrice === null ? DASH : formatAmount(position.averageClosePrice)}</TableCell>
								<TableCell>
									{position.isOpen === false ? (
										<span className="text-muted-foreground">{DASH}</span>
									) : position.markValue === null ? (
										<span className="text-muted-foreground" title="Провайдер котировок недоступен — стоимость не оценена">
											сбой котировок
										</span>
									) : (
										<span className={cn("tabular-nums", toneClass(position.markValue))}>{formatSignedAmount(position.markValue)}</span>
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
										<span className="text-muted-foreground" title="Провайдер котировок недоступен — общий P&L не оценён">
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
									<span className={position.isOpen ? "text-primary" : "text-muted-foreground"}>
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
										<Button
											variant="ghost"
											size="icon"
											className="size-7"
											aria-label={`комментарий позиции ${position.symbol}`}
											title="Править комментарий"
											onClick={() => setCommentTarget(position)}
										>
											<Pencil aria-hidden />
										</Button>
									</div>
								</TableCell>
								<TableCell>
									{position.isOpen ? (
										<Button variant="outline" size="sm" disabled={markPending} onClick={() => void beginMark(position.symbol)}>
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
		</section>
	);
}
