import { render, screen, within } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import {
	Table,
	tableDensityCellClasses,
	tableDensityHeadClasses,
	type TableDensity,
} from "./table";
import { TableBody, TableCell, TableHead, TableHeader, TableRow } from "./table";

// Проверяется общий слой таблиц: маппинг плотность→классы по дизайн-системе
// design.pen. Мастер дифференцирует геометрию ячеек по экранам — Body #1
// (конструкции) 9/10 и шрифт 12, Body #2 (карточка) 7/12 и 12, Body #5
// (входящие) 8/10 и 12, Body #8 (журнал запусков) 6/12 и 11.5; заголовки всех
// таблиц — 11/600 на textSecondary. Пиксельная сверка — скриншот-парами в
// .wf-research/design-audit/screenshots/04-tables/.
// Traceability: change:reconcile-frontend-with-design/design#D6
// Traceability: openspec:ui/design-system#requirement-typography-matches-design-system

/** Таблица одной строки для инспекции классов ячейки и заголовка. */
function renderTable(density?: TableDensity) {
	return render(
		<Table density={density}>
			<TableHeader>
				<TableRow>
					<TableHead>Колонка</TableHead>
				</TableRow>
			</TableHeader>
			<TableBody>
				<TableRow>
					<TableCell>Значение</TableCell>
				</TableRow>
			</TableBody>
		</Table>,
	);
}

describe("общий слой таблиц: варианты плотности по дизайн-системе", () => {
	it.each([
		// Плотность → геометрия ячейки из дизайн-нод Body #1/#5/#2/#8.
		["comfortable", "py-[9px] px-2.5 text-xs"],
		["regular", "py-2 px-2.5 text-xs"],
		["compact", "py-[7px] px-3 text-xs"],
		["dense", "py-1.5 px-3 text-[11.5px]"],
	] as const)("плотность %s даёт ячейке классы %s", (density, expected) => {
		// Act + Assert: маппинг плотность→классы ячейки без рендера.
		expect(tableDensityCellClasses[density]).toContain(expected);
	});

	it.each([
		// Заголовки всех плотностей: паддинги своей таблицы, кегль 11.
		["comfortable", "py-2 px-2.5 text-[11px]"],
		["regular", "py-2 px-2.5 text-[11px]"],
		["compact", "py-[7px] px-3 text-[11px]"],
		["dense", "py-1.5 px-3 text-[11px]"],
	] as const)("плотность %s даёт заголовку классы %s", (density, expected) => {
		// Act + Assert: маппинг плотность→классы заголовка без рендера.
		expect(tableDensityHeadClasses[density]).toContain(expected);
	});

	it.each(["comfortable", "regular", "compact", "dense"] as const)(
		"плотность %s применяется к рендеренным ячейкам и помечает таблицу атрибутом",
		(density) => {
			// Act: таблица с плотностью.
			renderTable(density);

			// Assert: атрибут плотности на table, классы маппинга — на th/td.
			const table = screen.getByRole("table");
			expect(table).toHaveAttribute("data-density", density);

			const head = within(table).getByRole("columnheader", { name: "Колонка" });
			expect(head.className).toContain(tableDensityHeadClasses[density]);

			const cell = within(table).getByRole("cell", { name: "Значение" });
			expect(cell.className).toContain(tableDensityCellClasses[density]);
		},
	);

	it("заголовок плотности — 11/600 на textSecondary по мастеру", () => {
		// Act: таблица любой плотности.
		renderTable("compact");

		// Assert: шапка мастеру — Inter 11/600 вторичным текстом, без h-10.
		const head = screen.getByRole("columnheader", { name: "Колонка" });
		expect(head.className).toContain("text-[11px]");
		expect(head.className).toContain("font-semibold");
		expect(head.className).toContain("text-muted-foreground");
		expect(head.className).not.toContain("h-10");
	});

	it("без плотности сохраняется прежняя геометрия shadcn", () => {
		// Act: таблица без пропса density (потребители вне дизайна таблиц).
		renderTable();

		// Assert: легаси-геометрия h-10/p-2/text-sm на месте, атрибута нет.
		const table = screen.getByRole("table");
		expect(table).not.toHaveAttribute("data-density");
		expect(table.className).toContain("text-sm");

		const head = screen.getByRole("columnheader", { name: "Колонка" });
		expect(head.className).toContain("h-10");
		expect(head.className).toContain("px-2");

		const cell = screen.getByRole("cell", { name: "Значение" });
		expect(cell.className).toContain("p-2");
		expect(cell.className).not.toContain("text-xs");
	});
});
