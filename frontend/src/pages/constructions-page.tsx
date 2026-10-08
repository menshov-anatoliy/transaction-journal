import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import * as React from "react";
import { RefreshCw } from "lucide-react";
import type { ConstructionsOverview } from "@/lib/api/constructions";
import { fetchConstructionPreview, fetchConstructionsOverview } from "@/lib/api/constructions";
import {
	applyHint,
	dismissHint,
	fetchConstructionHintsPanel,
	fetchJournalHintsPanel,
	markHintSeen,
	runHintsPass,
	type HintPassResult,
} from "@/lib/api/hints";
import { runSync, type SyncRunSummary } from "@/lib/api/sync";
import { ConstructionPreviewCard } from "@/components/constructions/construction-preview";
import { ConstructionsTable } from "@/components/constructions/constructions-table";
import { HintsPanel } from "@/components/hints/hints-panel";
import { Button } from "@/components/ui/button";
import { formatMoment } from "@/lib/format/display-time";
import { formatCount, pluralForm } from "@/lib/format/plural";
import { formatSignedAmount } from "@/lib/format/quantity";

// Раздел «Конструкции» — главный экран SPA по концепции §3: шапка с итогом
// журнала, счётчиками и кнопкой синхронизации; таблица конструкций с
// выделением строк; правая контекстная область — панель подсказок журнала
// с ручным запуском прохода агента или read-only превью выделенной
// конструкции. Данные и команды идут через единый версионированный API.
// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api

/** Ключи запросов раздела для согласованных инвалидаций кэша. */
const overviewKey = ["constructions-overview"] as const;
const journalPanelKey = ["hints-panel", "journal"] as const;
const constructionPanelKey = (constructionId: number) => ["hints-panel", "construction", constructionId] as const;
const previewKey = (constructionId: number) => ["construction-preview", constructionId] as const;

export function ConstructionsPage() {
	const [selectedId, setSelectedId] = React.useState<number | null>(null);
	const queryClient = useQueryClient();

	// Обзор раздела: сводка шапки и строки таблицы одним запросом.
	const overviewQuery = useQuery({ queryKey: overviewKey, queryFn: fetchConstructionsOverview });

	// Синхронизация из шапки: после закрытого запуска раздел перечитывает
	// обзор — новые сделки меняют итог и строки.
	const syncMutation = useMutation({
		mutationFn: runSync,
		onSuccess: () => queryClient.invalidateQueries({ queryKey: overviewKey }),
	});

	// Проход агента подсказок: панели и бейджи строк перечитываются по
	// результатам прохода.
	const passMutation = useMutation({
		mutationFn: runHintsPass,
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: ["hints-panel"] });
			void queryClient.invalidateQueries({ queryKey: overviewKey });
		},
	});

	// Команды карточек: после перевода подсказки панели и бейджи перечитываются.
	const transitionMutation = useMutation({
		mutationFn: async (request: { hintId: number; action: "apply" | "dismiss" }) =>
			request.action === "apply" ? applyHint(request.hintId) : dismissHint(request.hintId),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: ["hints-panel"] });
			void queryClient.invalidateQueries({ queryKey: overviewKey });
		},
	});

	return (
		<section className="flex flex-col gap-4 p-6">
			<h1 className="text-2xl font-semibold tracking-tight">Конструкции</h1>
			<ConstructionsHeader overview={overviewQuery.data} syncMutation={syncMutation} />

			<div className="grid grid-cols-1 items-start gap-6 xl:grid-cols-[minmax(0,1fr)_380px]">
				<ConstructionsMain overviewQuery={overviewQuery} selectedId={selectedId} onSelect={setSelectedId} />
				<aside className="flex flex-col gap-4">
					{selectedId === null ? (
						<JournalHintsArea passMutation={passMutation} onTransition={(request) => transitionMutation.mutate(request)} />
					) : (
						<ConstructionArea constructionId={selectedId} onTransition={(request) => transitionMutation.mutate(request)} />
					)}
				</aside>
			</div>
		</section>
	);
}

/** Шапка раздела: итог журнала с разбивкой, котировки, счётчики и синхронизация. */
function ConstructionsHeader({
	overview,
	syncMutation,
}: {
	overview: ConstructionsOverview | undefined;
	syncMutation: ReturnType<typeof useMutation<SyncRunSummary, Error, void>>;
}) {
	const summary = overview?.summary;

	return (
		<header className="flex flex-wrap items-center gap-x-6 gap-y-2">
			{/* Итог журнала — единственное место постоянного показа: с разбивкой
			    на реализованную и нереализованную части. */}
			{summary === undefined ? (
				<span className="text-muted-foreground text-sm">чтение журнала…</span>
			) : (
				<>
					<p className="text-sm">
						<span className="text-muted-foreground">итог </span>
						<b className="text-base">
							{summary.totalPnL === null ? "недоступен" : `${formatSignedAmount(summary.totalPnL)} USDT`}
						</b>
						{summary.totalPnL === null && summary.hasMarkFailure ? " (неполный: сбой котировок)" : ""}
						<span className="text-muted-foreground">
							{" "}
							(реализов. {formatSignedAmount(summary.realizedPnL)} / нереализов.{" "}
							{summary.unrealizedPnL === null ? "сбой котировок" : formatSignedAmount(summary.unrealizedPnL)})
						</span>
					</p>
					<p className="text-muted-foreground text-sm">
						{summary.hasMarkFailure
							? "котировки: сбой котировок"
							: summary.marksAsOf === null
								? "котировки: нет открытых остатков"
								: `котировки на ${formatMoment(summary.marksAsOf)}`}
					</p>
					<p className="text-muted-foreground text-sm">
						{formatCount(summary.constructionCount, { one: "конструкция", few: "конструкции", many: "конструкций" })} (
						{summary.openCount} {pluralForm(summary.openCount, { one: "открыта", few: "открыты", many: "открыто" })})
					</p>
				</>
			)}

			<Button size="sm" variant="outline" className="ml-auto" disabled={syncMutation.isPending} onClick={() => syncMutation.mutate()}>
				<RefreshCw aria-hidden className={syncMutation.isPending ? "animate-spin" : undefined} />
				Синхронизировать
			</Button>

			{/* Состояние и итог команды синхронизации; подробности — в разделе
			    «Синхронизация», шапке достаточно строки статуса. */}
			{syncMutation.isPending && (
				<p className="text-muted-foreground w-full text-sm" role="status">
					Синхронизация выполняется…
				</p>
			)}
			{syncMutation.isError && (
				<p className="text-destructive w-full text-sm" role="alert">
					Синхронизация прервана: {syncMutation.error.message}
				</p>
			)}
			{syncMutation.data !== undefined && (
				<p className="w-full text-sm" role="status">
					Синхронизация завершена — режим {syncMutation.data.mode === "backfill" ? "первичной загрузки" : "догрузки"}: сделок{" "}
					{syncMutation.data.newExecutions}, delivery {syncMutation.data.newDeliveries}, инструментов {syncMutation.data.newInstruments}.
				</p>
			)}
		</header>
	);
}

/** Центральная область: таблица конструкций с состояниями чтения и недоступности. */
function ConstructionsMain({
	overviewQuery,
	selectedId,
	onSelect,
}: {
	overviewQuery: ReturnType<typeof useQuery<ConstructionsOverview>>;
	selectedId: number | null;
	onSelect: (constructionId: number) => void;
}) {
	if (overviewQuery.isPending) {
		return <p className="text-muted-foreground text-sm">чтение журнала…</p>;
	}

	// Явное состояние вместо пустого экрана: журнал не прочитан — таблица
	// не показывается, пользователь видит причину.
	if (overviewQuery.isError) {
		return (
			<p className="text-sm" role="alert">
				Журнал недоступен: {overviewQuery.error.message}. Обновите экран.
			</p>
		);
	}

	return (
		<div className="rounded-lg border">
			<ConstructionsTable rows={overviewQuery.data.items} selectedId={selectedId} onSelect={onSelect} />
		</div>
	);
}

/** Правая область без выделения: панель подсказок журнала и ручной проход. */
function JournalHintsArea({
	passMutation,
	onTransition,
}: {
	passMutation: ReturnType<typeof useMutation<HintPassResult, Error, void>>;
	onTransition: (request: { hintId: number; action: "apply" | "dismiss" }) => void;
}) {
	const panelQuery = useQuery({ queryKey: journalPanelKey, queryFn: fetchJournalHintsPanel });

	// Автопометка первого показа: следствие показа в UI, повторные показы
	// запись не меняют — помечаются только записи без отметки.
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

	return (
		<section className="flex flex-col gap-3">
			<div className="flex items-center justify-between gap-2">
				<h2 className="text-base font-semibold">Подсказки журнала</h2>
				<Button size="sm" disabled={passMutation.isPending} onClick={() => passMutation.mutate()}>
					{passMutation.isPending ? "Проход выполняется…" : "Запустить проход подсказок"}
				</Button>
			</div>

			<PassStatus mutation={passMutation} />

			{panelQuery.isPending && <p className="text-muted-foreground text-sm">чтение подсказок…</p>}
			{panelQuery.isError && (
				<p className="text-sm" role="alert">
					Подсказки недоступны: {panelQuery.error.message}
				</p>
			)}
			{panelQuery.data !== undefined && (
				<HintsPanel
					panel={panelQuery.data}
					onApply={(hintId) => onTransition({ hintId, action: "apply" })}
					onDismiss={(hintId) => onTransition({ hintId, action: "dismiss" })}
				/>
			)}
		</section>
	);
}

/** Правая область с выделением: превью конструкции и её подсказки. */
function ConstructionArea({
	constructionId,
	onTransition,
}: {
	constructionId: number;
	onTransition: (request: { hintId: number; action: "apply" | "dismiss" }) => void;
}) {
	const previewQuery = useQuery({
		queryKey: previewKey(constructionId),
		queryFn: () => fetchConstructionPreview(constructionId),
	});
	const panelQuery = useQuery({
		queryKey: constructionPanelKey(constructionId),
		queryFn: () => fetchConstructionHintsPanel(constructionId),
	});

	// Автопометка первого показа подсказок превью.
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

	return (
		<>
			{previewQuery.isPending && <p className="text-muted-foreground text-sm">чтение конструкции…</p>}
			{previewQuery.isError && (
				<p className="text-sm" role="alert">
					Конструкция недоступна: {previewQuery.error.message}
				</p>
			)}
			{previewQuery.data !== undefined && <ConstructionPreviewCard preview={previewQuery.data} />}

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
						onApply={(hintId) => onTransition({ hintId, action: "apply" })}
						onDismiss={(hintId) => onTransition({ hintId, action: "dismiss" })}
					/>
				)}
			</section>
		</>
	);
}

/** Строка состояния ручного прохода: счётчики, пропуск или проблемы корпуса. */
function PassStatus({ mutation }: { mutation: ReturnType<typeof useMutation<HintPassResult, Error, void>> }) {
	if (mutation.isPending) {
		return (
			<p className="text-muted-foreground text-sm" role="status">
				Проход подсказок выполняется…
			</p>
		);
	}

	if (mutation.isError) {
		return (
			<p className="text-destructive text-sm" role="alert">
				Проход подсказок прерван: {mutation.error.message}
			</p>
		);
	}

	const result = mutation.data;
	if (result === undefined) {
		return null;
	}

	if (result.outcome === "completed") {
		return (
			<p className="text-sm" role="status">
				Проход завершён: создано {result.createdHints}, погашено {result.expiredHints}
			</p>
		);
	}

	if (result.outcome === "skipped-market-unavailable") {
		return (
			<p className="text-muted-foreground text-sm" role="status">
				Проход пропущен: котировки недоступны — {result.diagnostics?.[0] ?? "причина неизвестна"}
			</p>
		);
	}

	// CorpusInvalid: агрегированный список проблем карточек — владелец
	// приводит корпус в порядок до следующего запуска.
	return (
		<div className="text-destructive text-sm" role="alert">
			<b>Корпус правил невалиден:</b>
			<ul className="list-disc pl-4">
				{(result.diagnostics ?? []).map((problem) => (
					<li key={problem}>{problem}</li>
				))}
			</ul>
		</div>
	);
}
