import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { StatusChip, statusChipVariants } from "./status-chip";

// Проверяется примитив «Чип/Статус» (нода aa6cK, фрейм «Примитивы» s0YfZ)
// макета design.pen: pill радиус 999, паддинги [4,10], шрифт Inter 11.5/500
// и тональные варианты, извлечённые из инстансов дизайн-нод: pos —
// accentSoft/accentStrong («открыта»), info — infoSoft/info («Однозначное»),
// neutral — surface2/textSecondary («закрыта»), muted — surface2/textMuted
// («архив»), neg — negSoft/neg («Отклонено», карточки Body #6). Пиксельная
// сверка — парой скриншотов в
// .wf-research/design-audit/screenshots/02-primitives/design-components/.
// Traceability: openspec:ui/design-system#requirement-reusable-design-primitives
// Traceability: change:reconcile-frontend-with-design/design#D2

describe("StatusChip: тон статусной пилюли по дизайн-нодам", () => {
	it("несёт геометрию и типографику примитива: pill 999, [4,10], 11.5/500", () => {
		// Act: базовый рендер без переопределений.
		render(<StatusChip>открыта</StatusChip>);

		// Assert: содержимое видно, классы геометрии из ноды aa6cK.
		const chip = screen.getByText("открыта");
		expect(chip.className).toContain("rounded-full");
		expect(chip.className).toContain("px-2.5");
		expect(chip.className).toContain("py-1");
		expect(chip.className).toContain("text-[11.5px]");
		expect(chip.className).toContain("font-medium");
		// Интерлиньяж мастера — шрифтовой normal: пилюля не растягивается
		// унаследованным line-height контекста (напр. титулом 21/28).
		expect(chip.className).toContain("leading-[normal]");
	});

	it.each([
		// Тон → ключевые классы заливки/текста из инстансов дизайн-нод.
		["pos", "bg-accent-soft text-accent-strong"],
		["info", "bg-info-soft text-info"],
		["neutral", "bg-surface-2 text-text-secondary"],
		["muted", "bg-surface-2 text-text-muted"],
		// Инстансы Body #6 (rdODt/AutfO): чип «Отклонено» — negSoft/neg.
		["neg", "bg-neg-soft text-neg"],
	] as const)("тон %s разрешается в токены дизайн-системы", (tone, expected) => {
		// Act + Assert: cva-маппинг тон→классы без рендера.
		expect(statusChipVariants({ tone })).toContain(expected);
	});

	it("по умолчанию берёт тон pos: accentSoft/accentStrong", () => {
		// Act: рендер без явного тона.
		render(<StatusChip>Применено</StatusChip>);

		// Assert: мастер-нода aa6cK использует пару accentSoft/accentStrong.
		expect(screen.getByText("Применено").className).toContain("bg-accent-soft");
		expect(screen.getByText("Применено").className).toContain("text-accent-strong");
	});

	it("явный тон перекрашивает пилюлю", () => {
		// Act: нейтральный тон статуса «закрыта» из таблицы конструкций.
		render(<StatusChip tone="neutral">закрыта</StatusChip>);

		// Assert: инстанс EIqx3 — surface2/textSecondary, не акцентная пара.
		const classes = screen.getByText("закрыта").className;
		expect(classes).toContain("bg-surface-2");
		expect(classes).not.toContain("bg-accent-soft");
	});
});
