import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { TradeSourceBadge } from "./trade-source-badge";

// Бейдж источника по мастер-нодам «Бейдж:биржа»/«Бейдж:ручная» макета
// design.pen: пилюля с подписью источника; биржевая запись — поверхность
// с каймой $border, ручная — $accentSofter с каймой $accentSoft.
// Traceability: openspec:ui/screens#scenario-source-badge-shown

describe("бейдж источника сделки", () => {
	it("оформляет биржевую запись поверхностью и подписью «биржа»", () => {
		// Act: рендер бейджа биржевого источника.
		render(<TradeSourceBadge source="exchange" />);

		// Assert: подпись мастера и токены биржевого оформления.
		const badge = screen.getByText("биржа").closest("span")!;
		expect(badge.className).toContain("rounded-full");
		expect(badge.className).toContain("bg-surface");
		expect(badge.className).toContain("border-border");
		// Иконка биржи тонирована $textMuted правилом родителя.
		expect(badge.className).toContain("[&_svg]:text-text-muted");
	});

	it("оформляет ручную запись акцентом и подписью «ручная»", () => {
		// Act: рендер бейджа ручного источника.
		render(<TradeSourceBadge source="manual" />);

		// Assert: подпись мастера и токены ручного оформления.
		const badge = screen.getByText("ручная").closest("span")!;
		expect(badge.className).toContain("bg-accent-softer");
		expect(badge.className).toContain("border-accent-soft");
		// Иконка карандаша тонирована $accentStrong правилом родителя.
		expect(badge.className).toContain("[&_svg]:text-accent-strong");
	});
});
