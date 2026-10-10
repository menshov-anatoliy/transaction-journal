import {
	type ColumnDef,
	type SortingState,
	flexRender,
	getCoreRowModel,
	getSortedRowModel,
	useReactTable,
} from "@tanstack/react-table";
import * as React from "react";
import { ArrowDown, ArrowUp, ArrowUpDown } from "lucide-react";
import type { ConstructionRow } from "@/lib/api/constructions";
import { ConstructionStatusChip } from "./construction-status-chip";
import { CompactFinResultIndicator } from "@/components/finresult/fin-result-indicator";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { DASH, degrade } from "@/lib/format/degradation";
import { formatDay } from "@/lib/format/display-time";
import { formatAmount, formatSignedAmount, formatSignedPercent } from "@/lib/format/quantity";
import { cn } from "@/lib/utils";
import { useIsMobile } from "@/lib/use-mobile";

// Таблица конструкций раздела по концепции §3: паритет 13 колонок, бейдж
// живых подсказок в строке, компактный индикатор финрезультата в ячейке
// итога, клик по строке выделяет (не навигация), сортировка колонками.
// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-

/** Пропсы таблицы конструкций. */
export interface ConstructionsTableProps {
	/** Строки таблицы из обзора раздела. */
	readonly rows: readonly ConstructionRow[];
	/** Идентификатор выделенной строки; null — ничего не выделено. */
	readonly selectedId: number | null;
	/** Обработчик выделения строки кликом. */
	readonly onSelect: (constructionId: number) => void;
}

/** Сбой котировок строки: нереализованная часть не оценена именно из-за сбоя. */
function quotesDegraded(row: ConstructionRow): boolean {
	return row.unrealizedPnL === null;
}

/** Тон величины: положительная — зелёная, отрицательная — красная. */
function toneClass(value: number): string {
	if (value > 0) {
		return "text-[color:var(--fin-positive-strong)]";
	}

	return value < 0 ? "text-[color:var(--fin-negative)]" : "";
}

/** Величина ячейки с деградацией: прочерк вместо null, признак сбоя в title. */
function DegradedValue<T>({ value, format }: { value: T | null; format: (value: T) => string }) {
	const degraded = degrade(value, format);
	if (degraded.available) {
		return <span className="tabular-nums">{degraded.text}</span>;
	}

	return (
		<span className="text-muted-foreground" title={degraded.reason}>
			{DASH}
		</span>
	);
}

export function ConstructionsTable({ rows, selectedId, onSelect }: ConstructionsTableProps) {
	const isMobile = useIsMobile();
	const [sorting, setSorting] = React.useState<SortingState>([]);

	const columns = React.useMemo<ColumnDef<ConstructionRow>[]>(
		() => [
			{
				id: "name",
				header: "Конструкция",
				accessorKey: "name",
				// Имя строки по мастеру Body #1 — Inter 12.5/500 на textPrimary.
				// Traceability: change:reconcile-frontend-with-design/design#D6
				cell: ({ row }) => <span className="text-[12.5px] font-medium">{row.original.name}</span>,
			},
			{
				id: "hints",
				header: "Подсказки",
				accessorKey: "liveHintCount",
				cell: ({ row }) =>
					row.original.liveHintCount > 0 ? (
						// Бейдж живых подсказок строки: число живых записей; без
						// живых подсказок бейдж не рисуется.
						<span
							data-slot="hint-badge"
							className="inline-flex min-w-5 items-center justify-center rounded-full bg-primary px-1.5 text-xs font-semibold text-primary-foreground"
							title={`живых подсказок: ${row.original.liveHintCount}`}
						>
							{row.original.liveHintCount}
						</span>
					) : null,
			},
			{
				id: "status",
				header: "Статус",
				accessorKey: "status",
				// Статусы списка используют тот же примитив и тона, что и карточка.
				// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
				cell: ({ row }) => <ConstructionStatusChip status={row.original.status} />,
			},
			{
				id: "capital",
				header: "Капитал",
				accessorKey: "allocatedCapitalUsdt",
				sortingFn: "basic",
				// Числовые колонки сортируются от большего к меньшему: сильнейший итог сверху.
				sortDescFirst: true,
				cell: ({ row }) => <DegradedValue value={row.original.allocatedCapitalUsdt} format={formatAmount} />,
			},
			{
				id: "value",
				header: "Стоимость",
				accessorKey: "markValue",
				sortingFn: "basic",
				// Числовые колонки сортируются от большего к меньшему: сильнейший итог сверху.
				sortDescFirst: true,
				cell: ({ row }) =>
					row.original.markValue === null && quotesDegraded(row.original) ? (
						<span className="text-muted-foreground" title="Провайдер котировок недоступен — стоимость не оценена">
							сбой котировок
						</span>
					) : (
						<DegradedValue value={row.original.markValue} format={formatSignedAmount} />
					),
			},
			{
				id: "realized",
				header: "Реализов.",
				accessorKey: "realizedPnL",
				cell: ({ row }) => (
					<span className={cn("tabular-nums", toneClass(row.original.realizedPnL))}>
						{formatSignedAmount(row.original.realizedPnL)}
					</span>
				),
			},
			{
				id: "unrealized",
				header: "Нереализов.",
				accessorKey: "unrealizedPnL",
				sortingFn: "basic",
				// Числовые колонки сортируются от большего к меньшему: сильнейший итог сверху.
				sortDescFirst: true,
				cell: ({ row }) =>
					row.original.unrealizedPnL === null ? (
						// Сбой марок — видимое состояние строки: признак сбоя
						// занимает место нереализованной оценки.
						<span className="text-muted-foreground" title="Провайдер котировок недоступен — нереализованный PnL не оценён">
							сбой котировок
						</span>
					) : (
						<span className={cn("tabular-nums", toneClass(row.original.unrealizedPnL))}>
							{formatSignedAmount(row.original.unrealizedPnL)}
						</span>
					),
			},
			{
				id: "adjustments",
				header: "Коррект.",
				accessorKey: "adjustmentsPnL",
				cell: ({ row }) => (
					<span className={cn("tabular-nums", toneClass(row.original.adjustmentsPnL))}>
						{formatSignedAmount(row.original.adjustmentsPnL)}
					</span>
				),
			},
			{
				id: "total",
				header: "Итог",
				accessorKey: "totalPnL",
				sortingFn: "basic",
				// Числовые колонки сортируются от большего к меньшему: сильнейший итог сверху.
				sortDescFirst: true,
				cell: ({ row }) => (
					<div className="flex flex-col gap-1">
						{row.original.totalPnL === null ? (
							// Итог без нереализованной части неполный — признак
							// сбоя марок, а не прочерк нулевого итога.
							<span className="text-muted-foreground" title="Итог неполный: нереализованная часть не оценена из-за сбоя котировок">
								неполный
							</span>
						) : (
							<span className={cn("font-semibold tabular-nums", toneClass(row.original.totalPnL))}>
								{formatSignedAmount(row.original.totalPnL)}
							</span>
						)}
						<CompactFinResultIndicator
							input={{
								plannedRisk: row.original.riskUsdt,
								plannedProfit: row.original.profitUsdt,
								realized: row.original.realizedPnL,
								unrealized: row.original.unrealizedPnL,
								quotesDegraded: quotesDegraded(row.original),
								// Граница реального риска в таблице считается по
								// метрике бэкенда; null — заглушка плановым риском.
								// Traceability: openspec:ui/screens#requirement-risk-profit-hint
								realRisk: row.original.realRiskUsdt,
							}}
						/>
					</div>
				),
			},
			{
				id: "totalPercent",
				header: "% капитала",
				accessorKey: "totalPnLPercent",
				sortingFn: "basic",
				// Числовые колонки сортируются от большего к меньшему: сильнейший итог сверху.
				sortDescFirst: true,
				cell: ({ row }) =>
					row.original.totalPnLPercent === null ? (
						<span className="text-muted-foreground">—</span>
					) : (
						<span className={cn("tabular-nums", toneClass(row.original.totalPnLPercent))}>
							{formatSignedPercent(row.original.totalPnLPercent)}
						</span>
					),
			},
			{
				id: "capitalUsage",
				header: "Занято %",
				accessorKey: "capitalUsagePercent",
				sortingFn: "basic",
				// Числовые колонки сортируются от большего к меньшему: сильнейший итог сверху.
				sortDescFirst: true,
				cell: ({ row }) => (
					<DegradedValue value={row.original.capitalUsagePercent} format={formatSignedPercent} />
				),
			},
			{
				id: "openedAt",
				header: "Открыта",
				accessorKey: "openedAt",
				sortingFn: "basic",
				// Числовые колонки сортируются от большего к меньшему: сильнейший итог сверху.
				sortDescFirst: true,
				cell: ({ row }) => <DegradedValue value={row.original.openedAt} format={formatDay} />,
			},
			{
				id: "closedAt",
				header: "Закрыта",
				accessorKey: "closedAt",
				sortingFn: "basic",
				// Числовые колонки сортируются от большего к меньшему: сильнейший итог сверху.
				sortDescFirst: true,
				cell: ({ row }) => <DegradedValue value={row.original.closedAt} format={formatDay} />,
			},
		],
		[],
	);

	// Выделение строки сохраняет ссылку на данные: иначе автосброс состояния
	// TanStack Table запускает бесконечные перерисовки при каждом новом массиве.
	// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-
	const data = React.useMemo(() => [...rows], [rows]);

	const table = useReactTable({
		data,
		columns,
		state: { sorting },
		onSortingChange: setSorting,
		getCoreRowModel: getCoreRowModel(),
		getSortedRowModel: getSortedRowModel(),
	});

	if (isMobile) {
		// На узком экране таблица деградирует в карточки без потери колонок:
		// ключевые значения сохраняются в компактной строковой форме.
		// Traceability: doc:.wf-research/ui-concept/concept.md#11-адаптив
		return (
			<div className="flex flex-col gap-2 p-2">
				{rows.length === 0 ? (
					<p className="text-muted-foreground py-6 text-center text-sm">конструкций нет</p>
				) : (
					rows.map((row) => (
						<button
							key={row.constructionId}
							type="button"
							aria-selected={row.constructionId === selectedId}
							onClick={() => onSelect(row.constructionId)}
							className={cn("rounded-lg border p-3 text-left", row.constructionId === selectedId && "bg-accent")}
						>
							<div className="mb-1 flex items-start justify-between gap-2">
								<p className="font-medium">{row.name}</p>
								{row.liveHintCount > 0 && <span className="rounded-full bg-primary px-2 py-0.5 text-xs text-primary-foreground">{row.liveHintCount}</span>}
							</div>
							{/* Узкий список сохраняет тот же статусный примитив. */}
							{/* Traceability: openspec:ui/design-system#requirement-reusable-design-primitives */}
							<div className="text-muted-foreground flex flex-wrap items-center gap-1 text-xs">
								<ConstructionStatusChip status={row.status} />
								<span>· капитал {row.allocatedCapitalUsdt === null ? DASH : formatAmount(row.allocatedCapitalUsdt)}</span>
							</div>
							<p className="mt-1 text-sm">
								итог: {row.totalPnL === null ? "неполный" : formatSignedAmount(row.totalPnL)} ·
								{" "}реализов. {formatSignedAmount(row.realizedPnL)} ·
								{" "}нереализов. {row.unrealizedPnL === null ? "сбой котировок" : formatSignedAmount(row.unrealizedPnL)}
							</p>
							<p className="text-muted-foreground mt-1 text-xs">
								занято {row.capitalUsagePercent === null ? DASH : formatSignedPercent(row.capitalUsagePercent)} · открыта{" "}
								{row.openedAt === null ? DASH : formatDay(row.openedAt)}
							</p>
						</button>
					))
				)}
			</div>
		);
	}

	// Плотность Body #1 (X2ic2p) мастера: ячейки 9/10 при шрифте 12, имя —
	// 12.5/500, шапка — 8/10 и 11/600.
	// Traceability: change:reconcile-frontend-with-design/design#D6
	return (
		<Table density="comfortable">
			<TableHeader>
				{table.getHeaderGroups().map((headerGroup) => (
					<TableRow key={headerGroup.id}>
						{headerGroup.headers.map((header) => (
							<TableHead key={header.id}>
								<button
									type="button"
									className="inline-flex cursor-pointer items-center gap-1 hover:text-foreground"
									onClick={header.column.getToggleSortingHandler()}
								>
									{flexRender(header.column.columnDef.header, header.getContext())}
									<SortIcon direction={header.column.getIsSorted()} />
								</button>
							</TableHead>
						))}
					</TableRow>
				))}
			</TableHeader>
			<TableBody>
				{table.getRowModel().rows.length === 0 ? (
					<TableRow>
						<TableCell colSpan={columns.length} className="text-muted-foreground py-6 text-center">
							конструкций нет
						</TableCell>
					</TableRow>
				) : (
					table.getRowModel().rows.map((row) => (
						<TableRow
							key={row.original.constructionId}
							aria-selected={row.original.constructionId === selectedId}
							className={cn("cursor-pointer", row.original.constructionId === selectedId && "bg-accent")}
							onClick={() => onSelect(row.original.constructionId)}
						>
							{row.getVisibleCells().map((cell) => (
								<TableCell key={cell.id}>{flexRender(cell.column.columnDef.cell, cell.getContext())}</TableCell>
							))}
						</TableRow>
					))
				)}
			</TableBody>
		</Table>
	);
}

/** Иконка направления сортировки колонки. */
function SortIcon({ direction }: { direction: false | "asc" | "desc" }) {
	if (direction === "asc") {
		return <ArrowUp aria-hidden className="size-3" />;
	}

	return direction === "desc" ? <ArrowDown aria-hidden className="size-3" /> : <ArrowUpDown aria-hidden className="size-3 opacity-40" />;
}
