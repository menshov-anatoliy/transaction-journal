import { Landmark, Pencil } from "lucide-react";
import type { TradeSource } from "@/lib/api/manual-trades";
import { cn } from "@/lib/utils";

// Бейдж источника сделки по мастер-нодам «Бейдж:биржа»/«Бейдж:ручная»
// макета design.pen: пилюля радиуса 999 с подписью 12/normal $textSecondary;
// биржевая — заливка $surface, кайма $border, иконка landmark $textMuted;
// ручная — заливка $accentSofter, кайма $accentSoft, иконка pencil
// $accentStrong.
// Traceability: openspec:ui/screens#scenario-source-badge-shown

/** Оформление и подпись бейджа по источнику записи. */
const sourceMeta: Record<TradeSource, { label: string; Icon: typeof Landmark; className: string }> = {
	exchange: {
		label: "биржа",
		Icon: Landmark,
		className: "border-border bg-surface [&_svg]:text-text-muted",
	},
	manual: {
		label: "ручная",
		Icon: Pencil,
		className: "border-accent-soft bg-accent-softer [&_svg]:text-accent-strong",
	},
};

/** Пилюля источника записи таблицы: «биржа» либо «ручная». */
export function TradeSourceBadge({ source }: { source: TradeSource }) {
	const meta = sourceMeta[source];
	return (
		<span
			className={cn(
				"inline-flex shrink-0 items-center gap-1.5 rounded-full border px-2 py-[2px] text-[11.5px] text-text-secondary",
				meta.className,
			)}
		>
			<meta.Icon aria-hidden className="size-3" />
			{meta.label}
		</span>
	);
}
