import * as React from "react";
import { ChevronDown, type LucideIcon } from "lucide-react";
import { Button } from "@/components/ui/button";

/** Пункт меню «Параметры» шапки карточки. */
export interface ParamsMenuItem {
	/** Подпись пункта («Капитал…», «Риск…», «Профит…»). */
	readonly label: string;
	/** Иконка пункта из набора design.pen. */
	readonly icon: LucideIcon;
	/** Действие выбора пункта: открывает существующий диалог параметра. */
	readonly onSelect: () => void;
}

/** Пропсы меню «Параметры». */
export interface ParamsMenuProps {
	/** Подпись кнопки-триггера и aria-label выпадающего меню. */
	readonly label: string;
	/** Пункты меню. */
	readonly items: readonly ParamsMenuItem[];
	/** Блокировка на время выполнения команд карточки. */
	readonly disabled?: boolean;
}

// Меню «Параметры» собирает существующие диалоги правки капитала, риска и
// профита в одну точку входа шапки карточки; иконки и подписи — по мастеру
// «Меню:Параметры» design.pen.
// Traceability: openspec:ui/screens#scenario-detail-params-menu
export function ParamsMenu({ label, items, disabled = false }: ParamsMenuProps) {
	const [open, setOpen] = React.useState(false);
	const rootRef = React.useRef<HTMLDivElement>(null);

	// Клик вне меню и Escape закрывают меню, не выбирая пункт.
	React.useEffect(() => {
		if (open === false) {
			return;
		}

		const onPointerDown = (event: PointerEvent) => {
			if (rootRef.current !== null && rootRef.current.contains(event.target as Node) === false) {
				setOpen(false);
			}
		};
		const onKeyDown = (event: KeyboardEvent) => {
			if (event.key === "Escape") {
				setOpen(false);
			}
		};

		document.addEventListener("pointerdown", onPointerDown);
		document.addEventListener("keydown", onKeyDown);
		return () => {
			document.removeEventListener("pointerdown", onPointerDown);
			document.removeEventListener("keydown", onKeyDown);
		};
	}, [open]);

	return (
		<div ref={rootRef} className="relative">
			<Button
				variant="ghost"
				size="sm"
				className="text-xs"
				disabled={disabled}
				aria-haspopup="menu"
				aria-expanded={open}
				onClick={() => setOpen((value) => value === false)}
			>
				{label}
				<ChevronDown aria-hidden className="size-3.5" />
			</Button>
			{open && (
				<div
					role="menu"
					aria-label={label}
					className="bg-surface absolute top-full left-0 z-20 mt-1 flex min-w-40 flex-col gap-0.5 rounded-md border p-1.5 shadow-sm"
				>
					{items.map((item) => {
						const Icon = item.icon;
						return (
							<button
								key={item.label}
								type="button"
								role="menuitem"
								className="hover:bg-surface-2 flex items-center gap-2 rounded-sm px-2.5 py-2 text-left text-xs text-text-primary"
								onClick={() => {
									setOpen(false);
									item.onSelect();
								}}
							>
								<Icon aria-hidden className="size-3.5 shrink-0 text-text-secondary" />
								{item.label}
							</button>
						);
					})}
				</div>
			)}
		</div>
	);
}
