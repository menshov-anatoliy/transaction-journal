import * as React from "react";
import { useQuery } from "@tanstack/react-query";
import { Pencil } from "lucide-react";
import type { ConstructionCardTrade } from "@/lib/api/construction-card";
import { fetchMoveTargets } from "@/lib/api/construction-card";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { MarkdownEditDialog } from "@/components/markdown/markdown-edit-dialog";
import { MarkdownViewer } from "@/components/markdown/markdown-viewer";
import { DASH } from "@/lib/format/degradation";
import { formatMoment } from "@/lib/format/display-time";
import { formatAmount } from "@/lib/format/quantity";

// Таблица сделок карточки по концепции §4: атрибуты биржевой записи с
// inline-комментарием MD-рендером; действия строки — возврат во «Входящие»
// и перенос в другую конструкцию с выбором цели из активных без текущей.
// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid

/** Пропсы таблицы сделок карточки. */
export interface TradesTableProps {
	/** Идентификатор текущей конструкции — исключается из целей переноса. */
	readonly constructionId: number;
	/** Строки сделок из снимка карточки. */
	readonly trades: readonly ConstructionCardTrade[];
	/** Состояние команд строки (для блокировки кнопок). */
	readonly actionsPending: boolean;
	/** Возврат сделки во «Входящие». */
	readonly onReturn: (execId: string) => void;
	/** Перенос сделки в целевую конструкцию. */
	readonly onMove: (input: { execId: string; targetConstructionId: number }) => void;
	/** Сохранение комментария сделки. */
	readonly onSaveComment: (input: { execId: string; text: string | null }) => void;
}

export function TradesTable({ constructionId, trades, actionsPending, onReturn, onMove, onSaveComment }: TradesTableProps) {
	const [moveExecId, setMoveExecId] = React.useState<string | null>(null);
	const [moveTargetId, setMoveTargetId] = React.useState<number | null>(null);
	const [commentTarget, setCommentTarget] = React.useState<ConstructionCardTrade | null>(null);

	// Цели переноса читаются при открытии формы: активные конструкции без
	// текущей; первая кандидатура выбирается по умолчанию.
	const targetsQuery = useQuery({
		queryKey: ["construction-move-targets", constructionId] as const,
		queryFn: () => fetchMoveTargets(constructionId),
		enabled: moveExecId !== null,
	});

	const beginMove = (execId: string) => {
		setMoveExecId(execId);
		setMoveTargetId(targetsQuery.data?.[0]?.constructionId ?? null);
	};

	const submitMove = () => {
		if (moveExecId === null || moveTargetId === null) {
			return;
		}

		onMove({ execId: moveExecId, targetConstructionId: moveTargetId });
		setMoveExecId(null);
	};

	return (
		<section data-slot="trades-table" className="flex flex-col gap-2">
			{moveExecId !== null && (
				// Форма переноса: выбор целевой конструкции из активных без текущей.
				<div className="bg-card flex flex-wrap items-end gap-3 rounded-md border p-3">
					{targetsQuery.isPending && <span className="text-muted-foreground text-sm">чтение конструкций…</span>}
					{targetsQuery.isError && (
						<span className="text-destructive text-sm" role="alert">
							Список конструкций недоступен: {targetsQuery.error.message}
						</span>
					)}
					{targetsQuery.data !== undefined && targetsQuery.data.length === 0 && (
						<span className="text-sm">Нет других активных конструкций — сначала создайте целевую конструкцию.</span>
					)}
					{targetsQuery.data !== undefined && targetsQuery.data.length > 0 && (
						<>
							<label className="flex flex-col gap-1 text-sm">
								целевая конструкция
								<select
									className="border-input bg-background h-9 min-w-52 rounded-md border px-2 text-sm"
									value={moveTargetId ?? targetsQuery.data[0].constructionId}
									onChange={(event) => setMoveTargetId(Number(event.target.value))}
								>
									{targetsQuery.data.map((target) => (
										<option key={target.constructionId} value={target.constructionId}>
											{target.name}
										</option>
									))}
								</select>
							</label>
							<Button size="sm" disabled={actionsPending} onClick={submitMove}>
								Перенести
							</Button>
						</>
					)}
					<Button size="sm" variant="outline" onClick={() => setMoveExecId(null)}>
						Отмена
					</Button>
				</div>
			)}

			{/* Плотность Body #2 (yHC4d): все таблицы карточки — ячейки 7/12, шрифт 12. */}
			{/* Traceability: change:reconcile-frontend-with-design/design#D6 */}
			<Table density="compact">
				<TableHeader>
					<TableRow>
						<TableHead>Время</TableHead>
						<TableHead>execId</TableHead>
						<TableHead>Инструмент</TableHead>
						<TableHead>Направление</TableHead>
						<TableHead>Кол-во</TableHead>
						<TableHead>Цена</TableHead>
						<TableHead>Сумма</TableHead>
						<TableHead>Комиссия</TableHead>
						<TableHead>Комментарий</TableHead>
						<TableHead>Действия</TableHead>
					</TableRow>
				</TableHeader>
				<TableBody>
					{trades.length === 0 ? (
						<TableRow>
							<TableCell colSpan={10} className="text-muted-foreground py-6 text-center">
								сделок нет
							</TableCell>
						</TableRow>
					) : (
						trades.map((trade) => (
							<TableRow key={trade.execId}>
								<TableCell>{formatMoment(trade.executedAt)}</TableCell>
								<TableCell className="font-mono text-xs">{trade.execId}</TableCell>
								<TableCell className="font-medium">{trade.symbol}</TableCell>
								<TableCell>{trade.isBuy ? "покупка" : "продажа"}</TableCell>
								<TableCell className="tabular-nums">{formatAmount(trade.quantity)}</TableCell>
								<TableCell className="tabular-nums">{formatAmount(trade.price)}</TableCell>
								<TableCell className="tabular-nums">{formatAmount(trade.amountUsdt)}</TableCell>
								<TableCell className="tabular-nums">{formatAmount(trade.fee)}</TableCell>
								<TableCell>
									<div className="flex items-center gap-1">
										<div className="max-w-56 truncate" title={trade.comment ?? undefined}>
											{trade.comment === null ? (
												<span className="text-muted-foreground">{DASH}</span>
											) : (
												<MarkdownViewer text={trade.comment} />
											)}
										</div>
										<Button
											variant="ghost"
											size="icon"
											className="size-7"
											aria-label={`комментарий сделки ${trade.execId}`}
											title="Править комментарий"
											onClick={() => setCommentTarget(trade)}
										>
											<Pencil aria-hidden />
										</Button>
									</div>
								</TableCell>
								<TableCell>
									<div className="flex gap-1.5">
										<Button variant="outline" size="sm" disabled={actionsPending} onClick={() => onReturn(trade.execId)}>
											Во «Входящие»
										</Button>
										<Button variant="outline" size="sm" disabled={actionsPending} onClick={() => beginMove(trade.execId)}>
											Перенести…
										</Button>
									</div>
								</TableCell>
							</TableRow>
						))
					)}
				</TableBody>
			</Table>

			{/* Модальный split-редактор комментария сделки (§10). */}
			<MarkdownEditDialog
				open={commentTarget !== null}
				title={`Комментарий сделки ${commentTarget?.execId ?? ""}`}
				description="Комментарий следует за сделкой при переносе и возврате во «Входящие»."
				defaultText={commentTarget?.comment ?? ""}
				onSave={(text) => {
					if (commentTarget !== null) {
						onSaveComment({ execId: commentTarget.execId, text: text.trim() === "" ? null : text });
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
