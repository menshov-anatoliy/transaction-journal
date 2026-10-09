import { describe, expect, it } from "vitest";
import { createSseFrameParser } from "./parse-sse";

// Парсер SSE-кадров воспроизводит транспорт задачи 2.1: сервер кадрирует
// события чата как «event: имя\ndata: JSON\n\n» и flush-ит каждый кадр —
// клиент обязан собирать кадры из произвольно нарезанных порций ReadableStream.
// Traceability: openspec:http-api/transport#scenario-chat-tokens-stream-over-sse

describe("парсер SSE-кадров", () => {
	it("разбирает завершённый кадр события и нагрузки", () => {
		// Arrange: кадр токена в формате транспорта 2.1 приходит одной порцией.
		const parser = createSseFrameParser();

		// Act: порция скармливается парсеру.
		const frames = parser.push('event: token\ndata: {"text":"Привет"}\n\n');

		// Assert: кадр собран с именем события и JSON-нагрузкой одной строкой.
		expect(frames).toEqual([
			{ event: "token", data: '{"text":"Привет"}' },
		]);
	});

	it("собирает кадр, разрезанный границей порций ReadableStream", () => {
		// Arrange: транспорт пишет кадры маленькими flush-ами — граница порции
		// может разрезать кадр в любом месте, включая середину разделителя.
		const parser = createSseFrameParser();

		// Act: кадр приходит тремя порциями.
		const first = parser.push("event: tok");
		const second = parser.push('en\ndata: {"text":"ми"}\n');
		const third = parser.push("\n");

		// Assert: до полного кадра порции не дают ни одного события, затем
		// кадр собирается ровно один раз.
		expect(first).toEqual([]);
		expect(second).toEqual([]);
		expect(third).toEqual([{ event: "token", data: '{"text":"ми"}' }]);
	});

	it("отдаёт несколько кадров из одной порции", () => {
		// Arrange: буфер сети отдал сразу два кадра.
		const parser = createSseFrameParser();
		const chunk = 'event: token\ndata: {"text":"а"}\n\nevent: done\ndata: {}\n\n';

		// Act: порция разбирается целиком.
		const frames = parser.push(chunk);

		// Assert: кадры приходят в порядке следования в потоке.
		expect(frames).toEqual([
			{ event: "token", data: '{"text":"а"}' },
			{ event: "done", data: "{}" },
		]);
	});

	it("склеивает многострочную нагрузку data переносами строки", () => {
		// Arrange: по спецификации SSE нагрузка может занимать несколько
		// строк data — они соединяются «\n».
		const parser = createSseFrameParser();

		// Act: кадр с двумя строками data.
		const frames = parser.push("event: token\ndata: первая\ndata: вторая\n\n");

		// Assert: строки data объединены в одну нагрузку.
		expect(frames).toEqual([{ event: "token", data: "первая\nвторая" }]);
	});

	it("игнорирует комментарии и присваивает кадру имя message без event-строки", () => {
		// Arrange: keep-alive прокси и кадры без имени события — допустимая
		// спецификацией SSE помеха, ломать разбор она не должна.
		const parser = createSseFrameParser();

		// Act: комментарий keep-alive и безымянный кадр.
		const frames = parser.push(": keep-alive\ndata: {}\n\n");

		// Assert: комментарий отброшен, кадр получил имя по умолчанию.
		expect(frames).toEqual([{ event: "message", data: "{}" }]);
	});

	it("понимает разделители кадров с CRLF", () => {
		// Arrange: некоторые прокси нормализуют переводы строк в CRLF.
		const parser = createSseFrameParser();

		// Act: кадр с CR LF окончаниями строк и разделителем.
		const frames = parser.push('event: token\r\ndata: {"text":"б"}\r\n\r\n');

		// Assert: кадр распознан так же, как с LF.
		expect(frames).toEqual([{ event: "token", data: '{"text":"б"}' }]);
	});

	it("пустая нагрузка кадра разбирается пустой строкой", () => {
		// Arrange: транспорт 2.1 публикует кадры без данных пустым JSON-объектом,
		// но парсер — общий слой кадрирования и не обязан знать об этом.
		const parser = createSseFrameParser();

		// Act: строка data без значения.
		const frames = parser.push("event: start\ndata\n\n");

		// Assert: пустое значение data — пустая строка, кадр валиден.
		expect(frames).toEqual([{ event: "start", data: "" }]);
	});
});
