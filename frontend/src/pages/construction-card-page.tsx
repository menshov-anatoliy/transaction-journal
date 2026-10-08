import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import * as React from "react";
import { Link, useNavigate, useParams } from "react-router";
import { ArrowLeft, Pencil } from "lucide-react";
import { fetchConstructionCard, type ConstructionStatus } from "@/lib/api/construction-card";
import { applyHint, dismissHint, fetchConstructionHintsPanel, markHintSeen } from "@/lib/api/hints";
import { StatusChip } from "@/components/design";
import { useCardCommands } from "@/components/constructions/card/use-card-commands";
import { ConstructionMetricStrip } from "@/components/constructions/card/metric-strip";
import { PositionsTable } from "@/components/constructions/card/positions-table";
import { TradesTable } from "@/components/constructions/card/trades-table";
import { ClosingEntriesTable } from "@/components/constructions/card/closing-entries-table";
import { AdjustmentsTable } from "@/components/constructions/card/adjustments-table";
import { ConstructionChatsPanel } from "@/components/constructions/card/construction-chats-panel";
import { HintsPanel } from "@/components/hints/hints-panel";
import { MarkdownViewer } from "@/components/markdown/markdown-viewer";
import { MarkdownEditDialog } from "@/components/markdown/markdown-edit-dialog";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/lib/api/http";
import type { ConstructionCard } from "@/lib/api/construction-card";

// Карточка конструкции — маршрут /constructions/{id} по концепции §4: полная
// информация и всё управление (паритет №3–№9). Шапка с именем, статусом и
// действиями; kstrip метрик с периодом и полным индикатором; комментарий
// MD-рендером с модальным split-редактором; панель подсказок конструкции;
// четыре таблицы записей; правая скрываемая область чатов с автопривязкой.
// Все данные и команды карточки идут через единый версионированный API.
// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api

/** Ключ запроса снимка карточки. */
const cardKey = (constructionId: number) => ["construction-card", constructionId] as const;

/** Статус конструкции словами. */
function statusText(status: ConstructionStatus): string {
	switch (status) {
		case "open":
			return "открыта";
		case "closed":
			return "закрыта";
		case "archived":
			return "архив";
	}
}

// Тон статусной пилюли по инстансам дизайн-нод: «открыта» — pos (X7CR1q,
// Body #2: accentSoft/accentStrong), «закрыта» — neutral (EIqx3:
// surface2/textSecondary), «архив» — muted (dAcLW: surface2/textMuted).
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2
function statusTone(status: ConstructionStatus): "pos" | "neutral" | "muted" {
	switch (status) {
		case "open":
			return "pos";
		case "closed":
			return "neutral";
		case "archived":
			return "muted";
	}
}

export function ConstructionCardPage() {
	const { constructionId } = useParams<{ constructionId: string }>();
	const id = Number(constructionId);

	const cardQuery = useQuery({
		queryKey: cardKey(id),
		queryFn: () => fetchConstructionCard(id),
		enabled: Number.isFinite(id) && id > 0,
	});

	if (cardQuery.isPending) {
		return (
			<section className="flex flex-col gap-3 p-6">
				<h1 className="page-title">Карточка конструкции</h1>
				<p className="text-muted-foreground text-sm">чтение конструкции…</p>
			</section>
		);
	}

	// Явные состояния вместо пустого экрана: 404 — «не найдена», прочие сбои —
	// «недоступна» с причиной обновить экран.
	if (cardQuery.isError) {
		const notFound = cardQuery.error instanceof ApiError && cardQuery.error.status === 404;
		return (
			<section className="flex flex-col gap-3 p-6">
				<h1 className="page-title">Карточка конструкции</h1>
				{notFound ? (
					<p className="text-sm">
						Конструкция не найдена — возможно, она удалена.{" "}
						<Link to="/" className="text-primary underline-offset-4 hover:underline">
							Вернуться к списку конструкций
						</Link>
						.
					</p>
				) : (
					<p className="text-sm" role="alert">
						Журнал недоступен: {cardQuery.error.message}. Обновите экран.
					</p>
				)}
			</section>
		);
	}

	return <ConstructionCardBody card={cardQuery.data} />;
}

/** Тело карточки с данными: снимок уже прочитан. */
function ConstructionCardBody({ card }: { readonly card: ConstructionCard }) {
	const constructionId = card.constructionId;
	const commands = useCardCommands(constructionId);
	const navigate = useNavigate();
	const [commentOpen, setCommentOpen] = React.useState(false);

	// Первый показ активной ошибки любой команды: причина видна у действия.
	const actionError = firstError(
		commands.rename,
		commands.status,
		commands.capital,
		commands.risk,
		commands.profit,
		commands.remove,
		commands.constructionComment,
		commands.positionComment,
		commands.tradeComment,
		commands.addCloseMark,
		commands.editCloseMark,
		commands.deleteCloseMark,
		commands.returnTrade,
		commands.moveTrade,
		commands.addAdjustment,
		commands.editAdjustment,
		commands.deleteAdjustment,
	);

	return (
		<section className="flex flex-col gap-5 p-6">
			<ConstructionHeader card={card} commands={commands} actionError={actionError} onDeleted={() => navigate("/")} />

			<ConstructionMetricStrip card={card} />

			{/* Комментарий конструкции: MD-рендер по политике §10; иконка-карандаш
			    открывает модальный split-редактор. */}
			<div className="flex items-start gap-2">
				<span className="text-muted-foreground text-sm">комментарий</span>
				<div className="min-w-0 flex-1">
					{card.comment === null ? (
						<span className="text-muted-foreground text-sm">—</span>
					) : (
						<MarkdownViewer text={card.comment} />
					)}
				</div>
				<Button variant="outline" size="sm" onClick={() => setCommentOpen(true)} aria-label="изменить комментарий конструкции">
					<Pencil aria-hidden />
					изменить
				</Button>
			</div>

			<MarkdownEditDialog
				open={commentOpen}
				title="Комментарий конструкции"
				description="Свободный комментарий владельца; пустой текст снимает комментарий."
				defaultText={card.comment ?? ""}
				onSave={(text) => commands.constructionComment.mutate(text.trim() === "" ? null : text)}
				onOpenChange={setCommentOpen}
			/>

			<div className="grid grid-cols-1 items-start gap-6 xl:grid-cols-[minmax(0,1fr)_auto]">
				<div className="flex min-w-0 flex-col gap-6">
					<ConstructionHintsArea constructionId={constructionId} />

					<section className="flex flex-col gap-2">
						<h2 className="text-base font-semibold">Позиции</h2>
						<PositionsTable
							constructionId={constructionId}
							positions={card.positions}
							markPending={commands.addCloseMark.isPending}
							onAddCloseMark={(mark) => commands.addCloseMark.mutate(mark)}
							onSaveComment={(input) => commands.positionComment.mutate(input)}
						/>
					</section>

					<section className="flex flex-col gap-2">
						<h2 className="text-base font-semibold">Сделки</h2>
						<TradesTable
							constructionId={constructionId}
							trades={card.trades}
							actionsPending={commands.returnTrade.isPending || commands.moveTrade.isPending}
							onReturn={(execId) => commands.returnTrade.mutate(execId)}
							onMove={(input) => commands.moveTrade.mutate(input)}
							onSaveComment={(input) => commands.tradeComment.mutate(input)}
						/>
					</section>

					<section className="flex flex-col gap-2">
						<h2 className="text-base font-semibold">Закрывающие записи</h2>
						<ClosingEntriesTable
							entries={card.closingEntries}
							warnings={card.closingWarnings}
							actionsPending={commands.editCloseMark.isPending || commands.deleteCloseMark.isPending}
							onEditMark={(input) => commands.editCloseMark.mutate(input)}
							onDeleteMark={(markId) => commands.deleteCloseMark.mutate(markId)}
						/>
					</section>

					<section className="flex flex-col gap-2">
						<h2 className="text-base font-semibold">Корректировки PnL</h2>
						<AdjustmentsTable
							adjustments={card.adjustments}
							actionsPending={commands.addAdjustment.isPending || commands.editAdjustment.isPending || commands.deleteAdjustment.isPending}
							onAdd={(adjustment) => commands.addAdjustment.mutate(adjustment)}
							onEdit={(input) => commands.editAdjustment.mutate(input)}
							onDelete={(adjustmentId) => commands.deleteAdjustment.mutate(adjustmentId)}
						/>
					</section>
				</div>

				{/* Правая скрываемая область чатов с автопривязкой к конструкции. */}
				<ConstructionChatsPanel constructionId={constructionId} isClosed={card.status !== "open"} />
			</div>
		</section>
	);
}

/** Первая активная ошибка из мутаций карточки. */
function firstError(...mutations: readonly { isError: boolean; error: Error | null }[]): string | null {
	for (const mutation of mutations) {
		if (mutation.isError && mutation.error !== null) {
			return mutation.error.message;
		}
	}

	return null;
}

/** Шапка карточки: имя, статус, действия и отказы домена причиной. */
function ConstructionHeader({
	card,
	commands,
	actionError,
	onDeleted,
}: {
	card: ConstructionCard;
	commands: ReturnType<typeof useCardCommands>;
	actionError: string | null;
	onDeleted: () => void;
}) {
	const [renameOpen, setRenameOpen] = React.useState(false);
	const [renameValue, setRenameValue] = React.useState(card.name);
	const [capitalOpen, setCapitalOpen] = React.useState(false);
	const [capitalValue, setCapitalValue] = React.useState(card.allocatedCapitalUsdt?.toString() ?? "");
	const [boundForm, setBoundForm] = React.useState<"risk" | "profit" | null>(null);
	const [percentValue, setPercentValue] = React.useState("");
	const [usdtValue, setUsdtValue] = React.useState("");
	const [deleteOpen, setDeleteOpen] = React.useState(false);
	const [deleteBackup, setDeleteBackup] = React.useState(true);

	// Удаление предлагается только конструкции без сделок и корректировок:
	// пользователь снимает записи прежде удаления.
	const canDelete = card.trades.length === 0 && card.adjustments.length === 0;

	const openBound = (bound: "risk" | "profit") => {
		setPercentValue(bound === "risk" ? card.riskPercent?.toString() ?? "" : card.profitPercent?.toString() ?? "");
		setUsdtValue(bound === "risk" ? card.riskUsdt?.toString() ?? "" : card.profitUsdt?.toString() ?? "");
		setBoundForm(bound);
	};

	// Граница вводится ровно в одной единице: заполнение одной ячейки
	// очищает вторую; обе пустые равносильны удалению параметра.
	const onPercentChange = (value: string) => {
		setPercentValue(value);
		if (value !== "") {
			setUsdtValue("");
		}
	};

	const onUsdtChange = (value: string) => {
		setUsdtValue(value);
		if (value !== "") {
			setPercentValue("");
		}
	};

	const submitBound = () => {
		if (boundForm === null) {
			return;
		}

		const percent = percentValue.trim() === "" ? null : Number(percentValue.replace(",", "."));
		const usdt = usdtValue.trim() === "" ? null : Number(usdtValue.replace(",", "."));
		const percentKnown = percent !== null && !Number.isNaN(percent);
		const usdtKnown = usdt !== null && !Number.isNaN(usdt);
		const value = percentKnown ? percent : usdtKnown ? usdt : null;
		const unit = percentKnown ? ("percent" as const) : usdtKnown ? ("usdt" as const) : null;
		if (boundForm === "risk") {
			commands.risk.mutate({ value, unit });
		} else {
			commands.profit.mutate({ value, unit });
		}

		setBoundForm(null);
	};

	const submitCapital = () => {
		const capital = capitalValue.trim() === "" ? null : Number(capitalValue.replace(",", "."));
		commands.capital.mutate(capital !== null && !Number.isNaN(capital) ? capital : null);
		setCapitalOpen(false);
	};

	const submitDelete = () => {
		commands.remove.mutate(deleteBackup, { onSuccess: onDeleted });
	};

	const busy =
		commands.rename.isPending ||
		commands.status.isPending ||
		commands.capital.isPending ||
		commands.risk.isPending ||
		commands.profit.isPending ||
		commands.remove.isPending;

	return (
		<header className="flex flex-col gap-2">
			<Button variant="ghost" size="sm" className="text-muted-foreground -ml-2 self-start" asChild>
				<Link to="/">
					<ArrowLeft aria-hidden />
					Конструкции
				</Link>
			</Button>

			{/* Титул с именем и статусом-пилюлей: строка TitleRow мастера Body #2
			    (Uw04r) — имя 21/600 и чип статуса с зазором 10, выравнивание по
			    центру строки. */}
			{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
			<h1 className="page-title flex flex-wrap items-center gap-2.5">
				{card.name}
				<StatusChip tone={statusTone(card.status)}>{statusText(card.status)}</StatusChip>
			</h1>

			{/* Действия конструкции в шапке: переименование, смена ручного
			    статуса (включая архив и возврат из архива), капитал, плановые
			    границы и удаление пустой с подтверждением и флажком бэкапа. */}
			<div className="flex flex-wrap items-center gap-2">
				{renameOpen ? (
					<div className="flex items-center gap-2">
						<input
							className="border-input bg-background h-8 w-64 rounded-md border px-2 text-sm"
							value={renameValue}
							onChange={(event) => setRenameValue(event.target.value)}
							aria-label="новое имя"
						/>
						<Button
							size="sm"
							disabled={busy || renameValue.trim() === ""}
							onClick={() => {
								commands.rename.mutate(renameValue.trim());
								setRenameOpen(false);
							}}
						>
							Сохранить имя
						</Button>
						<Button size="sm" variant="outline" onClick={() => setRenameOpen(false)}>
							Отмена
						</Button>
					</div>
				) : (
					<Button variant="outline" size="sm" disabled={busy} onClick={() => setRenameOpen(true)}>
						Переименовать
					</Button>
				)}

				{/* Смена ручного статуса — свободные переходы; возврат из архива
				    восстанавливает статус «закрыта». */}
				{card.status === "open" && (
					<Button variant="outline" size="sm" disabled={busy} onClick={() => commands.status.mutate("closed")}>
						Закрыть
					</Button>
				)}
				{card.status === "closed" && (
					<Button variant="outline" size="sm" disabled={busy} onClick={() => commands.status.mutate("open")}>
						Открыть
					</Button>
				)}
				{card.status !== "archived" ? (
					<Button variant="outline" size="sm" disabled={busy} onClick={() => commands.status.mutate("archived")}>
						В архив
					</Button>
				) : (
					<Button variant="outline" size="sm" disabled={busy} onClick={() => commands.status.mutate("closed")}>
						Вернуть из архива
					</Button>
				)}

				{capitalOpen ? (
					<div className="flex items-center gap-2">
						<input
							className="border-input bg-background h-8 w-32 rounded-md border px-2 text-sm"
							placeholder="пусто — не задан"
							value={capitalValue}
							onChange={(event) => setCapitalValue(event.target.value)}
							aria-label="выделенный капитал, USDT"
						/>
						<Button size="sm" disabled={busy} onClick={submitCapital}>
							Сохранить капитал
						</Button>
						<Button size="sm" variant="outline" onClick={() => setCapitalOpen(false)}>
							Отмена
						</Button>
					</div>
				) : (
					<Button variant="outline" size="sm" disabled={busy} onClick={() => setCapitalOpen(true)}>
						Изменить капитал
					</Button>
				)}

				<Button variant="outline" size="sm" disabled={busy} onClick={() => openBound("risk")}>
					Риск…
				</Button>
				<Button variant="outline" size="sm" disabled={busy} onClick={() => openBound("profit")}>
					Профит…
				</Button>

				{canDelete && (
					<Button variant="destructive" size="sm" disabled={busy} onClick={() => setDeleteOpen(true)}>
						Удалить…
					</Button>
				)}
			</div>

			{boundForm !== null && (
				// Форма плановой границы: значение ровно в одной единице — вторая
				// ячейка очищается заполнением первой; обе пустые — «Убрать».
				<div className="bg-card flex flex-wrap items-end gap-3 rounded-md border p-3">
					<span className="text-sm font-medium">{boundForm === "risk" ? "риск" : "профит"} конструкции</span>
					<label className="flex flex-col gap-1 text-sm">
						%
						<input
							className="border-input bg-background h-9 w-24 rounded-md border px-2 text-sm"
							value={percentValue}
							onChange={(event) => onPercentChange(event.target.value)}
						/>
					</label>
					<label className="flex flex-col gap-1 text-sm">
						USDT
						<input
							className="border-input bg-background h-9 w-28 rounded-md border px-2 text-sm"
							value={usdtValue}
							onChange={(event) => onUsdtChange(event.target.value)}
						/>
					</label>
					<Button size="sm" disabled={busy} onClick={submitBound}>
						Сохранить
					</Button>
					<Button size="sm" variant="outline" onClick={() => setBoundForm(null)}>
						Отмена
					</Button>
					<span className="text-muted-foreground text-xs">одна единица ввода; обе пустые — убрать параметр</span>
				</div>
			)}

			{deleteOpen && (
				// Удаление требует явного подтверждения с флажком резервной копии,
				// включённым по умолчанию: копия создаётся до команды домена.
				<div className="bg-card flex flex-col gap-2 rounded-md border p-3">
					<span className="text-sm font-medium">Удалить пустую конструкцию «{card.name}»? Действие необратимо.</span>
					<label className="flex items-center gap-2 text-sm">
						<input type="checkbox" checked={deleteBackup} onChange={(event) => setDeleteBackup(event.target.checked)} />
						сделать резервную копию базы перед удалением
					</label>
					<div className="flex gap-2">
						<Button variant="destructive" size="sm" disabled={busy} onClick={submitDelete}>
							Удалить
						</Button>
						<Button size="sm" variant="outline" onClick={() => setDeleteOpen(false)}>
							Отмена
						</Button>
					</div>
				</div>
			)}

			{actionError !== null && (
				<p className="text-destructive text-sm" role="alert">
					Действие не выполнено: {actionError}
				</p>
			)}
		</header>
	);
}

/** Панель подсказок субъекта-конструкции: субъект фиксирован карточкой. */
function ConstructionHintsArea({ constructionId }: { constructionId: number }) {
	const queryClient = useQueryClient();
	const panelQuery = useQuery({
		queryKey: ["hints-panel", "construction", constructionId] as const,
		queryFn: () => fetchConstructionHintsPanel(constructionId),
	});

	// Автопометка первого показа: следствие показа в UI, повторные показы
	// запись не меняют.
	React.useEffect(() => {
		if (panelQuery.data === undefined) {
			return;
		}

		for (const group of panelQuery.data.liveGroups) {
			for (const hint of group.hints) {
				if (hint.firstSeenAt === null) {
					void markHintSeen(hint.id);
				}
			}
		}
	}, [panelQuery.data]);

	const transitionMutation = useMutation({
		mutationFn: async (request: { hintId: number; action: "apply" | "dismiss" }) =>
			request.action === "apply" ? applyHint(request.hintId) : dismissHint(request.hintId),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: ["hints-panel", "construction", constructionId] });
		},
	});

	return (
		<section className="flex flex-col gap-3">
			<h2 className="text-base font-semibold">Подсказки</h2>
			{panelQuery.isPending && <p className="text-muted-foreground text-sm">чтение подсказок…</p>}
			{panelQuery.isError && (
				<p className="text-sm" role="alert">
					Подсказки недоступны: {panelQuery.error.message}
				</p>
			)}
			{panelQuery.data !== undefined && (
				<HintsPanel
					panel={panelQuery.data}
					onApply={(hintId) => transitionMutation.mutate({ hintId, action: "apply" })}
					onDismiss={(hintId) => transitionMutation.mutate({ hintId, action: "dismiss" })}
				/>
			)}
		</section>
	);
}
