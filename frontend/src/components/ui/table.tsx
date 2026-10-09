import * as React from "react";
import { cn } from "@/lib/utils";

// Компонент shadcn/ui: код живёт в репозитории (ADR-0010), тема — в
// src/index.css через CSS-переменные Tailwind CSS 4.

// Варианты плотности таблиц дизайн-системы design.pen: вместо единого p-2
// каждый экран получает собственную геометрию ячеек (design.md D6).
// Паддинги мастера — [вертикаль, горизонт]: comfortable 9/10 — таблица
// конструкций (Body #1), regular 8/10 — входящие (Body #5), compact 7/12 —
// четыре таблицы карточки конструкции (Body #2), dense 6/12 — журнал
// запусков синхронизации (Body #8, кегль 11.5 — самый плотный экран).
// Traceability: change:reconcile-frontend-with-design/design#D6
export type TableDensity = "comfortable" | "regular" | "compact" | "dense";

/** Маппинг плотность→классы ячейки: паддинги своей таблицы и кегль 12/11.5. */
// Кегли ячеек (12 и 11.5) заданы требованием типографики дизайн-системы.
// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system
export const tableDensityCellClasses: Record<TableDensity, string> = {
	comfortable: "py-[9px] px-2.5 text-xs",
	regular: "py-2 px-2.5 text-xs",
	compact: "py-[7px] px-3 text-xs",
	dense: "py-1.5 px-3 text-[11.5px]",
};

/** Маппинг плотность→классы заголовка: паддинги своей таблицы, кегль 11. */
// Заголовки всех таблиц мастера — Inter 11/600 вторичным текстом.
// Traceability: change:reconcile-frontend-with-design/design#D6
export const tableDensityHeadClasses: Record<TableDensity, string> = {
	comfortable: "py-2 px-2.5 text-[11px]",
	regular: "py-2 px-2.5 text-[11px]",
	compact: "py-[7px] px-3 text-[11px]",
	dense: "py-1.5 px-3 text-[11px]",
};

/** Плотность таблицы для ячеек-потомков: без провайдера — легаси-геометрия. */
const TableDensityContext = React.createContext<TableDensity | null>(null);

/** Пропсы таблицы: плотность дизайн-системы поверх атрибутов table. */
export interface TableProps extends React.ComponentProps<"table"> {
	/** Вариант плотности ячеек по дизайн-системе; не задан — прежняя геометрия shadcn. */
	readonly density?: TableDensity;
}

function Table({ className, density, ...props }: TableProps) {
	return (
		<div data-slot="table-container" className="relative w-full overflow-x-auto">
			<TableDensityContext.Provider value={density ?? null}>
				<table data-slot="table" data-density={density} className={cn("w-full caption-bottom text-sm", className)} {...props} />
			</TableDensityContext.Provider>
		</div>
	);
}

function TableHeader({ className, ...props }: React.ComponentProps<"thead">) {
	return <thead data-slot="table-header" className={cn("[&_tr]:border-b", className)} {...props} />;
}

function TableBody({ className, ...props }: React.ComponentProps<"tbody">) {
	return <tbody data-slot="table-body" className={cn("[&_tr:last-child]:border-0", className)} {...props} />;
}

function TableFooter({ className, ...props }: React.ComponentProps<"tfoot">) {
	return <tfoot data-slot="table-footer" className={cn("bg-muted/50 border-t font-medium [&>tr]:last:border-b-0", className)} {...props} />;
}

function TableRow({ className, ...props }: React.ComponentProps<"tr">) {
	return (
		<tr
			data-slot="table-row"
			className={cn("hover:bg-muted/50 data-[state=selected]:bg-muted border-b transition-colors", className)}
			{...props}
		/>
	);
}

function TableHead({ className, ...props }: React.ComponentProps<"th">) {
	const density = React.useContext(TableDensityContext);
	return (
		<th
			data-slot="table-head"
			className={cn(
				"text-left align-middle whitespace-nowrap [&:has([role=checkbox])]:pr-0 [&>[role=checkbox]]:translate-y-[2px]",
				density === null
					? "text-foreground h-10 px-2 font-medium"
					: cn("h-auto text-muted-foreground font-semibold", tableDensityHeadClasses[density]),
				className,
			)}
			{...props}
		/>
	);
}

function TableCell({ className, ...props }: React.ComponentProps<"td">) {
	const density = React.useContext(TableDensityContext);
	return (
		<td
			data-slot="table-cell"
			className={cn(
				"align-middle whitespace-nowrap [&:has([role=checkbox])]:pr-0 [&>[role=checkbox]]:translate-y-[2px]",
				density === null ? "p-2" : tableDensityCellClasses[density],
				className,
			)}
			{...props}
		/>
	);
}

function TableCaption({ className, ...props }: React.ComponentProps<"caption">) {
	return <caption data-slot="table-caption" className={cn("text-muted-foreground mt-4 text-sm", className)} {...props} />;
}

export { Table, TableBody, TableCaption, TableCell, TableFooter, TableHead, TableHeader, TableRow };
