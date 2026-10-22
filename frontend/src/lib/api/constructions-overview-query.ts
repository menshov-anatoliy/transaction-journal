import { useQuery, type UseQueryResult } from "@tanstack/react-query";
import { fetchConstructionsOverview, type ConstructionsOverview } from "./constructions";

/**
 * Ключ запроса обзора «Конструкций» — единая константа для итога в топбаре
 * каркаса и шапки экрана списка: оба места читают один кэш React Query,
 * поэтому дублирующих запросов не появляется, а инвалидации синхронизации
 * и прохода подсказок обновляют итог везде одновременно.
 */
// Traceability: openspec:ui/screens#scenario-journal-total-always-visible
export const constructionsOverviewQueryKey = ["constructions-overview"] as const;

/**
 * Обзор раздела «Конструкции» из общего кэша: сводка журнала (итог с
 * разбивкой на реализованный и нереализованный P&L, признак сбоя котировок)
 * и строки таблицы одним ответом API.
 */
export function useConstructionsOverviewQuery(): UseQueryResult<ConstructionsOverview> {
	return useQuery({
		queryKey: constructionsOverviewQueryKey,
		queryFn: fetchConstructionsOverview,
	});
}
