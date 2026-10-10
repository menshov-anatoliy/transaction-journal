import { useCallback, useMemo, useState } from "react";

/**
 * Геометрия панелей каркаса: ширины в rem, границы тягия сплиттером и
 * persistence в localStorage («tj.layout.*»). Свёрнутость и ширины
 * восстанавливаются на монтировании; URL-параметры панельную геометрию
 * не несут — только фильтры и выбор (design.md D1).
 *
 * Traceability: openspec:ui/screens#requirement-app-frame-navigation
 * Traceability: change:rework-spa-shell-and-screens/design#D1
 */

/** Панели каркаса с сохраняемой геометрией. */
export type LayoutPanel = "left" | "right";

/** Допустимый диапазон ширины панели в rem: левая 4.25–20, правая 20–40. */
export const PANEL_WIDTH_RANGE_REM: Readonly<Record<LayoutPanel, { min: number; max: number }>> = {
	left: { min: 4.25, max: 20 },
	right: { min: 20, max: 40 },
};

// Ширины по умолчанию при отсутствии сохранённой геометрии: левая панель —
// прежняя w-64 (16 rem), правая контекстная — размер бывшей зашитой области
// страниц (24 rem), обе внутри своих диапазонов.
const DEFAULT_WIDTH_REM: Readonly<Record<LayoutPanel, number>> = {
	left: 16,
	right: 24,
};

// Ключи localStorage по design.md D1: геометрия хранится локально браузера
// и переживает перезагрузку; URL для неё не используется.
const STORAGE_KEYS: Readonly<Record<LayoutPanel, string>> = {
	left: "tj.layout.left",
	right: "tj.layout.right",
};

/** Сохраняемая геометрия одной панели каркаса. */
export interface PanelGeometry {
	/** Ширина панели в rem (внутри диапазона PANEL_WIDTH_RANGE_REM). */
	width: number;
	/** Панель схлопнута до иконочной колонки. */
	collapsed: boolean;
}

/** Приводит ширину панели к допустимому диапазону. */
export function clampPanelWidth(panel: LayoutPanel, width: number): number {
	const { min, max } = PANEL_WIDTH_RANGE_REM[panel];
	return Math.min(Math.max(width, min), max);
}

/** Нормализует произвольное значение в корректную геометрию панели. */
function normalizeGeometry(panel: LayoutPanel, candidate: Partial<PanelGeometry> | null): PanelGeometry {
	const width =
		typeof candidate?.width === "number" && Number.isFinite(candidate.width)
			? clampPanelWidth(panel, candidate.width)
			: DEFAULT_WIDTH_REM[panel];
	// Свёрнутость по умолчанию — развёрнутое состояние: сохранённое значение
	// принимается только как явный boolean из storage или вызова.
	const collapsed = typeof candidate?.collapsed === "boolean" ? candidate.collapsed : false;
	return { width, collapsed };
}

/**
 * Читает сохранённую геометрию панели; при отсутствии или порче записи
 * возвращает ширину по умолчанию — геометрия обязана восстанавливаться
 * на монтировании (scenario-frame-panel-geometry-persists).
 *
 * Traceability: openspec:ui/screens#scenario-frame-panel-geometry-persists
 */
export function readPanelGeometry(panel: LayoutPanel): PanelGeometry {
	if (typeof window === "undefined" || typeof localStorage === "undefined") {
		return normalizeGeometry(panel, null);
	}

	try {
		const raw = localStorage.getItem(STORAGE_KEYS[panel]);
		if (raw == null) {
			return normalizeGeometry(panel, null);
		}

		return normalizeGeometry(panel, JSON.parse(raw) as Partial<PanelGeometry>);
	} catch {
		// Повреждённая запись не должна ломать каркас — используем дефолт.
		return normalizeGeometry(panel, null);
	}
}

/** Сохраняет геометрию панели в localStorage; сбой storage не критичен. */
export function writePanelGeometry(panel: LayoutPanel, geometry: PanelGeometry): void {
	if (typeof window === "undefined" || typeof localStorage === "undefined") {
		return;
	}

	try {
		localStorage.setItem(STORAGE_KEYS[panel], JSON.stringify(geometry));
	} catch {
		// Приватный режим/переполнение квоты: панели продолжают работать
		// в памяти, персистентность вернётся при доступном storage.
	}
}

/** Управление геометрией одной панели из компонента каркаса. */
export interface PanelGeometryController {
	/** Текущая ширина панели в rem. */
	width: number;
	/** Текущая свёрнутость панели. */
	collapsed: boolean;
	/** Меняет ширину (значение обрезается к диапазону) и сохраняет её. */
	setWidth: (widthRem: number) => void;
	/** Меняет свёрнутость и сохраняет её. */
	setCollapsed: (collapsed: boolean) => void;
}

/**
 * Хук геометрии панели: восстановление из localStorage на монтировании
 * (инициализатор состояния) и запись при каждом изменении — иначе
 * перезагрузка потеряет последнее действие пользователя.
 *
 * Traceability: openspec:ui/screens#scenario-frame-panel-geometry-persists
 */
export function usePanelGeometry(panel: LayoutPanel): PanelGeometryController {
	const [geometry, setGeometry] = useState<PanelGeometry>(() => readPanelGeometry(panel));

	const setWidth = useCallback(
		(widthRem: number) => {
			setGeometry((previous) => {
				const next = { ...previous, width: clampPanelWidth(panel, widthRem) };
				writePanelGeometry(panel, next);
				return next;
			});
		},
		[panel],
	);

	const setCollapsed = useCallback(
		(collapsed: boolean) => {
			setGeometry((previous) => {
				const next = { ...previous, collapsed };
				writePanelGeometry(panel, next);
				return next;
			});
		},
		[panel],
	);

	return useMemo(
		() => ({ width: geometry.width, collapsed: geometry.collapsed, setWidth, setCollapsed }),
		[geometry.width, geometry.collapsed, setWidth, setCollapsed],
	);
}
