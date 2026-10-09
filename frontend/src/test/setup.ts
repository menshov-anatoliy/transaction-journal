import "@testing-library/jest-dom/vitest";
import { cleanup } from "@testing-library/react";
import { afterEach } from "vitest";

// Vitest запускается без globals, поэтому очистка DOM между тестами
// регистрируется явно.
afterEach(cleanup);

// jsdom не реализует ResizeObserver, а примитивы assistant-ui (viewport
// ленты сообщений) наблюдают за размером контейнера. Тестам достаточно
// бездействующего наблюдателя: реальные пересчёты прокрутки не проверяются.
class ResizeObserverStub implements ResizeObserver {
	observe(): void {}
	unobserve(): void {}
	disconnect(): void {}
}

if (typeof globalThis.ResizeObserver === "undefined") {
	globalThis.ResizeObserver = ResizeObserverStub;
}

// jsdom не реализует Element.prototype.scrollTo — автоскролл ленты сообщений
// assistant-ui дергает его в requestAnimationFrame после обновления истории.
// Прокрутка в тестах не проверяется, поэтому достаточно бездействующей заглушки.
if (typeof Element.prototype.scrollTo === "undefined") {
	Element.prototype.scrollTo = (): void => {};
}
