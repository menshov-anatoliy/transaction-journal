import { describe, expect, it } from "vitest";
import { sanitizeUrl } from "./sanitize-url";

// Проверяется политика ссылок журнального текста: разрешены только http(s),
// mailto и относительные адреса без схемы, небезопасные схемы вырезаются.
// Traceability: doc:.wf-research/ui-concept/concept.md#10-markdown-политика

describe("sanitizeUrl", () => {
	it("сохраняет ссылки на https и http", () => {
		// Act + Assert: безопасные схемы проходят без изменений.
		expect(sanitizeUrl("https://bybit.com/example")).toBe(
			"https://bybit.com/example",
		);
		expect(sanitizeUrl("http://example.com")).toBe("http://example.com");
	});

	it("сохраняет mailto-ссылки", () => {
		// Act + Assert: почтовая схема входит в белый список.
		expect(sanitizeUrl("mailto:user@example.com")).toBe(
			"mailto:user@example.com",
		);
	});

	it("сохраняет относительные адреса и якоря", () => {
		// Act + Assert: адреса без схемы — навигация внутри приложения.
		expect(sanitizeUrl("/constructions/42")).toBe("/constructions/42");
		expect(sanitizeUrl("#section")).toBe("#section");
		expect(sanitizeUrl("page.md?a=1")).toBe("page.md?a=1");
	});

	it("вырезает javascript-схему", () => {
		// Arrange: классический вектор XSS через href.
		const url = "javascript:alert(1)";

		// Act + Assert: схема вырезается целиком, не частично.
		expect(sanitizeUrl(url)).toBe("");
	});

	it("вырезает javascript-схему в любом регистре и с управляющими символами", () => {
		// Act + Assert: браузеры игнорируют табы/переводы строк внутри схемы,
		// поэтому проверка схемы идёт по адресу без управляющих символов.
		expect(sanitizeUrl("JAVASCRIPT:alert(1)")).toBe("");
		expect(sanitizeUrl("java\tscript:alert(1)")).toBe("");
		expect(sanitizeUrl("java\nscript:alert(1)")).toBe("");
	});

	it("вырезает data- и vbscript-схемы", () => {
		// Act + Assert: прочие опасные схемы также вырезаются.
		expect(sanitizeUrl("data:text/html;base64,PHNjcmlwdD4=")).toBe("");
		expect(sanitizeUrl("vbscript:msgbox")).toBe("");
	});
});
