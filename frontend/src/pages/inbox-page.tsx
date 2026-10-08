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
import { formatMoment } from "@/lib/format/display-time";
import { formatSignedAmount } from "@/lib/format/quantity";

// Раздел «Входящие» по концепции §5: фильтры в URL, выбор непривязанных
// сделок, список конструкций-целей и действия разбора — привязка кнопкой,
// drag&drop, создание конструкции из выбранного и инкрементальная сборка.
// Traceability: doc:.wf-research/ui-concept/concept.md#5-раздел-входящие-страница-разбора-маршрут-inbox
// Переносы №10/№12/№17: фильтры и разбор входящих, сборка из входящих,
// бейдж счётчика в навигации обновляется этим разделом через инвалидaции.
// Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129

const inboxOverviewKey = ["inbox-overview"] as const;
const inboxCountKey = ["inbox-count"] as const;
const constructionsOverviewKey = ["constructions-overview"] as const;

export function InboxPage() {
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
		setSelectedExecIds(checked ? visibleTrades.map((trade) => trade.execId) : []);
	};

	const onToggleRow = (execId: string, checked: boolean) => {
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
							<table className="min-w-full text-sm">
								<thead className="bg-muted/40">
									<tr>
										<th className="px-2 py-2 text-left">
											<label>
												<input
													type="checkbox"
													checked={allVisibleSelected}
													onChange={(event) => onToggleAll(event.currentTarget.checked)}
												/>{" "}
												Выбрать всё
											</label>
										</th>
										<th className="px-2 py-2 text-left">Время</th>
										<th className="px-2 py-2 text-left">execId</th>
										<th className="px-2 py-2 text-left">Инструмент</th>
										<th className="px-2 py-2 text-left">Направление</th>
										<th className="px-2 py-2 text-left">Количество</th>
										<th className="px-2 py-2 text-left">Цена</th>
										<th className="px-2 py-2 text-left">Сумма</th>
										<th className="px-2 py-2 text-left">Комиссия</th>
									</tr>
								</thead>
								<tbody>
									{visibleTrades.map((trade) => (
										<tr
											key={trade.execId}
											draggable
											onDragStart={() => onDragStart(trade)}
											className="border-t"
										>
											<td className="px-2 py-2">
												<input
													type="checkbox"
													checked={selectedExecIds.includes(trade.execId)}
													onChange={(event) => onToggleRow(trade.execId, event.currentTarget.checked)}
												/>
											</td>
											<td className="px-2 py-2">{formatMoment(trade.executedAt)}</td>
											<td className="px-2 py-2">{trade.execId}</td>
											<td className="px-2 py-2">{trade.symbol}</td>
											<td className="px-2 py-2">{trade.isBuy ? "покупка" : "продажа"}</td>
											<td className="px-2 py-2">{formatSignedAmount(trade.quantity)}</td>
											<td className="px-2 py-2">{trade.price}</td>
											<td className="px-2 py-2">{trade.amountUsdt}</td>
											<td className="px-2 py-2">{formatSignedAmount(trade.fee)} {trade.feeCurrency ?? ""}</td>
										</tr>
									))}
								</tbody>
							</table>
						</div>
					)}
				</div>

				<aside className="flex flex-col gap-3">
					<h2 className="text-base font-semibold">Конструкции-цели</h2>
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
