import { QueryClient } from "@tanstack/react-query";

/**
 * Клиент слоя запросов SPA. Живость данных по концепции §1 обеспечит
 * серверный канал событий после мутаций и фоновых изменений, поэтому
 * перезапрос по фокусу окна выключен; кэш умеренно свежий, повторы
 * при сбоях ограничены.
 */
export function createAppQueryClient(): QueryClient {
	return new QueryClient({
		defaultOptions: {
			queries: {
				staleTime: 30_000,
				refetchOnWindowFocus: false,
				retry: 1,
			},
		},
	});
}
