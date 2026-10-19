import * as React from "react";
import { useQuery } from "@tanstack/react-query";
import { Pencil, Plus } from "lucide-react";
import type { ConstructionCardTrade } from "@/lib/api/construction-card";
import type { ManualTradeInput } from "@/lib/api/manual-trades";
import { fetchMoveTargets } from "@/lib/api/construction-card";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { ManualTradeForm } from "@/components/trades/manual-trade-form";
import { TradeSourceBadge } from "@/components/trades/trade-source-badge";
import { GridSection } from "./grid-section";
import { MarkdownEditDialog } from "@/components/markdown/markdown-edit-dialog";
import { MarkdownViewer } from "@/components/markdown/markdown-viewer";
import { DASH } from "@/lib/format/degradation";
import { formatMoment } from "@/lib/format/display-time";
import { formatAmount } from "@/lib/format/quantity";
import { useIsMobile } from "@/lib/use-mobile";

// Таблица сделок карточки по концепции §4: атрибуты записи с inline-комментарием
// MD-рендером; действия строки — возврат во «Входящие» и перенос в другую
// конструкцию с выбором цели из активных без текущей. Ручные записи различаются
// бейджем источника и дополнительно адресуются правкой и удалением с
// подтверждением; из головки секции создаётся ручная сделка, попадающая
// сразу в показанную конструкцию.
// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
// Traceability: openspec:ui/screens#requirement-manual-trade-creation-ui
// Traceability: openspec:ui/screens#scenario-detail-edit-manual-trade

/** Пропсы таблицы сделок карточки. */
export interface TradesTableProps {
	/** Идентификатор текущей конструкции — исключается из целей переноса. */
	readonly constructionId: number;
	/** Имя конструкции для чипа цели формы ручной сделки. */
	readonly constructionName: string;
	/** Строки сделок из снимка карточки. */
	readonly trades: readonly ConstructionCardTrade[];
	/** Состояние команд строки (для блокировки кнопок). */
	readonly actionsPending: boolean;
	/** Состояние команд ручной сделки (создание/правка/удаление). */
	readonly tradeCommandsPending: boolean;
	/** Возврат сделки во «Входящие». */
	readonly onReturn: (execId: string) => void;
	/** Перенос сделки в целевую конструкцию. */
	readonly onMove: (input: { execId: string; targetConstructionId: number }) => void;
	/** Сохранение комментария биржевой сделки. */
	readonly onSaveComment: (input: { execId: string; text: string | null }) => void;
	/** Внесение ручной сделки в показанную конструкцию. */
	readonly onCreateManualTrade: (input: ManualTradeInput) => void;
	/** Правка атрибутов ручной сделки по ключу строки сырья. */
	readonly onEditManualTrade: (input: { id: number; trade: ManualTradeInput }) => void;
	/** Удаление ручной сделки по ключу строки сырья. */
	readonly onDeleteManualTrade: (id: number) => void;
	/** Человекочитаемая причина отказа команд ручной сделки. */
	readonly manualTradeError: string | null;
}

/** Переводит ISO-момент в значение поля datetime-local в зоне браузера. */
function toLocalInputValue(iso: string): string {
	const date = new Date(iso);
	const pad = (value: number) => String(value).padStart(2, "0");
	return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/**
 * Единый ключ сделки для команд принадлежности: execId биржевой записи либо
 * «manual:{id}» строки ручного сырья — возврат и перенос обслуживают оба
 * источника одними командами.
 * Traceability: openspec:ui/screens#requirement-trade-actions-in-detail
 */
function tradeKey(trade: ConstructionCardTrade): string {
	return trade.execId ?? `manual:${trade.manualTradeId}`;
}

export function TradesTable({
	constructionId,
	constructionName,
	trades,
	actionsPending,
	tradeCommandsPending,
	onReturn,
	onMove,
	onSaveComment,
	onCreateManualTrade,
	onEditManualTrade,
	onDeleteManualTrade,
	manualTradeError,
}: TradesTableProps) {
	const isMobile = useIsMobile();
	const [moveExecId, setMoveExecId] = React.useState<string | null>(null);
	const [moveTargetId, setMoveTargetId] = React.useState<number | null>(null);
	const [commentTarget, setCommentTarget] = React.useState<ConstructionCardTrade | null>(null);
	const [createFormOpen, setCreateFormOpen] = React.useState(false);
	const [editingTrade, setEditingTrade] = React.useState<ConstructionCardTrade | null>(null);
	const [deleteTarget, setDeleteTarget] = React.useState<ConstructionCardTrade | null>(null);

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
		const targetId = moveTargetId ?? targetsQuery.data?.[0]?.constructionId;
		if (moveExecId === null || targetId === undefined) {
			return;
		}

		// Первая загруженная цель доступна без лишней смены значения select.
		// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
		onMove({ execId: moveExecId, targetConstructionId: targetId });
		setMoveExecId(null);
	};

	return (
		// Секция-карточка сетки сделок по мастеру design.pen: головка с
		// заголовком, счётчиком «10 колонок · N» и действием создания ручной
		// сделки (мастер «Кнопка:Ручная сделка» головки «Сделки»).
		// Traceability: change:rework-spa-shell-and-screens/design#D7
		// Traceability: openspec:ui/screens#scenario-card-create-manual-trade
		<GridSection
			dataSlot="trades-table"
			title="Сделки"
			columnCount={10}
			rowCount={trades.length}
			headExtra={
				isMobile === false && (
					<Button
						variant="outline"
						size="sm"
						className="ml-auto h-auto gap-1.5 px-2.5 py-[5px] text-[11.5px]"
						disabled={tradeCommandsPending}
						onClick={() => setCreateFormOpen(true)}
					>
						<Plus aria-hidden className="size-3" />
						Ручная сделка
					</Button>
				)
			}
		>
			{isMobile === false && createFormOpen && (
				// Форма создания ручной сделки: чип цели показывает конструкцию,
				// в которую сделка попадёт сразу после сохранения.
				// Traceability: openspec:ui/screens#scenario-card-create-manual-trade
				<div className="p-2.5 pb-0">
					<ManualTradeForm
						target={{ constructionName }}
						submitLabel="Создать"
						pending={tradeCommandsPending}
						error={manualTradeError}
						onSubmit={(input) => onCreateManualTrade(input)}
						onCancel={() => setCreateFormOpen(false)}
					/>
				</div>
			)}

			{isMobile === false && editingTrade !== null && editingTrade.manualTradeId !== null && (
				// Форма правки ручной сделки: значения предзаполнены атрибутами
				// записи, привязка правкой не меняется.
				// Traceability: openspec:ui/screens#scenario-detail-edit-manual-trade
				<div className="p-2.5 pb-0">
					<ManualTradeForm
						target={{ constructionName }}
						submitLabel="Сохранить"
						pending={tradeCommandsPending}
						error={manualTradeError}
						initialValues={{
							executedAt: toLocalInputValue(editingTrade.executedAt),
							symbol: editingTrade.symbol,
							side: editingTrade.isBuy ? "Buy" : "Sell",
							quantity: String(editingTrade.quantity),
							price: String(editingTrade.price),
							feeUsdt: String(editingTrade.fee),
						}}
						onSubmit={(input) => onEditManualTrade({ id: editingTrade.manualTradeId!, trade: input })}
						onCancel={() => setEditingTrade(null)}
					/>
				</div>
			)}

			{isMobile === false && moveExecId !== null && (
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

			{/* Подтверждение удаления ручной сделки: действие необратимо, вопрос
			    показывается диалогом по действующему паттерну опасных действий. */}
			{/* Traceability: openspec:ui/screens#requirement-trade-actions-in-detail */}
			<Dialog
				open={deleteTarget !== null}
				onOpenChange={(value) => {
					if (value === false && tradeCommandsPending === false) {
						setDeleteTarget(null);
					}
				}}
			>
				<DialogContent showCloseButton={tradeCommandsPending === false}>
					<DialogTitle>Удалить ручную сделку?</DialogTitle>
					<DialogDescription>
						Ручная сделка «{deleteTarget?.symbol ?? ""}» будет удалена из журнала, позиции и метрики пересчитаются при
						очередном чтении. Действие нельзя отменить.
					</DialogDescription>
					<div className="flex gap-2">
						<Button
							variant="destructive"
							size="sm"
							disabled={tradeCommandsPending}
							onClick={() => {
								if (deleteTarget?.manualTradeId != null) {
									onDeleteManualTrade(deleteTarget.manualTradeId);
								}

								setDeleteTarget(null);
							}}
						>
							Подтвердить удаление
						</Button>
						<Button variant="outline" size="sm" disabled={tradeCommandsPending} onClick={() => setDeleteTarget(null)}>
							Отмена
						</Button>
					</div>
					{manualTradeError !== null && (
						<p className="text-destructive text-sm" role="alert">
							Сделка не удалена: {manualTradeError}
						</p>
					)}
				</DialogContent>
			</Dialog>

			{/* Плотность Body #2: ячейки 7/6 кегль 11, заголовки 9.5/600 на
			    заливке surface-2; значения вторичным текстом, инструмент —
			    основным. */}
			{/* Traceability: change:reconcile-frontend-with-design/design#D6 */}
			{/* Traceability: change:rework-spa-shell-and-screens/design#D7 */}
			<Table density="compact" className="text-text-secondary">
				<TableHeader className="bg-surface-2">
					<TableRow className="border-divider">
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
							// Разделитель строк — токен divider #EBEBE5 по мастеру Body #2.
							<TableRow key={trade.execId ?? `manual:${trade.manualTradeId}`} className="border-divider">
								<TableCell>{formatMoment(trade.executedAt)}</TableCell>
								{/* Идентификаторы сделок используют основной шрифт дизайна, Inter 11;
								    у ручной записи execId отсутствует — показывается прочерк. */}
								{/* Traceability: openspec:ui/design-system#requirement-typography-matches-design-system */}
								<TableCell className="font-sans text-[11px]">{trade.execId ?? DASH}</TableCell>
								<TableCell className="font-medium text-foreground">
									{/* Инстр-ряд мастера: инструмент с бейджем источника записи. */}
									{/* Traceability: openspec:ui/screens#scenario-source-badge-shown */}
									<span className="flex items-center gap-1.5">
										{trade.symbol}
										<TradeSourceBadge source={trade.source} />
									</span>
								</TableCell>
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
										{/* Иконка правки комментария — pencil 12 приглушённым текстом;
										    комментарий адресуется execId, у ручной записи его нет. */}
										{trade.execId !== null && (
											<Button
												variant="ghost"
												size="icon"
												className="size-5"
												aria-label={`комментарий сделки ${trade.execId}`}
												title="Править комментарий"
												onClick={() => setCommentTarget(trade)}
											>
												<Pencil aria-hidden className="size-3 text-text-muted" />
											</Button>
										)}
									</div>
								</TableCell>
								<TableCell>
									{/* Распределение сделок — desktop-операция, чтение доступно с телефона. */}
									{/* Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив */}
									{isMobile === false && <div className="flex gap-1.5">
										{/* Мелкие кнопки действия строки: радиус 6, паддинг [3,7], кегль 9.5.
										    Правка и удаление — только у ручной записи; у биржевой их нет. */}
										{/* Traceability: openspec:ui/screens#scenario-detail-synced-trade-immutable */}
										{trade.source === "manual" && (
											<>
												<Button
													variant="outline"
													size="sm"
													className="h-auto rounded-[6px] px-[7px] py-[3px] text-[9.5px] text-text-secondary"
													disabled={tradeCommandsPending}
													onClick={() => setEditingTrade(trade)}
												>
													Изменить
												</Button>
												<Button
													variant="outline"
													size="sm"
													className="h-auto rounded-[6px] px-[7px] py-[3px] text-[9.5px] text-text-secondary"
													disabled={tradeCommandsPending}
													onClick={() => setDeleteTarget(trade)}
												>
													<span className="text-neg">Удалить</span>
												</Button>
											</>
										)}
										<Button
											variant="outline"
											size="sm"
											className="h-auto rounded-[6px] px-[7px] py-[3px] text-[9.5px] text-text-secondary"
											disabled={actionsPending}
											onClick={() => onReturn(tradeKey(trade))}
										>
											Во «Входящие»
										</Button>
										<Button
											variant="outline"
											size="sm"
											className="h-auto rounded-[6px] px-[7px] py-[3px] text-[9.5px] text-text-secondary"
											disabled={actionsPending}
											onClick={() => beginMove(tradeKey(trade))}
										>
											Перенести…
										</Button>
									</div>}
								</TableCell>
							</TableRow>
						))
					)}
				</TableBody>
			</Table>

			{/* Модальный split-редактор комментария биржевой сделки (§10). */}
			<MarkdownEditDialog
				open={commentTarget !== null}
				title={`Комментарий сделки ${commentTarget?.execId ?? ""}`}
				description="Комментарий следует за сделкой при переносе и возврате во «Входящие»."
				defaultText={commentTarget?.comment ?? ""}
				onSave={(text) => {
					if (commentTarget !== null && commentTarget.execId !== null) {
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
		</GridSection>
	);
}
