import { afterEach, describe, expect, it, vi } from "vitest";
import { fetchHintsLog } from "./hints";

// Контракт журнала подсказок раздела /hints: фильтры и пагинация кодируются
// query-string под единым префиксом /api/v1.
// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api

afterEach(() => {
	vi.unstubAllGlobals();
});

describe("api подсказок", () => {
	it("кодирует фильтры журнала подсказок в query-string", async () => {
		const fetchMock = vi.fn(async () =>
			new Response(JSON.stringify({ items: [], total: 0, limit: 200, offset: 0 }), {
				status: 200,
				headers: { "Content-Type": "application/json" },
			}),
		);
		vi.stubGlobal("fetch", fetchMock);

		await fetchHintsLog({
			status: "new",
			group: "futures-leg",
			character: "futures-leg",
			limit: 50,
			offset: 10,
		});

		expect(fetchMock).toHaveBeenCalledWith(
			"/api/v1/hints/log?status=new&group=futures-leg&character=futures-leg&limit=50&offset=10",
			expect.objectContaining({ headers: expect.anything() }),
		);
	});
});
