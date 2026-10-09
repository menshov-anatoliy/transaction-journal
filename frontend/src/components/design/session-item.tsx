import { cva, type VariantProps } from "class-variance-authority";
import * as React from "react";
import { cn } from "@/lib/utils";

/*
	Примитив «Сессия/пункт» (мастер-нода s9J3h, reusable-компонент design.pen)
	перенесён 1:1 с макета: радиус 10, вертикальный блок с зазором 3, паддинги
	[8,10]; строка Top — заголовок Inter 12.5/600 textPrimary и время 11/normal
	textMuted на краях (space-between, зазор 8); превью — textMuted 11.5/normal,
	одна строка (textGrowth fixed-width → truncate). Превью опционально: ChatDto
	чат-слоя несёт заголовок (params.model) и lastMessageAt, но не превью
	сообщения. Корень — кнопка выбора сессии: интеграция задачи 5.2 повесит
	на неё onSelect списка чатов. Активное состояние — по инстансам Cur (WizmS,
	экран «Агент · Чат активный»: fill surface2 + stroke border) и S1 (V1puZa,
	«Агент · Чаты»: fill surface2 без обводки); бордер взят с Cur как более
	полной формы текущей сессии.
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

const sessionItemVariants = cva(
	"flex w-full cursor-pointer flex-col gap-[3px] rounded-md px-2.5 py-2 text-left",
	{
		variants: {
			active: {
				true: "border bg-surface-2",
				false: "",
			},
		},
		defaultVariants: {
			active: false,
		},
	},
);

function SessionItem({
	className,
	active,
	title,
	time,
	children,
	...props
}: React.ComponentProps<"button"> &
	VariantProps<typeof sessionItemVariants> & {
		/** Заголовок сессии: textPrimary 12.5/600 (нода mPoUU). */
		title: React.ReactNode;
		/** Время последнего сообщения: textMuted 11/normal (нода SqhBW). */
		time: React.ReactNode;
		/** Превью последнего сообщения: textMuted 11.5, одна строка (нода r6Idf). */
		children?: React.ReactNode;
	}) {
	return (
		<button
			type="button"
			data-slot="session-item"
			data-active={active === true ? "" : undefined}
			aria-current={active === true ? "true" : undefined}
			className={cn(sessionItemVariants({ active, className }))}
			{...props}
		>
			<span className="flex items-center justify-between gap-2">
				<span className="min-w-0 flex-1 truncate text-[12.5px] font-semibold text-text-primary">
					{title}
				</span>
				<span className="shrink-0 text-[11px] whitespace-nowrap text-text-muted">
					{time}
				</span>
			</span>
			{children != null && (
				<span
					data-slot="session-item-preview"
					className="truncate text-[11.5px] text-text-muted"
				>
					{children}
				</span>
			)}
		</button>
	);
}

export { SessionItem, sessionItemVariants };
