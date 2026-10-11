import { describe, expect, it } from "vitest";
import { decodeChatStreamFrame } from "./chat-stream-events";

// Декодер кадров SSE-канала чата зеркалит транспорт задачи 2.1: к кадрам
// start/token/done/error добавился кадр tool с нагрузкой
// { name, phase: "start"|"end", detail? } — деталь при отсутствии
// не сериализуется (WhenWritingNull транспорта).
// Traceability: openspec:http-api/transport#scenario-chat-tool-activity-streamed

describe("декодер кадров SSE-канала чата", () => {
	it("разбирает кадр начала вызова инструмента с деталью", () => {
		// Arrange: транспорт кадрирует старт вызова тула с именем, фазой
		// и опциональной деталью.
		// Traceability: openspec:http-api/transport#scenario-chat-tool-activity-streamed
		const frame = {
			event: "tool",
			data: '{"name":"get_market_snapshot","phase":"start","detail":"BTC"}',
		};

		// Act: кадр декодируется в событие протокола.
		const event = decodeChatStreamFrame(frame);

		// Assert: имя, фаза старта и деталь доезжают в событие без искажений.
		expect(event).toEqual({
			type: "tool",
			name: "get_market_snapshot",
			phase: "start",
			detail: "BTC",
		});
	});

	it("разбирает кадр конца вызова без необязательной детали", () => {
		// Arrange: end-кадр уходит без detail — сериализация пропускает
		// отсутствующие поля, у события поле отсутствует вовсе.
		// Traceability: openspec:http-api/transport#scenario-chat-tool-activity-streamed
		const frame = {
			event: "tool",
			data: '{"name":"read_rule_card","phase":"end"}',
		};

		// Act: кадр декодируется в событие протокола.
		const event = decodeChatStreamFrame(frame);

		// Assert: инструмент погашен фазой конца, детали нет.
		expect(event).toEqual({ type: "tool", name: "read_rule_card", phase: "end" });
		expect(event).not.toHaveProperty("detail");
	});

	it("игнорирует tool-кадр без имени инструмента", () => {
		// Arrange: кадр без имени не является событием протокола.
		const frame = { event: "tool", data: '{"phase":"start"}' };

		// Act: декодер встречает неполный кадр активности.
		const event = decodeChatStreamFrame(frame);

		// Assert: чужой вариант кадра отброшен — как незнакомое имя события.
		expect(event).toBeNull();
	});

	it("игнорирует tool-кадр с незнакомой фазой", () => {
		// Arrange: фаза вне словаря start/end не входит в протокол.
		const frame = { event: "tool", data: '{"name":"get_option_board","phase":"progress"}' };

		// Act: декодер встречает незнакомую фазу.
		const event = decodeChatStreamFrame(frame);

		// Assert: кадр игнорируется ради прямой совместимости.
		expect(event).toBeNull();
	});

	it("игнорирует незнакомые имена событий", () => {
		// Arrange: транспорт может добавить новые события раньше клиента.
		const frame = { event: "usage", data: '{"tokens":42}' };

		// Act: декодер встречает незнакомое имя события.
		const event = decodeChatStreamFrame(frame);

		// Assert: кадр игнорируется без ошибки — канал продолжает работать.
		expect(event).toBeNull();
	});

	it("стрим без tool-кадров декодируется прежними событиями", () => {
		// Arrange: обычный ход start → token → done и ход с ошибкой идут
		// тем же каналом — кадры активности лишь дополняют протокол.
		const frames = [
			{ event: "start", data: "{}" },
			{ event: "token", data: '{"text":"Ответ"}' },
			{ event: "tool", data: '{"name":"get_option_board","phase":"start"}' },
			{ event: "error", data: '{"message":"генерация не удалась"}' },
			{ event: "done", data: "{}" },
		];

		// Act: все кадры декодируются по очереди.
		const events = frames.map((frame) => decodeChatStreamFrame(frame));

		// Assert: не-tool кадры дают прежние события, tool — событие активности.
		expect(events).toEqual([
			{ type: "start" },
			{ type: "token", text: "Ответ" },
			{ type: "tool", name: "get_option_board", phase: "start" },
			{ type: "error", message: "генерация не удалась" },
			{ type: "done", message: undefined },
		]);
	});
});
