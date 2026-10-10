import { useRef } from "react";
import { cn } from "@/lib/utils";

/**
 * Сплиттер ширины панели (design.md D1): узкая полоса-захват на границе
 * панели и центра. Тянет шину указателем; для клавиатуры поддержаны
 * стрелки влево/вправо (шаг 0.5 rem). Дельта передаётся наружу в rem,
 * клампинг к диапазону делает контроллер геометрии.
 *
 * Traceability: openspec:ui/screens#requirement-app-frame-navigation
 * Traceability: change:rework-spa-shell-and-screens/design#D1
 */

/** Базовый размер корневого шрифта, если computed style недоступен (jsdom). */
const FALLBACK_ROOT_FONT_SIZE_PX = 16;

/** Читает размер корневого шрифта для перевода пикселей в rem. */
function getRootFontSizePx(): number {
	const size = Number.parseFloat(window.getComputedStyle(document.documentElement).fontSize);
	return Number.isFinite(size) && size > 0 ? size : FALLBACK_ROOT_FONT_SIZE_PX;
}

export interface PanelResizerProps {
	/** Какая панель тянется: «left» растёт вправо, «right» — влево. */
	side: "left" | "right";
	/** Дельта изменения ширины панели в rem (со знаком). */
	onResize: (deltaRem: number) => void;
	/** Подпись для вспомогательных технологий. */
	label: string;
	className?: string;
}

export function PanelResizer({ side, onResize, label, className }: PanelResizerProps) {
	const lastPointerXRef = useRef<number | null>(null);

	// Направление: движение указателя вправо расширяет левую панель и
	// сужает правую, поэтому дельта для правой панели инвертируется.
	const direction = side === "left" ? 1 : -1;

	return (
		<div
			role="separator"
			aria-orientation="vertical"
			aria-label={label}
			tabIndex={0}
			data-slot="panel-resizer"
			data-side={side}
			className={cn(
				"w-1 shrink-0 cursor-col-resize bg-transparent transition-colors hover:bg-divider focus-visible:bg-divider focus-visible:outline-none",
				className,
			)}
			onPointerDown={(event) => {
				event.currentTarget.setPointerCapture(event.pointerId);
				lastPointerXRef.current = event.clientX;
			}}
			onPointerMove={(event) => {
				const lastX = lastPointerXRef.current;
				if (lastX == null) {
					return;
				}

				const deltaRem = ((event.clientX - lastX) * direction) / getRootFontSizePx();
				lastPointerXRef.current = event.clientX;
				if (deltaRem != 0) {
					onResize(deltaRem);
				}
			}}
			onPointerUp={(event) => {
				event.currentTarget.releasePointerCapture(event.pointerId);
				lastPointerXRef.current = null;
			}}
			onPointerCancel={() => {
				lastPointerXRef.current = null;
			}}
			onKeyDown={(event) => {
				// Стрелки согласованы с перетаскиванием: вправо расширяет
				// левую панель и сужает правую (дельта инвертируется).
				const stepRem = 0.5;
				if (event.key === "ArrowRight") {
					onResize(stepRem * direction);
					event.preventDefault();
					return;
				}

				if (event.key === "ArrowLeft") {
					onResize(-stepRem * direction);
					event.preventDefault();
				}
			}}
		/>
	);
}
