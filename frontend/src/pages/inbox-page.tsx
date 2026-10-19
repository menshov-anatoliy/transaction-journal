import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowRight, Calendar, MousePointer2, Plus } from "lucide-react";
import * as React from "react";
import { useSearchParams } from "react-router";
import {
	assembleInbox,
	bindInboxTrades,
	createConstructionFromInbox,
	fetchInboxOverview,
	type AssembleInboxResult,
	type InboxTargetConstruction,
	type InboxTrade,
} from "@/lib/api/inbox";
import { createManualTrade, deleteManualTrade, editManualTrade, type ManualTradeInput } from "@/lib/api/manual-trades";
import { ManualTradeForm } from "@/components/trades/manual-trade-form";
import { TradeSourceBadge } from "@/components/trades/trade-source-badge";
import { Dialog, DialogContent, DialogDescription, DialogTitle } from "@/components/ui/dialog";
import { useContextPanel } from "@/components/layout/context-panel";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Input } from "@/components/ui/input";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { DASH } from "@/lib/format/degradation";
import { formatMoment } from "@/lib/format/display-time";
import { formatCount } from "@/lib/format/plural";
import { formatSignedAmount } from "@/lib/format/quantity";
import { useIsMobile } from "@/lib/use-mobile";
import { cn } from "@/lib/utils";

// Раздел «Входящие» по концепции §5: фильтры в URL, выбор непривязанных
// сделок, список конструкций-целей и действия разбора — привязка кнопкой,
// drag&drop, создание конструкции из выбранного и инкрементальная сборка.
// Traceability: doc:.wf-research/ui-concept/concept.md#5-раздел-входящие-страница-разбора-маршрут-inbox
// Переносы №10/№12/№17: фильтры и разбор входящих, сборка из входящих,
// бейдж счётчика в навигации обновляется этим разделом через инвалидaции.
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129
// Мобильный режим ограничен просмотром и лёгкими действиями: тяжёлые
// операции распределения остаются desktop-сценарием.
// Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив
// Дизайн-слой (задача 7.3 change reconcile-frontend-with-design): строка
// фильтров, панель «Конструкции-цели» и чекбоксы перенесены по мастер-нодам
// Body #5 «Входящие» (TECU5) макета design.pen.
// Traceability: change:reconcile-frontend-with-design/design#D2
// Контекстный топбар (решения раунд-сверки 4.0, мастер B1GoG1): список
// «КОНСТРУКЦИИ-ЦЕЛИ» живёт в правой контекстной панели каркаса через
// useContextPanel (чип «Конструкции-цели»), кнопка «Собрать из Входящих» —
// в строке H1 шапки раздела; крошка и заголовок сегмента страницы —
// fallback из appSections (шапка не регистрируется, чипов у сегмента
// страницы нет, как на экранах «Конструкции» и «Карточка конструкции»).
// Traceability: change:rework-contextual-topbar/tasks#4.0
// Traceability: change:rework-contextual-topbar/tasks#4.1

const inboxOverviewKey = ["inbox-overview"] as const;
const inboxCountKey = ["inbox-count"] as const;
const constructionsOverviewKey = ["constructions-overview"] as const;

export function InboxPage() {
	const isMobile = useIsMobile();
	const [searchParams, setSearchParams] = useSearchParams();
	const queryClient = useQueryClient();
	const [selectedExecIds, setSelectedExecIds] = React.useState<string[]>([]);
	const [targetHighlightId, setTargetHighlightId] = React.useState<number | null>(null);
	const [dragExecIds, setDragExecIds] = React.useState<string[]>([]);
	const [createName, setCreateName] = React.useState("");
	const [createCapital, setCreateCapital] = React.useState("");
	// Форма ручной сделки по мастеру XJapZ: создание из «Входящих» без
	// конструкции, правка адресуется ключом строки сырья записи.
	// Traceability: openspec:ui/screens#scenario-inbox-create-manual-trade
	const [manualFormOpen, setManualFormOpen] = React.useState(false);
	const [editingTrade, setEditingTrade] = React.useState<InboxTrade | null>(null);
	const [deleteTarget, setDeleteTarget] = React.useState<InboxTrade | null>(null);

	const overviewQuery = useQuery({ queryKey: inboxOverviewKey, queryFn: fetchInboxOverview });

	React.useEffect(() => {
		if (searchParams.has("from") || searchParams.has("to") || searchParams.has("sides") || searchParams.has("symbols")) {
			return;
		}

		const now = new Date();
		const to = formatDateInput(now);
		const fromDate = new Date(now);
		fromDate.setMonth(now.getMonth() - 1);
		const from = formatDateInput(fromDate);
		const next = new URLSearchParams(searchParams);
		next.set("from", from);
		next.set("to", to);
		next.set("sides", "buy,sell");
		setSearchParams(next, { replace: true });
	}, [searchParams, setSearchParams]);

	const availableSymbols = React.useMemo(
		() => [...new Set((overviewQuery.data?.items ?? []).map((trade) => trade.symbol))].sort((left, right) => left.localeCompare(right)),
		[overviewQuery.data?.items],
	);

	const selectedSymbols = React.useMemo(() => {
		const value = searchParams.get("symbols");
		if (value == null || value.trim().length == 0) {
			return new Set(availableSymbols);
		}

		return new Set(value.split(",").filter((symbol) => symbol.trim().length > 0));
	}, [availableSymbols, searchParams]);

	const selectedSides = React.useMemo(() => {
		const raw = searchParams.get("sides") ?? "buy,sell";
		const parts = raw.split(",").map((part) => part.trim()).filter((part) => part.length > 0);
		return new Set(parts);
	}, [searchParams]);

	const fromDate = searchParams.get("from");
	const toDate = searchParams.get("to");

	const visibleTrades = React.useMemo(() => {
		const items = overviewQuery.data?.items ?? [];
		return items.filter((trade) => {
			if (selectedSymbols.has(trade.symbol) == false) {
				return false;
			}

			if (trade.isBuy && selectedSides.has("buy") == false) {
				return false;
			}

			if (trade.isBuy == false && selectedSides.has("sell") == false) {
				return false;
			}

			const day = trade.executedAt.slice(0, 10);
			if (fromDate != null && fromDate.length > 0 && day < fromDate) {
				return false;
			}

			if (toDate != null && toDate.length > 0 && day > toDate) {
				return false;
			}

			return true;
		});
	}, [overviewQuery.data?.items, selectedSymbols, selectedSides, fromDate, toDate]);

	React.useEffect(() => {
		setSelectedExecIds((current) => current.filter((execId) => visibleTrades.some((trade) => trade.execId === execId)));
	}, [visibleTrades]);

	const bindMutation = useMutation({
		mutationFn: (request: { constructionId: number; execIds: readonly string[] }) =>
			bindInboxTrades(request.constructionId, request.execIds),
		onSuccess: async () => {
			setSelectedExecIds([]);
			await invalidateAfterMutation(queryClient);
		},
	});

	const createMutation = useMutation({
		mutationFn: createConstructionFromInbox,
		onSuccess: async () => {
			setSelectedExecIds([]);
			setCreateName("");
			setCreateCapital("");
			await invalidateAfterMutation(queryClient);
		},
	});

	const assembleMutation = useMutation({
		mutationFn: assembleInbox,
		onSuccess: async () => {
			setSelectedExecIds([]);
			await invalidateAfterMutation(queryClient);
		},
	});

	// Ручные сделки: внесение в общий пул «Входящих», правка атрибутов и
	// удаление по ключу строки сырья; ошибки валидации приходят от сервера
	// и показываются у формы/действия.
	// Traceability: openspec:ui/screens#requirement-manual-trade-creation-ui
	// Traceability: openspec:ui/screens#scenario-detail-edit-manual-trade
	const manualCreateMutation = useMutation({
		mutationFn: (input: ManualTradeInput) => createManualTrade({ ...input, constructionId: null }),
		onSuccess: async () => {
			setManualFormOpen(false);
			await invalidateAfterMutation(queryClient);
		},
	});
	const manualEditMutation = useMutation({
		mutationFn: (input: { id: number; trade: ManualTradeInput }) => editManualTrade(input.id, input.trade),
		onSuccess: async () => {
			setEditingTrade(null);
			await invalidateAfterMutation(queryClient);
		},
	});
	const manualDeleteMutation = useMutation({
		mutationFn: (id: number) => deleteManualTrade(id),
		onSuccess: async () => {
			setDeleteTarget(null);
			await invalidateAfterMutation(queryClient);
		},
	});
	const manualTradeBusy = manualCreateMutation.isPending || manualEditMutation.isPending || manualDeleteMutation.isPending;
	const manualTradeError = manualCreateMutation.error?.message
		?? manualEditMutation.error?.message
		?? manualDeleteMutation.error?.message
		?? null;

	const selectedCount = selectedExecIds.length;
	// Распределение адресуется execId биржевых записей: ручные записи в
	// привязке не участвуют и в «выбрать всё» не попадают.
	const selectableTrades = React.useMemo(
		() => visibleTrades.filter((trade) => trade.execId !== null),
		[visibleTrades],
	);
	const allVisibleSelected = selectableTrades.length > 0 && selectedCount === selectableTrades.length;
	const busy = bindMutation.isPending || createMutation.isPending || assembleMutation.isPending;

	// Суммарный объём выбранных сделок — правая подпись блока выбранного
	// мастера (g7OSCw: «+21 530 объём»), формат доменный, без дробной части.
	const selectedVolume = React.useMemo(
		() =>
			visibleTrades
				.filter((trade) => trade.execId !== null && selectedExecIds.includes(trade.execId))
				.reduce((sum, trade) => sum + trade.amountUsdt, 0),
		[visibleTrades, selectedExecIds],
	);

	const errorText = bindMutation.error?.message
		?? createMutation.error?.message
		?? assembleMutation.error?.message
		?? null;

	const onToggleAll = (checked: boolean) => {
		if (isMobile) {
			return;
		}

		setSelectedExecIds(checked ? selectableTrades.map((trade) => trade.execId!) : []);
	};

	const onToggleRow = (execId: string, checked: boolean) => {
		if (isMobile) {
			return;
		}

		setSelectedExecIds((current) => {
			if (checked) {
				return current.includes(execId) ? current : [...current, execId];
			}

			return current.filter((candidate) => candidate !== execId);
		});
	};

	const updateSearchParam = (key: string, value: string | null) => {
		const next = new URLSearchParams(searchParams);
		if (value == null || value.length == 0) {
			next.delete(key);
		} else {
			next.set(key, value);
		}
		setSearchParams(next, { replace: true });
	};

	const toggleSymbol = (symbol: string, checked: boolean) => {
		const next = new Set(selectedSymbols);
		if (checked) {
			next.add(symbol);
		} else {
			next.delete(symbol);
		}

		updateSearchParam("symbols", [...next].join(","));
	};

	const toggleSide = (side: "buy" | "sell", checked: boolean) => {
		const next = new Set(selectedSides);
		if (checked) {
			next.add(side);
		} else {
			next.delete(side);
		}

		updateSearchParam("sides", [...next].join(","));
	};

	// «Сбросить» (Ghost-кнопка b0ZLfV мастера) возвращает фильтры к окну по
	// умолчанию: очистка параметров запускает эффект дефолтного диапазона
	// (месяц назад → сегодня, обе стороны, все инструменты).
	const onResetFilters = () => {
		setSearchParams(new URLSearchParams(), { replace: true });
	};

	// Обработчики целей и формы создания завёрнуты в useCallback: они входят
	// в memo-стабильную регистрацию контекстной панели (контракт useContextPanel),
	// поэтому их идентичность должна меняться только при смене зависимостей.
	const bindToTarget = React.useCallback(
		(constructionId: number, execIds: readonly string[]) => {
			if (execIds.length == 0 || busy) {
				return;
			}

			bindMutation.mutate({ constructionId, execIds });
		},
		[bindMutation.mutate, busy],
	);

	const onCreate = React.useCallback(() => {
		const name = createName.trim();
		if (name.length == 0 || selectedExecIds.length == 0 || busy) {
			return;
		}

		const capitalValue = createCapital.trim().length == 0 ? null : Number(createCapital);
		if (capitalValue != null && Number.isFinite(capitalValue) == false) {
			return;
		}

		createMutation.mutate({
			name,
			allocatedCapitalUsdt: capitalValue,
			execIds: selectedExecIds,
		});
	}, [createName, createCapital, selectedExecIds, busy, createMutation.mutate]);

	const onDragStart = React.useCallback(
		(trade: InboxTrade) => {
			if (trade.execId === null) {
				return;
			}

			const ids = selectedExecIds.includes(trade.execId) ? selectedExecIds : [trade.execId];
			setDragExecIds(ids);
		},
		[selectedExecIds],
	);

	const onDropTarget = React.useCallback(
		(constructionId: number) => {
			setTargetHighlightId(null);
			const ids = dragExecIds.length > 0 ? dragExecIds : selectedExecIds;
			bindToTarget(constructionId, ids);
			setDragExecIds([]);
		},
		[bindToTarget, dragExecIds, selectedExecIds],
	);

	// Панель «Конструкции-цели» переносится из тела страницы в правую
	// контекстную панель каркаса (решение раунд-сверки 4.0, мастер B1GoG1):
	// пилюли целей с drag&drop, подсказка «Отпустите…» и блок «Выбрано: N»
	// с формой создания конструкции регистрируются слотом useContextPanel;
	// чип сегмента контекста — «Конструкции-цели», чип мастера «Карточка ·
	// BTC-280324» отклонён как нестыковка мастера (design.md D9). На мобильном
	// регистрация не выполняется: контекстная панель каркаса скрыта, а
	// drag-функциональность на мобильный не переносится (Mobile — стадия
	// 10.x сохраняет текущий просмотр целей в теле страницы).
	// Traceability: openspec:ui/screens#scenario-frame-context-chip-shows-registered-content
	// Traceability: openspec:ui/screens#requirement-inbox-screen
	// Traceability: change:rework-contextual-topbar/tasks#4.0
	const goalsPanelRegistration = React.useMemo(
		() =>
			isMobile
				? null
				: {
						chipLabel: "Конструкции-цели",
						content: (
							<GoalsTargetsPanel
								targets={overviewQuery.data?.targets}
								selectedExecIds={selectedExecIds}
								selectedVolume={selectedVolume}
								busy={busy}
								createName={createName}
								onCreateNameChange={setCreateName}
								createCapital={createCapital}
								onCreateCapitalChange={setCreateCapital}
								onCreate={onCreate}
								bindToTarget={bindToTarget}
								targetHighlightId={targetHighlightId}
								onTargetHighlightChange={setTargetHighlightId}
								onDropTarget={onDropTarget}
								dragExecIds={dragExecIds}
							/>
						),
					},
		[
			isMobile,
			overviewQuery.data?.targets,
			selectedExecIds,
			selectedVolume,
			busy,
			createName,
			createCapital,
			onCreate,
			bindToTarget,
			targetHighlightId,
			onDropTarget,
			dragExecIds,
		],
	);
	useContextPanel(goalsPanelRegistration);

	return (
		<section className="flex flex-col gap-6 p-6">
			{/* Крошка и заголовок сегмента страницы — fallback из appSections:
			    шапка раздела не регистрируется через usePageHeader, чипов у
			    сегмента страницы нет (решение раунд-сверки 4.0, прецедент
			    экранов «Конструкции» и «Карточка конструкции»).
			    Traceability: openspec:ui/screens#scenario-frame-page-header-registration
			    Traceability: change:rework-contextual-topbar/tasks#4.0 */}
			<div className="flex flex-wrap items-center gap-3">
				<h1 className="page-title">Входящие</h1>
				{overviewQuery.data !== undefined && (
					// Счётчик шапки мастера (tLN45): Inter 12.5/normal $textSecondary.
					// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
					// Traceability: change:reconcile-frontend-with-design/design#D2
					<span className="text-[12.5px] text-text-secondary">
						{formatCount(overviewQuery.data.items.length, {
							one: "непривязанная сделка",
							few: "непривязанные сделки",
							many: "непривязанных сделок",
						})}
					</span>
				)}
				{isMobile == false && (
					// «Собрать из Входящих» — secondary-кнопка в строке H1 шапки
					// раздела, справа от счётчика (решение раунд-сверки 4.0, как
					// «Синхронизировать» на «Конструкциях»); из панели целей
					// кнопка убрана. Инкрементальная сборка запускается без
					// диалога подтверждения.
					// Traceability: openspec:ui/screens#scenario-inbox-assembly-without-confirmation
					// Traceability: change:rework-contextual-topbar/tasks#4.0
					<Button variant="secondary" size="sm" disabled={busy} onClick={() => assembleMutation.mutate()}>
						{assembleMutation.isPending ? "Сборка выполняется…" : "Собрать из Входящих"}
					</Button>
				)}
				{/* «Ручная сделка» — outline-кнопка мастера «Кнопка:Ручная сделка»
				    шапки «Входящих» (tSfT8): иконка plus 12 $textSecondary и подпись
				    11.5/500 $textSecondary; слева от «Собрать из Входящих». */}
				{/* Traceability: openspec:ui/screens#requirement-manual-trade-creation-ui */}
				<Button
					variant="outline"
					size="sm"
					className="h-auto gap-1.5 px-2.5 py-[5px] text-[11.5px]"
					disabled={manualTradeBusy}
					onClick={() => setManualFormOpen(true)}
				>
					<Plus aria-hidden className="size-3" />
					Ручная сделка
				</Button>
			</div>

			{/* Форма ручной сделки по мастеру XJapZ: создание кладёт запись
			    в общий пул «Входящих», правка предзаполняет атрибуты записи;
			    ошибки серверной валидации показывает сама форма. */}
			{/* Traceability: openspec:ui/screens#requirement-manual-trade-creation-ui */}
			{manualFormOpen && (
				<ManualTradeForm
					target={{ constructionName: null }}
					submitLabel="Создать"
					pending={manualTradeBusy}
					error={manualTradeError}
					onSubmit={(input) => manualCreateMutation.mutate(input)}
					onCancel={() => setManualFormOpen(false)}
				/>
			)}
			{editingTrade !== null && editingTrade.manualTradeId !== null && (
				<ManualTradeForm
					target={{ constructionName: null }}
					submitLabel="Сохранить"
					pending={manualTradeBusy}
					error={manualTradeError}
					initialValues={{
						executedAt: toLocalInputValue(editingTrade.executedAt),
						symbol: editingTrade.symbol,
						side: editingTrade.isBuy ? "Buy" : "Sell",
						quantity: String(Math.abs(editingTrade.quantity)),
						price: String(editingTrade.price),
						feeUsdt: String(editingTrade.fee),
					}}
					onSubmit={(input) => manualEditMutation.mutate({ id: editingTrade.manualTradeId!, trade: input })}
					onCancel={() => setEditingTrade(null)}
				/>
			)}

			{/* Подтверждение удаления ручной сделки: необратимое действие с
			    явным вопросом по действующему паттерну диалогов. */}
			{/* Traceability: openspec:ui/screens#requirement-manual-trade-creation-ui */}
			<Dialog
				open={deleteTarget !== null}
				onOpenChange={(value) => {
					if (value === false && manualDeleteMutation.isPending == false) {
						setDeleteTarget(null);
					}
				}}
			>
				<DialogContent showCloseButton={manualDeleteMutation.isPending == false}>
					<DialogTitle>Удалить ручную сделку?</DialogTitle>
					<DialogDescription>
						Ручная сделка «{deleteTarget?.symbol ?? ""}» будет удалена из журнала. Действие нельзя отменить.
					</DialogDescription>
					<div className="flex gap-2">
						<Button
							variant="destructive"
							size="sm"
							disabled={manualDeleteMutation.isPending}
							onClick={() => {
								if (deleteTarget?.manualTradeId != null) {
									manualDeleteMutation.mutate(deleteTarget.manualTradeId);
								}
							}}
						>
							Подтвердить удаление
						</Button>
						<Button variant="outline" size="sm" disabled={manualDeleteMutation.isPending} onClick={() => setDeleteTarget(null)}>
							Отмена
						</Button>
					</div>
					{manualDeleteMutation.error !== null && (
						<p className="text-destructive text-sm" role="alert">
							Сделка не удалена: {manualDeleteMutation.error.message}
						</p>
					)}
				</DialogContent>
			</Dialog>

			{/* Итог последней сборки виден под действием, которое её запустило. */}
			{assembleMutation.data !== undefined && <AssembleStatus result={assembleMutation.data} />}

			{/*
				Строка фильтров перенесена с дизайн-ноды «Фильтры» (ZvdbR) Body #5:
				поля дат с иконкой календаря (VYItk/euvt0, [7,10], радиус 8,
				Inter 12/normal), группа инструментов в рамке поля (AYWJR),
				группа направления без рамки (qoUNa) и Ghost-кнопка «Сбросить»
				(b0ZLfV) — нативные input type=date/checkbox заменены
				примитивами-полями и чекбоксами дизайн-системы.
			*/}
			{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
			{/* Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens */}
			{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
			<div className="flex flex-wrap items-center gap-2.5">
				<DateField label="Дата с" value={fromDate ?? ""} onChange={(value) => updateSearchParam("from", value)} />
				<span aria-hidden="true" className="text-xs text-text-muted">—</span>
				<DateField label="Дата по" value={toDate ?? ""} onChange={(value) => updateSearchParam("to", value)} />

				<div className="flex flex-wrap items-center gap-x-3 gap-y-1.5 rounded-sm border bg-card px-2.5 py-[7px]">
					<span className="text-xs text-text-secondary">Инструменты:</span>
					{availableSymbols.map((symbol) => (
						<label key={symbol} className="flex cursor-pointer items-center gap-1.5 text-xs text-foreground">
							<Checkbox
								checked={selectedSymbols.has(symbol)}
								onChange={(event) => toggleSymbol(symbol, event.currentTarget.checked)}
							/>
							{symbol}
						</label>
					))}
				</div>

				<div className="flex flex-wrap items-center gap-3 px-2.5 py-[7px]">
					<span className="text-xs text-text-secondary">Направление:</span>
					<label className="flex cursor-pointer items-center gap-1.5 text-xs text-foreground">
						<Checkbox
							checked={selectedSides.has("buy")}
							onChange={(event) => toggleSide("buy", event.currentTarget.checked)}
						/>
						покупка
					</label>
					<label className="flex cursor-pointer items-center gap-1.5 text-xs text-foreground">
						<Checkbox
							checked={selectedSides.has("sell")}
							onChange={(event) => toggleSide("sell", event.currentTarget.checked)}
						/>
						продажа
					</label>
				</div>

				<Button variant="ghost" className="h-auto px-2.5 py-1.5 text-xs" onClick={onResetFilters}>
					Сбросить
				</Button>
			</div>

			{overviewQuery.isPending && <p className="text-muted-foreground text-sm">чтение входящих…</p>}
			{overviewQuery.isError && (
				<p className="text-destructive text-sm" role="alert">
					Входящие недоступны: {overviewQuery.error.message}
				</p>
			)}

			{overviewQuery.data !== undefined && overviewQuery.data.items.length == 0 && (
				<p className="text-muted-foreground text-sm">Входящие пусты: непривязанных сделок нет.</p>
			)}
			{overviewQuery.data !== undefined && overviewQuery.data.items.length > 0 && visibleTrades.length == 0 && (
				<p className="text-muted-foreground text-sm">По заданным фильтрам ничего не найдено.</p>
			)}

			{/* Main мастера (lFPDa, макет B1GoG1): таблица непривязанных сделок —
			    карточка радиуса 12; панель целей из прежнего grid-ряда перенесена
			    в контекстную панель каркаса (решение раунд-сверки 4.0), поэтому
			    таблица занимает всю ширину центральной зоны. На мобильном
			    просмотр целей остаётся в теле страницы (стадия Mobile 10.x).
			    Traceability: change:rework-contextual-topbar/tasks#4.0 */}
			<div className="flex flex-col gap-5">
				<div className="min-w-0">
					{visibleTrades.length > 0 && (
						<div className="overflow-hidden rounded-lg border bg-card">
							{/* Плотность Body #5 (TECU5) мастера: шапка и ячейки 8/10,
							    ячейки Inter 12/normal, инструмент — 12.5/500. Карточка
							    таблицы — радиус 12, кайма $border; шапка залита
							    $surface2, разделители строк — $divider, выбранные
							    строки — $accentSofter. Таблица непривязанных сделок
							    переведена с сырой HTML-разметки на общий слой таблиц. */}
							{/* Traceability: change:reconcile-frontend-with-design/design#D6 */}
							{/* Traceability: openspec:ui/design-system#requirement-typography-matches-design-system */}
							{/* Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens */}
							<Table density="regular">
								<TableHeader className="bg-surface-2 [&_tr]:border-divider">
									<TableRow>
										<TableHead>
											{isMobile ? "Выбор" : (
												<label className="flex cursor-pointer items-center gap-1.5">
													<Checkbox
														checked={allVisibleSelected}
														onChange={(event) => onToggleAll(event.currentTarget.checked)}
													/>
													Выбрать всё
												</label>
											)}
										</TableHead>
										<TableHead>Время</TableHead>
										<TableHead>execId</TableHead>
										<TableHead>Инструмент</TableHead>
										<TableHead>Направление</TableHead>
										<TableHead>Количество</TableHead>
										<TableHead>Цена</TableHead>
										<TableHead>Сумма</TableHead>
										<TableHead>Комиссия</TableHead>
										<TableHead>Действия</TableHead>
									</TableRow>
								</TableHeader>
								<TableBody>
									{visibleTrades.map((trade) => {
										// Привязка адресуется execId: ручная запись не выбирается
										// и не перетаскивается — её действия в ячейке «Действия».
										const rowSelected = trade.execId !== null && selectedExecIds.includes(trade.execId);
										return (
											<TableRow
												key={trade.execId ?? `manual:${trade.manualTradeId}`}
												draggable={isMobile == false && trade.execId !== null}
												onDragStart={() => {
													if (isMobile == false) {
														onDragStart(trade);
													}
												}}
												data-state={rowSelected ? "selected" : undefined}
												className="border-divider data-[state=selected]:bg-accent-softer"
											>
												<TableCell>
													{isMobile || trade.execId === null ? (
														<span className="text-muted-foreground text-xs">—</span>
													) : (
														<Checkbox
															aria-label={`Выбрать сделку ${trade.execId}`}
															checked={rowSelected}
															onChange={(event) => onToggleRow(trade.execId!, event.currentTarget.checked)}
														/>
													)}
												</TableCell>
												<TableCell className="text-text-secondary">{formatMoment(trade.executedAt)}</TableCell>
												{/* execId адресует биржевую запись; у ручной его нет — прочерк. */}
												<TableCell className="text-text-secondary">{trade.execId ?? DASH}</TableCell>
												{/* Инструмент по мастеру Body #5 — Inter 12.5/500 на textPrimary,
												    рядом бейдж источника записи («Инстр-ряд» мастера). */}
												{/* Traceability: openspec:ui/screens#scenario-source-badge-shown */}
												<TableCell>
													<span className="flex items-center gap-1.5">
														<span className="text-[12.5px] font-medium">{trade.symbol}</span>
														<TradeSourceBadge source={trade.source} />
													</span>
												</TableCell>
												{/* Направление мастера тонировано: покупка — $info,
												    продажа — $risk (textFills Body #5: info ×4, risk ×1). */}
												{/* Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens */}
												{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
												<TableCell>
													<span className={trade.isBuy ? "text-info" : "text-risk"}>
														{trade.isBuy ? "покупка" : "продажа"}
													</span>
												</TableCell>
												<TableCell className="text-text-secondary">{formatSignedAmount(trade.quantity)}</TableCell>
												<TableCell className="text-text-secondary">{trade.price}</TableCell>
												<TableCell className="text-text-secondary">{trade.amountUsdt}</TableCell>
												<TableCell className="text-text-secondary">{formatSignedAmount(trade.fee)} {trade.feeCurrency ?? ""}</TableCell>
												<TableCell>
													{/* Правка и удаление ручной записи — desktop-действие;
													    у биржевой записи этих действий нет (неизменяемость). */}
													{/* Traceability: openspec:ui/screens#scenario-detail-edit-manual-trade */}
													{/* Traceability: openspec:ui/screens#scenario-detail-synced-trade-immutable */}
													{isMobile == false && (
														trade.source === "manual" ? (
															<div className="flex gap-1.5">
																<Button
																	variant="outline"
																	size="sm"
																	className="h-auto rounded-[6px] px-[7px] py-[3px] text-[9.5px] text-text-secondary"
																	disabled={manualTradeBusy}
																	onClick={() => setEditingTrade(trade)}
																>
																	Изменить
																</Button>
																<Button
																	variant="outline"
																	size="sm"
																	className="h-auto rounded-[6px] px-[7px] py-[3px] text-[9.5px] text-text-secondary"
																	disabled={manualTradeBusy}
																	onClick={() => setDeleteTarget(trade)}
																>
																	<span className="text-neg">Удалить</span>
																</Button>
															</div>
														) : (
															<span className="text-muted-foreground text-xs">{DASH}</span>
														)
													)}
												</TableCell>
											</TableRow>
										);
									})}
								</TableBody>
							</Table>
						</div>
					)}
				</div>

				{/* На мобильном просмотр целей остаётся в теле страницы:
				    карточка-список мастера P9U8QH без перетаскивания; тяжёлые
				    операции распределения — desktop-сценарий. Поведение Mobile
				    (стадия 10.x) сохранено решением раунд-сверки 4.0.
				    Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив
				    Traceability: change:rework-contextual-topbar/tasks#4.0 */}
				{isMobile && (
					<aside className="flex flex-col gap-2.5 rounded-lg border bg-card p-3.5">
						<h2 className="text-[10px] font-normal tracking-[0.5px] text-text-muted">КОНСТРУКЦИИ-ЦЕЛИ</h2>
						<p className="text-muted-foreground text-sm">
							мобильный режим: доступен только просмотр входящих; привязка, создание и сборка выполняются на desktop.
						</p>
						<ul className="flex flex-col gap-2.5">
							{overviewQuery.data?.targets.map((target) => (
								<li key={target.constructionId} className="rounded-[9px] border px-[11px] py-[9px]">
									<div className="text-[12.5px] font-medium">{target.name}</div>
									<div className="text-[11px] text-text-muted">
										{targetStatusLabel(target.status)} · итог {target.totalPnL === null ? "—" : formatSignedAmount(target.totalPnL)}
									</div>
								</li>
							))}
						</ul>
					</aside>
				)}
			</div>

			{errorText !== null && (
				<p className="text-destructive text-sm" role="alert">
					{errorText}
				</p>
			)}
		</section>
	);
}

function AssembleStatus({ result }: { result: AssembleInboxResult }) {
	return (
		<p className="text-sm" role="status">
			Сборка завершена: создано конструкций {result.constructionsCount}, привязано сделок {result.boundCount}, осталось во Входящих{" "}
			{result.tradesInInbox}.
		</p>
	);
}

/** Пропсы десктопной панели «Конструкции-цели» контекстной панели каркаса. */
interface GoalsTargetsPanelProps {
	/** Конструкции-цели из обзора входящих; при загрузке — undefined. */
	readonly targets: readonly InboxTargetConstruction[] | undefined;
	/** Выбранные сделки — управляют доступностью привязки и блока выбранного. */
	readonly selectedExecIds: readonly string[];
	/** Суммарный объём выбранных сделок для блока выбранного мастера. */
	readonly selectedVolume: number;
	/** Общая занятость мутаций разбора — блокирует действия панели. */
	readonly busy: boolean;
	readonly createName: string;
	readonly onCreateNameChange: (value: string) => void;
	readonly createCapital: string;
	readonly onCreateCapitalChange: (value: string) => void;
	readonly onCreate: () => void;
	readonly bindToTarget: (constructionId: number, execIds: readonly string[]) => void;
	readonly targetHighlightId: number | null;
	readonly onTargetHighlightChange: React.Dispatch<React.SetStateAction<number | null>>;
	readonly onDropTarget: (constructionId: number) => void;
	readonly dragExecIds: readonly string[];
}

/**
 * Панель «Конструкции-цели» рабочего стола: пилюли целей с привязкой
 * кнопкой и drag&drop, подсказка сброса перетаскивания и блок выбранного
 * с формой создания конструкции. Монтируется страницей в слот правой
 * контекстной панели каркаса (useContextPanel): хром карточки задаёт
 * панель каркаса, компонент рисует только содержимое.
 *
 * Traceability: openspec:ui/screens#scenario-frame-context-chip-shows-registered-content
 * Traceability: change:rework-contextual-topbar/tasks#4.0
 */
function GoalsTargetsPanel({
	targets,
	selectedExecIds,
	selectedVolume,
	busy,
	createName,
	onCreateNameChange,
	createCapital,
	onCreateCapitalChange,
	onCreate,
	bindToTarget,
	targetHighlightId,
	onTargetHighlightChange,
	onDropTarget,
	dragExecIds,
}: GoalsTargetsPanelProps) {
	const selectedCount = selectedExecIds.length;

	return (
		<div className="flex flex-col gap-2.5">
			{/* Заголовок-подпись панели мастера Body #5 (m9kwpl): Inter 10,
			    letterSpacing 0.5, $textMuted; хром карточки P9U8QH задаёт
			    панель каркаса. */}
			<h2 className="text-[10px] font-normal tracking-[0.5px] text-text-muted">КОНСТРУКЦИИ-ЦЕЛИ</h2>
			{/*
				Пилюли-цели (VZPxZ и родственные): радиус 9, паддинги [9,11],
				имя 12.5/500, мета 11 $textMuted, стрелка arrow-right 14;
				подсветка перетаскивания (jMnKo) — заливка $accentSofter с
				каймой $accent 1.5 и стрелкой $accentStrong, индиго-подсветки нет.
			*/}
			{/* Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens */}
			{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
			{/* Traceability: change:reconcile-frontend-with-design/design#D2 */}
			{targets?.map((target) => {
				const highlighted = targetHighlightId === target.constructionId;
				return (
					<button
						key={target.constructionId}
						type="button"
						onClick={() => bindToTarget(target.constructionId, selectedExecIds)}
						onDragOver={(event) => {
							event.preventDefault();
							onTargetHighlightChange(target.constructionId);
						}}
						onDragLeave={() => onTargetHighlightChange((current) => (current === target.constructionId ? null : current))}
						onDrop={(event) => {
							event.preventDefault();
							onDropTarget(target.constructionId);
						}}
						disabled={selectedCount == 0 && dragExecIds.length == 0 || busy}
						className={cn(
							"flex w-full items-center gap-2 rounded-[9px] border px-[11px] py-[9px] text-left",
							highlighted ? "border-primary border-[1.5px] bg-accent-softer" : "bg-card",
						)}
					>
						<span className="flex min-w-0 flex-1 flex-col gap-0.5">
							<span className="truncate text-[12.5px] font-medium text-foreground">{target.name}</span>
							<span className="text-[11px] text-text-muted">
								{targetStatusLabel(target.status)} · итог {target.totalPnL === null ? "—" : formatSignedAmount(target.totalPnL)}
							</span>
						</span>
						<ArrowRight
							aria-hidden="true"
							className={cn("size-3.5 shrink-0", highlighted ? "text-accent-strong" : "text-text-muted")}
						/>
					</button>
				);
			})}

			{/* Подсказка сброса перетаскивания (JkO8D): заливка
			    $accentSofter, подпись и курсор — $accentStrong. */}
			{dragExecIds.length > 0 && (
				<div className="flex items-center gap-2 rounded-[9px] bg-accent-softer px-[11px] py-2">
					<MousePointer2 aria-hidden="true" className="size-[13px] shrink-0 text-accent-strong" />
					<span className="text-[11.5px] text-accent-strong">
						Отпустите, чтобы привязать {formatCount(dragExecIds.length, bindPluralForms)}
					</span>
				</div>
			)}

			{/* Блок выбранного (g7OSCw): «Выбрано: N …» Inter 12/600,
			    суммарный объём 11 $textMuted; доменная форма создания
			    конструкции — на примитивах-полях дизайн-системы. */}
			<div className="flex flex-col gap-2 pt-2.5">
				<div className="flex items-center gap-2">
					<span className="text-xs font-semibold text-foreground">Выбрано: {formatCount(selectedCount, dealPluralForms)}</span>
					<span className="h-px flex-1" />
					<span className="text-[11px] text-text-muted">{formatSignedAmount(selectedVolume, 0)} объём</span>
				</div>
				<div className="flex flex-col gap-2">
					<label className="flex flex-col gap-1 text-xs text-text-secondary">
						Имя конструкции
						<Input value={createName} onChange={(event) => onCreateNameChange(event.currentTarget.value)} />
					</label>
					<label className="flex flex-col gap-1 text-xs text-text-secondary">
						Капитал (USDT, опционально)
						<Input value={createCapital} onChange={(event) => onCreateCapitalChange(event.currentTarget.value)} />
					</label>
					<Button
						variant="ghost"
						className="h-auto w-full justify-center border px-2.5 py-[7px] text-[11.5px]"
						onClick={onCreate}
						disabled={selectedCount == 0 || createName.trim().length == 0 || busy}
					>
						Создать конструкцию из выбранного
					</Button>
				</div>
			</div>
		</div>
	);
}

async function invalidateAfterMutation(queryClient: ReturnType<typeof useQueryClient>) {
	await Promise.all([
		queryClient.invalidateQueries({ queryKey: inboxOverviewKey }),
		queryClient.invalidateQueries({ queryKey: inboxCountKey }),
		queryClient.invalidateQueries({ queryKey: constructionsOverviewKey }),
	]);
}

function formatDateInput(value: Date) {
	return value.toISOString().slice(0, 10);
}

/** Переводит ISO-момент в значение поля datetime-local в зоне браузера. */
function toLocalInputValue(iso: string): string {
	const date = new Date(iso);
	const pad = (value: number) => String(value).padStart(2, "0");
	return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

/** Формы числительного для строк «Выбрано: N …» блока выбранного мастера. */
const dealPluralForms = { one: "сделка", few: "сделки", many: "сделок" } as const;

/** Формы числительного винительного падежа для «привязать N …» подсказки сброса. */
const bindPluralForms = { one: "сделку", few: "сделки", many: "сделок" } as const;

/** Статус конструкции-цели словом строки (как statusText списка конструкций). */
function targetStatusLabel(status: InboxTargetConstruction["status"]): string {
	switch (status) {
		case "open":
			return "открыта";
		case "closed":
			return "закрыта";
		case "archived":
			return "архив";
	}
}

/*
	Поле даты фильтра — инстанс мастер-нод DateFrom/DateTo (VYItk/euvt0):
	примитив-поле дизайн-системы с иконкой календаря 13×13 $textMuted (узел
	Cal) слева от значения. Нативный индикатор календаря скрыт: его роль
	берёт иконка мастера, выбор даты — по клику поля.
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2
function DateField({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
	return (
		<label className="relative inline-flex cursor-pointer items-center">
			<Calendar aria-hidden="true" className="pointer-events-none absolute left-2.5 size-[13px] text-text-muted" />
			<Input
				type="date"
				aria-label={label}
				value={value}
				onChange={(event) => onChange(event.currentTarget.value)}
				className="pl-[31px] scheme-light [&::-webkit-calendar-picker-indicator]:hidden"
			/>
		</label>
	);
}
