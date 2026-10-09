import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { HintPanel, HintRecord } from "@/lib/api/hints";
import { HintsPanel } from "./hints-panel";

// Проверяется панель подсказок правой области по концепции §3: живые подсказки
// группами справочника v1, карточки полного состава, кнопки «Применено»/
// «Отклонено», свёрнутая история, явное пустое состояние.
// Traceability: doc:.wf-research/ui-concept/concept.md#3-раздел-конструкции-маршрут-

const liveHint: HintRecord = {
	id: 5,
	ruleId: "risk-limit-week",
	character: "risk-mode",
	clarity: "crisp",
	sources: [{ tag: "ПИ", file: "risk/limits.md", quotes: ["Лимит риска недели"] }],
	text: "Лимит риска недели достигнут: 2.1% использовано",
	facts: { weekPnl: "2.1%" },
	asOf: "2026-06-20T14:30:00",
	status: "new",
	firstSeenAt: null,
};

const appliedHint: HintRecord = {
	id: 9,
	ruleId: "roll-window",
	character: "rolling",
	clarity: "fuzzy",
	sources: [],
	text: "Окно ролла закрыто",
	facts: {},
	asOf: "2026-06-19T10:00:00",
	status: "applied",
	firstSeenAt: "2026-06-19T10:05:00",
};

const panel: HintPanel = {
	subject: { kind: "journal", constructionId: null },
	liveGroups: [{ group: { id: "risk-mode", title: "Риск-режим" }, hints: [liveHint] }],
	history: [appliedHint],
	liveCount: 1,
};

describe("панель подсказок", () => {
	it("показывает живую подсказку группой справочника с полным составом", () => {
		// Act: панель с одной живой подсказкой.
		render(<HintsPanel panel={panel} onApply={() => {}} onDismiss={() => {}} />);

		// Assert: заголовок группы и карточка полного состава.
		expect(screen.getByRole("heading", { name: "Риск-режим" })).toBeInTheDocument();
		expect(screen.getByText(/Лимит риска недели достигнут/)).toBeInTheDocument();
		expect(screen.getByText("risk-limit-week")).toBeInTheDocument();
		expect(screen.getByText("2026-06-20 14:30")).toBeInTheDocument();
		expect(screen.getByText("живая")).toBeInTheDocument();
	});

	it("предлагает кнопки «Применено» и «Отклонено» у живой подсказки", async () => {
		// Arrange: обработчики команд панели.
		const user = userEvent.setup();
		const onApply = vi.fn();
		const onDismiss = vi.fn();
		render(<HintsPanel panel={panel} onApply={onApply} onDismiss={onDismiss} />);

		// Act: кнопки завершения жизненного цикла живой подсказки.
		await user.click(screen.getByRole("button", { name: "Применено" }));
		await user.click(screen.getByRole("button", { name: "Отклонено" }));

		// Assert: команды ушли идентификаторами записи.
		expect(onApply).toHaveBeenCalledWith(5);
		expect(onDismiss).toHaveBeenCalledWith(5);
	});

	it("держит терминальную историю свёрнутой и раскрывает по кнопке", async () => {
		// Arrange: пользователь для раскрытия истории.
		const user = userEvent.setup();
		render(<HintsPanel panel={panel} onApply={() => {}} onDismiss={() => {}} />);

		// Assert: терминальная запись скрыта до раскрытия.
		expect(screen.queryByText(/Окно ролла закрыто/)).not.toBeInTheDocument();

		// Act: раскрытие истории.
		await user.click(screen.getByRole("button", { name: /история \(1\)/i }));

		// Assert: запись истории видна со своим статусом и без кнопок.
		expect(screen.getByText(/Окно ролла закрыто/)).toBeInTheDocument();
		expect(screen.getByText("применена")).toBeInTheDocument();
		const historyCard = screen.getByText(/Окно ролла закрыто/).closest("article")!;
		expect(within(historyCard).queryByRole("button")).toBeNull();
	});

	it("пустая панель показывает явное сообщение", () => {
		// Act: панель субъекта без подсказок.
		const emptyPanel: HintPanel = {
			subject: { kind: "journal", constructionId: null },
			liveGroups: [],
			history: [],
			liveCount: 0,
		};
		render(<HintsPanel panel={emptyPanel} onApply={() => {}} onDismiss={() => {}} />);

		// Assert: сообщение отсутствия, а не пустая разметка.
		expect(screen.getByText("подсказок нет")).toBeInTheDocument();
	});
});
