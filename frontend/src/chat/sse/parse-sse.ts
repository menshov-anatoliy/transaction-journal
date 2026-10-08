/** Завершённый кадр SSE: имя события и строковая нагрузка. */
export interface SseFrame {
	readonly event: string;
	readonly data: string;
}

export interface SseFrameParser {
	/** Скармливает очередную порцию потока и отдаёт готовые кадры. */
	push(chunk: string): SseFrame[];
}

// Имя события по спецификации SSE для кадра без строки event:.
const DEFAULT_EVENT_NAME = "message";

/**
 * Потоковый парсер SSE-кадров: накапливает произвольно нарезанные порции
 * ReadableStream, режет буфер по пустой строке-разделителю кадра, собирает
 * строки event:/data: и игнорирует комментарии. Разбор кадра соответствует
 * Server-Sent Events W3C: многострочная нагрузка data соединяется «\n»,
 * переводы CRLF нормализуются, чужие поля (id, retry) не ломают кадр.
 */
export function createSseFrameParser(): SseFrameParser {
	let buffer = "";

	return {
		push(chunk: string): SseFrame[] {
			// CRLF нормализуется к LF до разбиения, чтобы разделитель кадра
			// «\n\n» ловил оба стиля окончаний строк.
			buffer += chunk.replaceAll("\r\n", "\n");

			const frames: SseFrame[] = [];
			// Разделитель кадра — пустая строка; ищем её от начала буфера,
			// пока порция содержит хотя бы один целый кадр.
			for (
				let boundary = buffer.indexOf("\n\n");
				boundary !== -1;
				boundary = buffer.indexOf("\n\n")
			) {
				const rawFrame = buffer.slice(0, boundary);
				buffer = buffer.slice(boundary + 2);
				const frame = parseFrame(rawFrame);
				if (frame !== null)
					frames.push(frame);
			}

			return frames;
		},
	};
}

function parseFrame(rawFrame: string): SseFrame | null {
	let event = DEFAULT_EVENT_NAME;
	const dataLines: string[] = [];

	for (const line of rawFrame.split("\n")) {
		// Комментарий начинается с двоеточия и игнорируется целиком.
		if (line.startsWith(":") || line === "")
			continue;

		const colon = line.indexOf(":");
		const field = colon === -1 ? line : line.slice(0, colon);
		// Значение поля отделяется одним пробелом после двоеточия.
		let value = colon === -1 ? "" : line.slice(colon + 1);
		if (value.startsWith(" "))
			value = value.slice(1);

		if (field === "event")
			event = value;
		else if (field === "data")
			dataLines.push(value);
		// Остальные поля спецификации (id, retry) транспорту чата не нужны.
	}

	// Кадр из одних комментариев или пустых строк событием не считается.
	if (dataLines.length === 0)
		return null;

	return { event, data: dataLines.join("\n") };
}
