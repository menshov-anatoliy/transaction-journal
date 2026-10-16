import { act, renderHook } from "@testing-library/react";
import { beforeEach, describe, expect, it } from "vitest";
import {
	COLLAPSED_LEFT_WIDTH_REM,
	PANEL_WIDTH_RANGE_REM,
	clampPanelWidth,
	readPanelGeometry,
	usePanelGeometry,
	writePanelGeometry,
} from "./layout-geometry";

// Геометрия панелей каркаса: границы ширин (левая 4.25–20 rem, правая
// 20–40 rem), persistence в localStorage («tj.layout.*») и восстановление
// на монтировании. Та же геометрия задаёт ширины сегментов топбара —
// колонки каркаса и шапки совпадают.
// Traceability: openspec:ui/screens#scenario-frame-panel-geometry-persists
// Traceability: openspec:ui/screens#scenario-frame-topbar-segments-align-to-columns
// Traceability: change:rework-spa-shell-and-screens/design#D1
// Traceability: change:rework-contextual-topbar/design#D1

beforeEach(() => {
	localStorage.clear();
});

// Ширина свёрнутой панели равна рейке 44 px; та же константа сжимает
// сегмент топбара над панелью и не меняет сохраняемый диапазон.
// Traceability: openspec:ui/screens#scenario-frame-collapsed-panel-miniature-rail
// Traceability: openspec:ui/screens#scenario-frame-topbar-segments-align-to-columns
it("свёрнутая панель занимает только ширину рейки миниатюр", () => {
	expect(COLLAPSED_LEFT_WIDTH_REM).toBe(2.75);
	expect(PANEL_WIDTH_RANGE_REM.left.min).toBe(4.25);
});

describe("clampPanelWidth", () => {
	it("держит ширину левой панели в диапазоне 4.25–20 rem", () => {
		// Assert: значения за границами обрезаются к диапазону панели.
		expect(clampPanelWidth("left", 2)).toBe(PANEL_WIDTH_RANGE_REM.left.min);
		expect(clampPanelWidth("left", 50)).toBe(PANEL_WIDTH_RANGE_REM.left.max);
		expect(clampPanelWidth("left", 10)).toBe(10);
	});

	it("держит ширину правой панели в диапазоне 20–40 rem", () => {
		// Assert: значения за границами обрезаются к диапазону панели.
		expect(clampPanelWidth("right", 5)).toBe(PANEL_WIDTH_RANGE_REM.right.min);
		expect(clampPanelWidth("right", 99)).toBe(PANEL_WIDTH_RANGE_REM.right.max);
		expect(clampPanelWidth("right", 30)).toBe(30);
	});
});

describe("readPanelGeometry", () => {
	it("возвращает дефолт при отсутствии записи", () => {
		// Act/Assert: без сохранённой геометрии — ширины по умолчанию,
		// панель развёрнута.
		expect(readPanelGeometry("left")).toEqual({ width: 16, collapsed: false });
		expect(readPanelGeometry("right")).toEqual({ width: 24, collapsed: false });
	});

	it("восстанавливает сохранённые ширину и свёрнутость", () => {
		// Arrange: пользователь ранее сузил левую панель и свернул правую.
		localStorage.setItem("tj.layout.left", JSON.stringify({ width: 8, collapsed: false }));
		localStorage.setItem("tj.layout.right", JSON.stringify({ width: 32, collapsed: true }));

		// Act/Assert: геометрия читается как сохранённая.
		expect(readPanelGeometry("left")).toEqual({ width: 8, collapsed: false });
		expect(readPanelGeometry("right")).toEqual({ width: 32, collapsed: true });
	});

	it("обрезает сохранённую ширину вне диапазона к границе", () => {
		// Arrange: запись из старой версии с недопустимой шириной.
		localStorage.setItem("tj.layout.left", JSON.stringify({ width: 42, collapsed: false }));

		// Act/Assert: ширина приводится к максимуму левой панели.
		expect(readPanelGeometry("left").width).toBe(PANEL_WIDTH_RANGE_REM.left.max);
	});

	it("возвращает дефолт при повреждённой записи", () => {
		// Arrange: storage испорчен (не JSON).
		localStorage.setItem("tj.layout.right", "{broken");

		// Act/Assert: каркас не падает, используется дефолт.
		expect(readPanelGeometry("right")).toEqual({ width: 24, collapsed: false });
	});
});

describe("writePanelGeometry", () => {
	it("сохраняет геометрию в ключ tj.layout.* и читает её обратно", () => {
		// Act: запись геометрии панели.
		writePanelGeometry("left", { width: 12, collapsed: true });

		// Assert: запись легла в ожидаемый ключ и читается без потерь.
		expect(JSON.parse(localStorage.getItem("tj.layout.left") ?? "")).toEqual({
			width: 12,
			collapsed: true,
		});
		expect(readPanelGeometry("left")).toEqual({ width: 12, collapsed: true });
	});
});

describe("usePanelGeometry", () => {
	it("восстанавливает геометрию на монтировании", () => {
		// Arrange: до монтирования пользователь свернул левую панель.
		localStorage.setItem("tj.layout.left", JSON.stringify({ width: 6, collapsed: true }));

		// Act: хук монтируется.
		const { result } = renderHook(() => usePanelGeometry("left"));

		// Assert: геометрия восстановлена без дополнительных вызовов.
		expect(result.current.width).toBe(6);
		expect(result.current.collapsed).toBe(true);
	});

	it("пишет изменение ширины в localStorage с обрезкой к диапазону", () => {
		// Arrange: хук смонтирован с дефолтной геометрией.
		const { result } = renderHook(() => usePanelGeometry("left"));

		// Act: тянем панель шире допустимого.
		act(() => result.current.setWidth(99));

		// Assert: ширина обрезана к максимуму и сохранена.
		expect(result.current.width).toBe(PANEL_WIDTH_RANGE_REM.left.max);
		expect(JSON.parse(localStorage.getItem("tj.layout.left") ?? "")).toEqual({
			width: PANEL_WIDTH_RANGE_REM.left.max,
			collapsed: false,
		});
	});

	it("пишет смену свёрнутости в localStorage", () => {
		// Arrange: хук смонтирован с дефолтной геометрией.
		const { result } = renderHook(() => usePanelGeometry("right"));

		// Act: сворачиваем панель.
		act(() => result.current.setCollapsed(true));

		// Assert: свёрнутость сохранена вместе с шириной.
		expect(result.current.collapsed).toBe(true);
		expect(JSON.parse(localStorage.getItem("tj.layout.right") ?? "")).toEqual({
			width: 24,
			collapsed: true,
		});
	});
});
