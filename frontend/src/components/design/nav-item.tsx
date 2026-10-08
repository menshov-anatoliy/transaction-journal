import { Slot, Slottable } from "@radix-ui/react-slot";
import { cva, type VariantProps } from "class-variance-authority";
import type { LucideIcon } from "lucide-react";
import * as React from "react";
import { cn } from "@/lib/utils";

/*
	Примитив «Нав-пункт» (ноды apcZH и Vx9C2 «Нав-пункт/Активный», фрейм
	«Примитивы» s0YfZ) перенесён 1:1 с макета design.pen: радиус 8, паддинги
	[8,10], зазор 10, иконка Lucide 16×16, подпись Inter 13/500 textSecondary.
	Активное состояние по мастер-ноде Vx9C2: заливка surface, бордер border,
	иконка и подпись accentStrong 13/600. Примечание: утверждение аудита
	§3.1 «активный пункт — accentSoft/accentStrong» опровергнуто прямым
	чтением нод через MCP pen — пара accentSoft/accentStrong в «Каркасе»
	принадлежит логотипу топбара; бейдж «Входящие» на этой паре — решение
	задачи 3.1 (перекрасится badgeClassName на интеграции). Бейдж — по
	мастер-ноде q35Tj9: pill 999, заливка accent, [2,8], 11/600.
	asChild подключает React Router NavLink без обёрток (задача 3.1).
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

const navItemVariants = cva(
	"inline-flex w-full items-center gap-2.5 rounded-sm px-2.5 py-2 text-[13px] transition-colors whitespace-nowrap",
	{
		variants: {
			active: {
				true: "border bg-card font-semibold text-accent-strong",
				false: "font-medium text-text-secondary",
			},
		},
		compoundVariants: [
			{
				// Иконка наследует цвет пункта, активная — accentStrong (Vx9C2).
				active: true,
				class: "[&_svg]:text-accent-strong",
			},
		],
		defaultVariants: {
			active: false,
		},
	},
);

function NavItem({
	className,
	active = false,
	asChild = false,
	icon: Icon,
	badge,
	badgeClassName,
	children,
	...props
}: React.ComponentProps<"a"> &
	VariantProps<typeof navItemVariants> & {
		asChild?: boolean;
		/** Иконка раздела из Lucide (16×16 по мастер-ноде apcZH). */
		icon: LucideIcon;
		/** Числовой бейдж-счётчик (мастер-нода q35Tj9, в «Каркасе» — «Входящие»). */
		badge?: React.ReactNode;
		/** Классы бейджа: точка перекраски под пару accentSoft/accentStrong (3.1). */
		badgeClassName?: string;
	}) {
	const iconElement = <Icon aria-hidden="true" className="size-4 shrink-0" />;
	const badgeElement =
		badge != null ? (
			<span
				data-slot="nav-item-badge"
				className={cn(
					"inline-flex items-center justify-center rounded-full bg-primary px-2 py-0.5 text-[11px] font-semibold text-primary-foreground",
					badgeClassName,
				)}
			>
				{badge}
			</span>
		) : null;

	if (asChild) {
		// Slottable выносит переданную router-ссылку в корень, иконка и бейдж
		// дописываются внутрь неё в дизайн-порядке «иконка → подпись → бейдж».
		return (
			<Slot
				data-slot="nav-item"
				data-active={active === true ? "" : undefined}
				aria-current={active === true ? "page" : undefined}
				className={cn(navItemVariants({ active, className }))}
				{...props}
			>
				{iconElement}
				<Slottable>{children}</Slottable>
				{badgeElement}
			</Slot>
		);
	}

	return (
		<a
			data-slot="nav-item"
			data-active={active === true ? "" : undefined}
			aria-current={active === true ? "page" : undefined}
			className={cn(navItemVariants({ active, className }))}
			{...props}
		>
			{iconElement}
			<span className="flex min-w-0 flex-1 truncate">{children}</span>
			{badgeElement}
		</a>
	);
}

export { NavItem, navItemVariants };
