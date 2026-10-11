import { cva, type VariantProps } from "class-variance-authority";
import * as React from "react";
import { cn } from "@/lib/utils";

/*
	Примитив «Сессия/пункт» (мастер-нода s9J3h, reusable-компонент design.pen)
	перенесён 1:1 с макета: радиус 10, вертикальный блок с зазором 3, паддинги
	[8,10]; строка Top — заголовок Inter 12.5/600 textPrimary и время 11/normal
	textMuted на краях (space-between, зазор 8); превью — textMuted 11.5/normal,
	одна строка (textGrowth fixed-width → truncate). Превью опционально: список
	сессий формы агентов передаёт в него текст первого сообщения владельца
	(ChatDto.firstMessagePreview), панель чатов карточки рендерит пункты
	без превью. Бейдж (имя привязанной конструкции) — опциональная своя строка
	над превью: пилюля infoSoft, как чип привязки композера bCLlQ; у чата
	без привязки бейдж не передаётся. Корень — кнопка выбора сессии:
	активное состояние — по инстансам Cur (WizmS, экран «Агент · Чат
	активный»: fill surface2 + stroke border) и S1 (V1puZa, «Агент · Чаты»:
	fill surface2 без обводки); выбор сессии — кликом по пункту.
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
	badge,
	children,
	...props
}: React.ComponentProps<"button"> &
	VariantProps<typeof sessionItemVariants> & {
		/** Заголовок сессии: textPrimary 12.5/600 (нода mPoUU). */
		title: React.ReactNode;
		/** Время последнего сообщения: textMuted 11/normal (нода SqhBW). */
		time: React.ReactNode;
		/** Бейдж привязки (имя конструкции): своя строка над превью. */
		badge?: React.ReactNode;
		/** Превью первого сообщения владельца: textMuted 11.5, одна строка (нода r6Idf). */
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
			{badge != null && (
				// Бейдж привязки: пилюля infoSoft, как чип привязки композера bCLlQ;
				// имя конструкции делает пункт опознаваемым без открытия чата.
				// Traceability: openspec:ui/screens#scenario-ui-agent-session-shows-construction-name
				<span data-slot="session-item-badge" className="flex min-w-0">
					<span className="inline-flex max-w-full truncate rounded-full bg-info-soft px-2 py-px text-[10.5px] whitespace-nowrap text-text-secondary">
						{badge}
					</span>
				</span>
			)}
			{children != null && (
				// Превью первого сообщения владельца — одна строка с truncate:
				// сервер отдаёт текст целиком, ужимание вёрсткой происходит здесь.
				// Traceability: openspec:ui/screens#scenario-ui-agent-session-shows-preview
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
