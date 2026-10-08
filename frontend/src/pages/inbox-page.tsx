import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import * as React from "react";
import { useSearchParams } from "react-router";
import {
	assembleInbox,
	bindInboxTrades,
	createConstructionFromInbox,
	fetchInboxOverview,
	type AssembleInboxResult,
	type InboxTrade,
} from "@/lib/api/inbox";
import { Button } from "@/components/ui/button";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatMoment } from "@/lib/format/display-time";
import { formatSignedAmount } from "@/lib/format/quantity";
import { useIsMobile } from "@/lib/use-mobile";

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

	const selectedCount = selectedExecIds.length;
	const allVisibleSelected = visibleTrades.length > 0 && selectedCount === visibleTrades.length;
	const busy = bindMutation.isPending || createMutation.isPending || assembleMutation.isPending;

	const errorText = bindMutation.error?.message
		?? createMutation.error?.message
		?? assembleMutation.error?.message
		?? null;

	const onToggleAll = (checked: boolean) => {
		if (isMobile) {
			return;
		}

		setSelectedExecIds(checked ? visibleTrades.map((trade) => trade.execId) : []);
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

	const bindToTarget = (constructionId: number, execIds: readonly string[]) => {
		if (execIds.length == 0 || busy) {
			return;
		}

		bindMutation.mutate({ constructionId, execIds });
	};

	const onCreate = () => {
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
	};

	const onDragStart = (trade: InboxTrade) => {
		const ids = selectedExecIds.includes(trade.execId) ? selectedExecIds : [trade.execId];
		setDragExecIds(ids);
	};

	const onDropTarget = (constructionId: number) => {
		setTargetHighlightId(null);
		const ids = dragExecIds.length > 0 ? dragExecIds : selectedExecIds;
		bindToTarget(constructionId, ids);
		setDragExecIds([]);
	};

	return (
		<section className="flex flex-col gap-6 p-6">
			<h1 className="text-2xl font-semibold tracking-tight">Входящие</h1>

			<div className="grid grid-cols-1 gap-4 rounded-lg border p-4 lg:grid-cols-[minmax(0,1fr)_280px]">
				<div className="flex flex-col gap-4">
					<div className="flex flex-wrap items-center gap-3">
						<label className="text-sm">
							с
							<input
								type="date"
								className="ml-2 rounded border px-2 py-1"
								value={fromDate ?? ""}
								onChange={(event) => updateSearchParam("from", event.currentTarget.value)}
							/>
						</label>
						<label className="text-sm">
							по
							<input
								type="date"
								className="ml-2 rounded border px-2 py-1"
								value={toDate ?? ""}
								onChange={(event) => updateSearchParam("to", event.currentTarget.value)}
							/>
						</label>
						<label className="text-sm">
							<input
								type="checkbox"
								checked={selectedSides.has("buy")}
								onChange={(event) => toggleSide("buy", event.currentTarget.checked)}
							/>{" "}
							покупка
						</label>
						<label className="text-sm">
							<input
								type="checkbox"
								checked={selectedSides.has("sell")}
								onChange={(event) => toggleSide("sell", event.currentTarget.checked)}
							/>{" "}
							продажа
						</label>
					</div>

					<div className="flex flex-wrap gap-2">
						{availableSymbols.map((symbol) => (
							<label key={symbol} className="text-sm">
								<input
									type="checkbox"
									checked={selectedSymbols.has(symbol)}
									onChange={(event) => toggleSymbol(symbol, event.currentTarget.checked)}
								/>{" "}
								{symbol}
							</label>
						))}
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

					{visibleTrades.length > 0 && (
						<div className="overflow-x-auto rounded border">
							{/* Плотность Body #5 (TECU5) мастера: шапка и ячейки 8/10,
							    ячейки Inter 12/normal, инструмент — 12.5/500. Таблица
							    непривязанных сделок переведена с сырой HTML-разметки
							    на общий слой таблиц дизайн-системы. */}
							{/* Traceability: change:reconcile-frontend-with-design/design#D6 */}
							{/* Traceability: openspec:ui/design-system#requirement-typography-matches-design-system */}
							<Table density="regular">
								<TableHeader>
									<TableRow>
										<TableHead>
											{isMobile ? "Выбор" : (
												<label>
													<input
														type="checkbox"
														checked={allVisibleSelected}
														onChange={(event) => onToggleAll(event.currentTarget.checked)}
													/>{" "}
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
									</TableRow>
								</TableHeader>
								<TableBody>
									{visibleTrades.map((trade) => (
										<TableRow
											key={trade.execId}
											draggable={isMobile == false}
											onDragStart={() => {
												if (isMobile == false) {
													onDragStart(trade);
												}
											}}
										>
											<TableCell>
												{isMobile ? (
													<span className="text-muted-foreground text-xs">—</span>
												) : (
													<input
														type="checkbox"
														checked={selectedExecIds.includes(trade.execId)}
														onChange={(event) => onToggleRow(trade.execId, event.currentTarget.checked)}
													/>
												)}
											</TableCell>
											<TableCell>{formatMoment(trade.executedAt)}</TableCell>
											<TableCell>{trade.execId}</TableCell>
											{/* Инструмент по мастеру Body #5 — Inter 12.5/500 на textPrimary. */}
											<TableCell>
												<span className="text-[12.5px] font-medium">{trade.symbol}</span>
											</TableCell>
											<TableCell>{trade.isBuy ? "покупка" : "продажа"}</TableCell>
											<TableCell>{formatSignedAmount(trade.quantity)}</TableCell>
											<TableCell>{trade.price}</TableCell>
											<TableCell>{trade.amountUsdt}</TableCell>
											<TableCell>{formatSignedAmount(trade.fee)} {trade.feeCurrency ?? ""}</TableCell>
										</TableRow>
									))}
								</TableBody>
							</Table>
						</div>
					)}
				</div>

				<aside className="flex flex-col gap-3">
					<h2 className="text-base font-semibold">Конструкции-цели</h2>
					{isMobile ? (
						<>
							<p className="text-muted-foreground text-sm">
								мобильный режим: доступен только просмотр входящих; привязка, создание и сборка выполняются на desktop.
							</p>
							<ul className="flex flex-col gap-2">
								{overviewQuery.data?.targets.map((target) => (
									<li key={target.constructionId} className="rounded border p-3">
										<div className="font-medium">{target.name}</div>
										<div className="text-muted-foreground text-xs">{target.status} · итог {target.totalPnL ?? "—"}</div>
									</li>
								))}
							</ul>
						</>
					) : (
						<>
							{overviewQuery.data?.targets.map((target) => (
								<button
									key={target.constructionId}
									type="button"
									onClick={() => bindToTarget(target.constructionId, selectedExecIds)}
									onDragOver={(event) => {
										event.preventDefault();
										setTargetHighlightId(target.constructionId);
									}}
									onDragLeave={() => setTargetHighlightId((current) => (current === target.constructionId ? null : current))}
									onDrop={(event) => {
										event.preventDefault();
										onDropTarget(target.constructionId);
									}}
									disabled={selectedCount == 0 && dragExecIds.length == 0 || busy}
									className={`rounded border p-3 text-left ${targetHighlightId === target.constructionId ? "border-primary bg-primary/5" : ""}`}
								>
									<div className="font-medium">{target.name}</div>
									<div className="text-muted-foreground text-xs">{target.status} · итог {target.totalPnL ?? "—"}</div>
									<div className="text-xs">Привязать {selectedCount > 0 ? selectedCount : dragExecIds.length}</div>
								</button>
							))}

							<div className="mt-2 flex flex-col gap-2 rounded border p-3">
								<label className="text-sm">
									Имя конструкции
									<input
										value={createName}
										onChange={(event) => setCreateName(event.currentTarget.value)}
										className="mt-1 w-full rounded border px-2 py-1"
									/>
								</label>
								<label className="text-sm">
									Капитал (USDT, опционально)
									<input
										value={createCapital}
										onChange={(event) => setCreateCapital(event.currentTarget.value)}
										className="mt-1 w-full rounded border px-2 py-1"
									/>
								</label>
								<Button size="sm" onClick={onCreate} disabled={selectedCount == 0 || createName.trim().length == 0 || busy}>
									Создать конструкцию из выбранного
								</Button>
							</div>

							<Button size="sm" variant="outline" onClick={() => assembleMutation.mutate()} disabled={busy}>
								{assembleMutation.isPending ? "Сборка выполняется…" : "Собрать из Входящих"}
							</Button>
							{assembleMutation.data !== undefined && <AssembleStatus result={assembleMutation.data} />}
						</>
					)}
				</aside>
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
