import type { ConstructionStatus } from "@/lib/api/constructions";
import { StatusChip } from "@/components/design";

// Единый статус конструкции для таблицы, узкого списка и карточки.
// Тон пилюли по инстансам макета: «открыта» — pos (X7CR1q,
// accentSoft/accentStrong), «закрыта» — neutral (EIqx3,
// surface2/textSecondary), «архив» — muted (dAcLW, surface2/textMuted).
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
const statuses = {
	open: { label: "открыта", tone: "pos" },
	closed: { label: "закрыта", tone: "neutral" },
	archived: { label: "архив", tone: "muted" },
} as const;

export function ConstructionStatusChip({ status }: { status: ConstructionStatus }) {
	const { label, tone } = statuses[status];
	return <StatusChip tone={tone}>{label}</StatusChip>;
}
