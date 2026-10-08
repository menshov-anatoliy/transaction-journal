import { Slot } from "@radix-ui/react-slot";
import { cva, type VariantProps } from "class-variance-authority";
import * as React from "react";
import { cn } from "@/lib/utils";

// Компонент shadcn/ui: код живёт в репозитории (ADR-0010), тема —
// в src/index.css через CSS-переменные Tailwind CSS 4.

/*
	Тема кнопок перенесена 1:1 с дизайн-примитивов «Кнопка/*» фрейма
	«Примитивы» (s0YfZ) макета design.pen: радиус 8 (шаг --radius-sm лестницы
	--radius), шрифт Inter 13/500, паддинги 9/16, без теней. Соответствие
	вариантов shadcn → дизайн-ноды:
	  default     → Кнопка/Primary   (заливка accent #1FA36B, текст surface)
	  outline     → Кнопка/Secondary (заливка surface, бордер border #E5E5DF,
	                текст textPrimary)
	  ghost       → Кнопка/Ghost     (без заливки, текст textSecondary,
	                паддинг 9/12)
	  destructive → Кнопка/Danger    (заливка negSoft #FBEAE7, текст neg #D14B41)
	«secondary» — прямое имя дизайн-варианта Secondary, дублирует вид «outline»
	(белая поверхность с бордером). Hover-состояний в макете нет: они выведены
	из соседних токенов дизайн-системы (accentStrong — Primary, surface2 —
	Secondary/Ghost, neg/15 — Danger).
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: openspec:ui/design-system#requirement-visual-layer-uses-design-tokens
// Traceability: change:reconcile-frontend-with-design/design#D2

const buttonVariants = cva(
	"inline-flex shrink-0 items-center justify-center gap-2 whitespace-nowrap rounded-sm text-[13px] font-medium transition-all outline-none focus-visible:border-ring focus-visible:ring-ring/50 focus-visible:ring-[3px] disabled:pointer-events-none disabled:opacity-50 aria-invalid:border-destructive aria-invalid:ring-destructive/20 [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-4",
	{
		variants: {
			variant: {
				default: "bg-primary text-primary-foreground hover:bg-accent-strong",
				destructive:
					"bg-neg-soft text-neg hover:bg-neg/15 focus-visible:ring-neg/20",
				outline:
					"border bg-card text-foreground hover:bg-surface-2",
				secondary:
					"border bg-card text-foreground hover:bg-surface-2",
				ghost: "text-text-secondary hover:bg-surface-2 hover:text-foreground",
				link: "text-primary underline-offset-4 hover:underline",
			},
			size: {
				default: "h-9 px-4 py-2 has-[>svg]:px-3",
				sm: "h-8 gap-1.5 px-3",
				lg: "h-10 px-6",
				icon: "size-9",
			},
		},
		compoundVariants: [
			// Ghost в макете компактнее остальных: горизонтальный паддинг 12,
			// а не 16 (нода Kn5dY, паддинг [9,12]).
			{
				variant: ["ghost"],
				size: "default",
				class: "px-3",
			},
		],
		defaultVariants: {
			variant: "default",
			size: "default",
		},
	},
);

function Button({
	className,
	variant,
	size,
	asChild = false,
	...props
}: React.ComponentProps<"button"> &
	VariantProps<typeof buttonVariants> & {
		asChild?: boolean;
	}) {
	const Comp = asChild ? Slot : "button";

	return (
		<Comp
			data-slot="button"
			className={cn(buttonVariants({ variant, size, className }))}
			{...props}
		/>
	);
}

export { Button, buttonVariants };
