import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import * as React from "react";
import { Lightbulb, Loader2, RefreshCw } from "lucide-react";
import { useSearchParams } from "react-router";
import type { ConstructionsOverview } from "@/lib/api/constructions";
import { fetchConstructionPreview } from "@/lib/api/constructions";
import { constructionsOverviewQueryKey, useConstructionsOverviewQuery } from "@/lib/api/constructions-overview-query";
import { runHintsPass, type HintPassResult } from "@/lib/api/hints";
import { runSync, type SyncRunSummary } from "@/lib/api/sync";
import { ConstructionPreviewCard } from "@/components/constructions/construction-preview";
import { ConstructionsTable } from "@/components/constructions/constructions-table";
import { useContextPanel } from "@/components/layout/context-panel";
import { Button } from "@/components/ui/button";
import { formatClock, formatMoment } from "@/lib/format/display-time";
import { formatSignedAmount } from "@/lib/format/quantity";
import { useIsMobile } from "@/lib/use-mobile";
import { cn } from "@/lib/utils";

// Раздел «Конструкции» — главный экран SPA: тулбар с титулом раздела и
// счётчиками конструкций, сегментом области видимости и действиями (ручной
// проход подсказок, синхронизация); строка итога сводки; таблица с выделением
// строк. Вход в разбор входящих — через раздел «Входящие» панели навигации,
// кнопки в тулбаре раздела нет (решение раунд-сверки формы).
// Выделение и область видимости живут в URL и переживают перезагрузку;
// краткая карточка выделенной конструкции регистрируется в правой
// контекстной панели каркаса через useContextPanel (design.md D3–D6).
// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api

/** Ключ запроса превью для согласованных инвалидаций кэша карточки. */
const previewKey = (constructionId: number) => ["construction-preview", constructionId] as const;

/** Область видимости списка: только открытые или весь журнал. */
type ScopeFilter = "open" | "all";

export function ConstructionsPage() {
	const [searchParams, setSearchParams] = useSearchParams();
	const [mobileContextOpen, setMobileContextOpen] = React.useState(false);
	const isMobile = useIsMobile();
	const queryClient = useQueryClient();

	// Область видимости — URL-параметр со значением по умолчанию «открытые»:
	// неизвестные значения трактуются как дефолт.
	// Traceability: openspec:ui/screens#scenario-list-scope-open-default
	const scopeParam = searchParams.get("scope");
	const scope: ScopeFilter = scopeParam === "all" ? "all" : "open";

	// Выделенная конструкция — URL-параметр ?construction=<id> (design.md D4):
	// выделение переживает перезагрузку и делится ссылкой.
	// Traceability: openspec:ui/screens#scenario-list-row-click-selects
	const constructionParam = searchParams.get("construction");
	const selectedId =
		constructionParam !== null && Number.isFinite(Number(constructionParam)) && constructionParam !== ""
			? Number(constructionParam)
			: null;

	// Обзор раздела: сводка шапки и строки таблицы одним запросом. Хук читает
	// общий кэш с итогом в топбаре каркаса (один ключ — один запрос), поэтому
	// сводка списка не плодит дублирующих обращений к API.
	// Traceability: openspec:ui/screens#scenario-list-summary-shows-pnl-breakdown
	const overviewQuery = useConstructionsOverviewQuery();
	// Счётчики у титула тулбара: полный набор конструкций, открыто и архив;
	// читаются из сводки обзора тем же запросом.
	const summary = overviewQuery.data?.summary;

	// Синхронизация из шапки: после закрытого запуска раздел перечитывает
	// обзор — новые сделки меняют итог и строки.
	const syncMutation = useMutation({
		mutationFn: runSync,
		onSuccess: () => queryClient.invalidateQueries({ queryKey: constructionsOverviewQueryKey }),
	});

	// Ручной проход подсказок из тулбара: по завершению перечитываются
	// бейджи живых подсказок строк (обзор) и панели подсказок.
	// Traceability: openspec:ui/screens#scenario-ui-manual-pass-runs-engine
	const passMutation = useMutation({
		mutationFn: runHintsPass,
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: ["hints-panel"] });
			void queryClient.invalidateQueries({ queryKey: constructionsOverviewQueryKey });
		},
	});

	// Переключение выделения URL-параметром: повторный клик по выделенной
	// строке снимает выделение (параметр удаляется, панель сворачивается).
	// Traceability: openspec:ui/screens#scenario-list-second-click-collapses-context-panel
	const toggleSelection = React.useCallback(
		(constructionId: number) => {
			setSearchParams(
				(previous) => {
					const next = new URLSearchParams(previous);
					if (next.get("construction") === String(constructionId)) {
						next.delete("construction");
					} else {
						next.set("construction", String(constructionId));
					}
					return next;
				},
				{ replace: true },
			);
		},
		[setSearchParams],
	);

	// Двойной клик открывает карточку конструкции в новом окне браузера;
	// ссылка корне-относительная — SPA раздаётся из корня хоста без префикса.
	// Traceability: openspec:ui/screens#scenario-list-double-click-opens-new-window
	// Traceability: adr:docs/adr/0013-spa-served-from-host-root.md#fixed-mechanics
	const openInNewWindow = React.useCallback((constructionId: number) => {
		window.open(`/constructions/${constructionId}`, "_blank");
	}, []);

	// Переключение области видимости: клиентская фильтрация строк по статусу.
	// Traceability: openspec:ui/screens#scenario-list-scope-all-shows-archived
	const setScope = React.useCallback(
		(next: ScopeFilter) => {
			setSearchParams(
				(previous) => {
					const result = new URLSearchParams(previous);
					if (next === "open") {
						result.delete("scope");
					} else {
						result.set("scope", next);
					}
					return result;
				},
				{ replace: true },
			);
		},
		[setSearchParams],
	);

	// Клиентская фильтрация строк по области видимости; счётчики и итог
	// сводки считаются по полному набору из API и фильтром не режутся.
	// Traceability: openspec:ui/screens#scenario-list-scope-all-shows-archived
	const visibleRows = React.useMemo(() => {
		if (overviewQuery.data === undefined) {
			return [];
		}

		return scope === "open" ? overviewQuery.data.items.filter((row) => row.status === "open") : [...overviewQuery.data.items];
	}, [overviewQuery.data, scope]);

	// Строка выделенной конструкции: число живых подсказок для карточки.
	const selectedRow = overviewQuery.data?.items.find((row) => row.constructionId === selectedId) ?? null;

	// Регистрация краткой карточки в правой контекстной панели каркаса:
	// без выделения регистрация null — панель и сегмент контекста скрыты.
	// Объект регистрации обязан быть memo-стабильным, иначе эффект
	// контекстной панели перезапускается на каждом рендере страницы. Число
	// живых подсказок берётся из строки обзора для строки периода карточки.
	// Короткое название «Конструкция» идёт в чип сегмента контекста топбара.
	// Traceability: openspec:ui/screens#scenario-list-context-panel-brief-card
	// Traceability: openspec:ui/screens#scenario-frame-context-chip-shows-registered-content
	const panelRegistration = React.useMemo(
		() =>
			selectedId === null
				? null
				: {
						chipLabel: "Конструкция",
						content: <SelectedConstructionBrief constructionId={selectedId} liveHintCount={selectedRow?.liveHintCount} />,
					},
		[selectedId, selectedRow],
	);
	useContextPanel(panelRegistration);

	return (
		<section className="flex flex-col gap-4 p-6">
			{isMobile ? (
				/*
					Мобильный экран «Конструкций» (раунд-сверка 10.0, мастер
					iGlhI): сразу под топбаром блок «Итог», затем список —
					H1-тулбар (титул, счётчики-строка, область видимости,
					проход подсказок, синхронизация) и десктопная шапка сводки
					скрыты; синхронизация на мобильном — через раздел
					«Синхронизация».
				*/
				// Traceability: change:rework-contextual-topbar/tasks#10.0
				<MobileSummaryBlock summary={summary} />
			) : (
				<>
				{/* Тулбар раздела: титул со счётчиками конструкций, сегмент области
				    видимости и действия — ручной проход подсказок и синхронизация в
				    одном ряду (design.md D5, D6; TitleRow мастера «Экран/Конструкции»). */}
				<div className="flex flex-wrap items-center gap-3">
					{/* Титул раздела — Inter 21/600 дизайн-системы (H1 Body-экранов).
					    Traceability: openspec:ui/design-system#requirement-typography-matches-design-system */}
					<h1 className="page-title">Конструкции</h1>
					{/* Счётчики конструкций рядом с титулом — формат макета «Counters»
					    «конструкций N · открыто N · архив N» (12.5 $textSecondary,
					    мастер NwnY4), архив — все минус открытые. Считаются по полному
					    набору журнала, независимо от области видимости списка. Кнопку
					    «Разобрать входящие» из тулбара убрали решением раунд-сверки:
					    вход — через раздел «Входящие» панели навигации.
					    Traceability: change:rework-contextual-topbar/tasks#2.0
					    Traceability: openspec:ui/screens#requirement-construction-list-screen */}
					{summary !== undefined && (
						<p data-slot="constructions-counters" className="text-[12.5px] text-text-secondary">
							{`конструкций ${summary.constructionCount} · открыто ${summary.openCount} · архив ${
								summary.constructionCount - summary.openCount
							}`}
						</p>
					)}
					<div className="ml-auto flex flex-wrap items-center gap-2">
						{/* Сегмент «Открытые | Все»: состояние в URL, дефолт —
						    открытые; контейнер по фрейму ScopeFilter (b5KyT). */}
						{/* Traceability: openspec:ui/screens#scenario-list-scope-open-default */}
						<div
							data-slot="scope-filter"
							role="group"
							aria-label="Область видимости"
							className="bg-card flex items-center gap-1 rounded-[9px] border p-[3px]"
						>
							<ScopeSegmentButton label="Открытые" active={scope === "open"} onClick={() => setScope("open")} />
							<ScopeSegmentButton label="Все" active={scope === "all"} onClick={() => setScope("all")} />
						</div>
						{/* Ручной проход подсказок рядом с синхронизацией
						    (POST /hints/pass): иконочная кнопка по фрейму HintsPassBtn
						    (Y9mB6), результат — строки состояния под шапкой.
						    Traceability: openspec:ui/screens#scenario-ui-manual-pass-runs-engine */}
						<Button
							size="sm"
							variant="outline"
							aria-label="Запустить проход подсказок"
							title="Запустить проход подсказок"
							disabled={passMutation.isPending}
							onClick={() => passMutation.mutate()}
						>
							{passMutation.isPending ? (
								<Loader2 aria-hidden className="animate-spin" />
							) : (
								<Lightbulb aria-hidden className="size-3.5" />
							)}
						</Button>
						<Button size="sm" variant="outline" disabled={syncMutation.isPending} onClick={() => syncMutation.mutate()}>
							<RefreshCw aria-hidden className={syncMutation.isPending ? "animate-spin" : undefined} />
							Синхронизировать
						</Button>
					</div>
				</div>
				<ConstructionsHeader overview={overviewQuery.data} syncMutation={syncMutation} />
				{/* Состояние и итог ручного прохода: строки состояния живут под
				    сводкой раздела, проблемы корпуса — агрегированным списком.
				    Traceability: openspec:ui/screens#scenario-ui-manual-pass-corpus-error */}
				<PassStatus mutation={passMutation} />
				</>
			)}
			{/* На мобильном правая контекстная панель каркаса скрыта: краткая
			    карточка выделенной конструкции открывается в drawer.
			    Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив */}
			{isMobile && selectedId !== null && (
				<Button variant="outline" size="sm" className="self-start" onClick={() => setMobileContextOpen(true)}>
					Открыть краткую карточку
				</Button>
			)}

			<ConstructionsMain
				overviewQuery={overviewQuery}
				rows={visibleRows}
				selectedId={selectedId}
				onSelect={toggleSelection}
				onOpenInNewWindow={openInNewWindow}
			/>
			{isMobile && mobileContextOpen && (
				<div className="fixed inset-0 z-40">
					<button
						type="button"
						aria-label="Закрыть контекст раздела"
						className="absolute inset-0 bg-black/40"
						onClick={() => setMobileContextOpen(false)}
					/>
					<aside className="bg-background absolute right-0 top-0 z-10 h-full w-[min(28rem,100vw)] overflow-y-auto border-l p-4">
						<div className="mb-3 flex items-center justify-between">
							<h2 className="text-base font-semibold">Краткая карточка</h2>
							<Button variant="outline" size="sm" onClick={() => setMobileContextOpen(false)}>
								Закрыть
							</Button>
						</div>
						{selectedId !== null && <SelectedConstructionBrief constructionId={selectedId} liveHintCount={selectedRow?.liveHintCount} />}
					</aside>
				</div>
			)}
		</section>
	);
}

/*
	Блок «Итог» мобильного экрана «Конструкции» (раунд-сверка 10.0, мастер
	iGlhI «Экран/Mobile · Конструкции»): прозрачный блок сразу под топбаром,
	без карточки — паддинги [14,16], межстрочный зазор 3, три строки: итог
	18/600 $accentStrong; «реализов. … / нереализов. … · котировки HH:MM»
	11 normal $textSecondary (время котировок — из той же сводки обзора, что
	и строка итога, деградации повторяют десктопную сводку); «конструкций
	N · открыто N» 11 normal $textMuted — на мобильном без «архива»,
	источник счётчиков тот же, что у строки у H1 десктопа (решение 2.0).
	H1-тулбар с областью видимости и синхронизацией на мобильном скрыт —
	список идёт сразу за блоком.
*/
// Traceability: openspec:ui/screens#scenario-list-summary-shows-pnl-breakdown
// Traceability: change:rework-contextual-topbar/tasks#2.0
// Traceability: change:rework-contextual-topbar/tasks#10.0
function MobileSummaryBlock({ summary }: { summary: ConstructionsOverview["summary"] | undefined }) {
	if (summary === undefined) {
		return <p className="text-muted-foreground px-4 py-3.5 text-sm">чтение журнала…</p>;
	}

	// Котировки — сегмент строки разбивки; при норме показывается только
	// время (формат мастера iGlhI), сбои повторяют тексты десктопной сводки.
	const quotesNote = summary.hasMarkFailure
		? "котировки: сбой котировок"
		: summary.marksAsOf === null
			? "котировки: нет открытых остатков"
			: `котировки ${formatClock(summary.marksAsOf)}`;

	return (
		<div
			data-slot="constructions-mobile-summary"
			data-testid="constructions-mobile-summary"
			className="flex flex-col gap-[3px] px-4 py-3.5"
		>
			<p className="text-[18px] font-semibold text-accent-strong">
				{summary.totalPnL === null ? "недоступен" : `${formatSignedAmount(summary.totalPnL)} USDT`}
			</p>
			<p className="text-[11px] text-text-secondary">
				{`реализов. ${formatSignedAmount(summary.realizedPnL)} / нереализов. ${
					summary.unrealizedPnL === null ? "сбой котировок" : formatSignedAmount(summary.unrealizedPnL)
				} · ${quotesNote}`}
			</p>
			<p className="text-[11px] text-text-muted">{`конструкций ${summary.constructionCount} · открыто ${summary.openCount}`}</p>
		</div>
	);
}

/** Кнопка сегмента «Открытые | Все»: активная — акцентная пилюля. */
function ScopeSegmentButton({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
	return (
		<button
			type="button"
			aria-pressed={active}
			onClick={onClick}
			className={cn(
				"cursor-pointer rounded-[7px] px-3 py-1.5 text-xs",
				active ? "bg-accent-soft font-semibold text-accent-strong" : "text-text-secondary hover:text-text-primary",
			)}
		>
			{label}
		</button>
	);
}

/** Шапка раздела: итог журнала с разбивкой, котировки и статусы синхронизации. */
function ConstructionsHeader({
	overview,
	syncMutation,
}: {
	overview: ConstructionsOverview | undefined;
	syncMutation: ReturnType<typeof useMutation<SyncRunSummary, Error, void>>;
}) {
	const summary = overview?.summary;
	// Подпись котировок — сегмент одной строки итога: деградация видима всегда,
	// при норме вместо неё отметка времени марок.
	const quotesNote =
		summary === undefined
			? null
			: summary.hasMarkFailure
				? "котировки: сбой котировок"
				: summary.marksAsOf === null
					? "котировки: нет открытых остатков"
					: `котировки на ${formatMoment(summary.marksAsOf)}`;

	return (
		<header className="flex flex-col gap-2">
			{/* Детальная сводка раздела: итог с разбивкой и отметка времени
			    марок; компактный итог на любом экране живёт в топбаре каркаса
			    и читает тот же кэш обзора. Счётчики конструкций — рядом с
			    титулом тулбара, в строке итога их нет. */}
			{/* Шапка итога — одна строка по мастеру ud07u design.pen (после
			    синхронизации К1): итог 20/600 $accentStrong (J3jIaE), разбивка
			    12.5 $textSecondary (g2EeYx), котировки 11.5 $textMuted (otg5U);
			    паддинги [14,16]; сегменты выравнены по базовой линии и
			    переносятся на узком экране. */}
			{/* Traceability: openspec:ui/screens#scenario-list-summary-shows-pnl-breakdown */}
			{/* Traceability: openspec:ui/design-system#requirement-screenshot-acceptance */}
			{summary === undefined ? (
				<p className="text-muted-foreground text-sm">чтение журнала…</p>
			) : (
				<div data-slot="constructions-summary" className="flex flex-wrap items-baseline gap-x-4 gap-y-1 px-4 py-3.5">
					<p className="text-[20px] font-semibold text-accent-strong">
						{summary.totalPnL === null ? "недоступен" : `${formatSignedAmount(summary.totalPnL)} USDT`}
						{summary.totalPnL === null && summary.hasMarkFailure ? " (неполный: сбой котировок)" : ""}
					</p>
					<p className="text-[12.5px] text-text-secondary">{`реализов. ${formatSignedAmount(summary.realizedPnL)} / нереализов. ${
						summary.unrealizedPnL === null ? "сбой котировок" : formatSignedAmount(summary.unrealizedPnL)
					}`}</p>
					{/* Котировки всегда по полному набору конструкций, независимо
					    от области видимости списка.
					    Traceability: openspec:ui/screens#scenario-list-scope-all-shows-archived */}
					{quotesNote !== null && <p className="text-[11.5px] text-text-muted">{quotesNote}</p>}
				</div>
			)}

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
	rows,
	selectedId,
	onSelect,
	onOpenInNewWindow,
}: {
	overviewQuery: ReturnType<typeof useQuery<ConstructionsOverview>>;
	rows: ConstructionsOverview["items"];
	selectedId: number | null;
	onSelect: (constructionId: number) => void;
	onOpenInNewWindow: (constructionId: number) => void;
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
			<ConstructionsTable rows={rows} selectedId={selectedId} onSelect={onSelect} onOpenInNewWindow={onOpenInNewWindow} />
		</div>
	);
}

/** Краткая карточка выделенной конструкции: read-only превью без команд. */
function SelectedConstructionBrief({
	constructionId,
	liveHintCount,
}: {
	constructionId: number;
	/** Число живых подсказок из строки обзора; без строки обзора сегмент скрывается. */
	liveHintCount?: number;
}) {
	const previewQuery = useQuery({
		queryKey: previewKey(constructionId),
		queryFn: () => fetchConstructionPreview(constructionId),
	});

	if (previewQuery.isPending) {
		return <p className="text-muted-foreground text-sm">чтение конструкции…</p>;
	}

	if (previewQuery.isError) {
		return (
			<p className="text-sm" role="alert">
				Конструкция недоступна: {previewQuery.error.message}
			</p>
		);
	}

	return <ConstructionPreviewCard preview={previewQuery.data} liveHintCount={liveHintCount} />;
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
	// Traceability: openspec:ui/screens#scenario-ui-manual-pass-corpus-error
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
