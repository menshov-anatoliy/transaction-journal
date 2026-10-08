/*
	Единая точка экспорта библиотеки примитивов дизайн-системы design.pen
	(design.md D2): страницы импортируют примитивы только отсюда, чтобы
	состав библиотеки контролировался одним модулем. Примитивы перенесены
	с reusable-нод фрейма «Примитивы» (s0YfZ) макета design.pen.
*/
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

export { Metric } from "./metric";
export { NavItem, navItemVariants } from "./nav-item";
export { SourceChip } from "./source-chip";
export { StatusChip, statusChipVariants } from "./status-chip";
