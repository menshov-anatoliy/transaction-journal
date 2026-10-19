import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { ConstructionCardTrade } from "@/lib/api/construction-card";
import { ManualSyntheticLinkForm } from "./manual-synthetic-link-form";

// Форма ручной синтетической связки по мастеру «Мастер/Синтетика · форма
// ручной связки» (vG1f8) design.pen: выбор линейной сделки и встречного
// опциона из сделок конструкции, количество, футер «Отмена» +
// «Создать связку»; правила пары (одна конструкция, совпадение страйка
// и экспирации, согласованное количество) проверяет сервер — причина
// отказа показывается формой.
// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link

/** Сделки конструкции: биржевая линейная, биржевой опцион и ручная линейная. */
const trades: readonly ConstructionCardTrade[] = [
	{
		execId: "exec-linear-1",
		symbol: "ETHUSDT",
		executedAt: "2026-06-01T10:00:00+00:00",
		isBuy: true,
		quantity: 0.5,
		price: 2412,
		amountUsdt: 1206,
		fee: 0.5,
		comment: null,
		source: "exchange",
		manualTradeId: null,
	},
	{
		execId: "exec-option-1",
		symbol: "ETH-26DEC-3000-P",
		executedAt: "2026-06-01T10:05:00+00:00",
		isBuy: false,
		quantity: 0.5,
		price: 118.5,
		amountUsdt: 59.25,
		fee: 0.3,
		comment: null,
		source: "exchange",
		manualTradeId: null,
	},
	{
		execId: null,
		symbol: "ETHUSDT",
		executedAt: "2026-06-02T12:00:00+00:00",
		isBuy: true,
		quantity: 0.2,
		price: 2400,
		amountUsdt: 480,
		fee: 0.2,
		comment: null,
		source: "manual",
		manualTradeId: 5,
	},
];

function renderForm(overrides?: Partial<Parameters<typeof ManualSyntheticLinkForm>[0]>) {
	const onSubmit = vi.fn();
	const onCancel = vi.fn();
	render(
		<ManualSyntheticLinkForm
			trades={trades}
			pending={false}
			error={null}
			onSubmit={onSubmit}
			onCancel={onCancel}
			{...overrides}
		/>,
	);
	return { onSubmit, onCancel };
}

describe("форма ручной синтетической связки", () => {
	it("показывает пустые состояния выбора и плейсхолдер количества по мастеру", () => {
		// Act: форма в пустом состоянии мастера.
		renderForm();

		// Assert: плейсхолдеры выбора и количества совпадают с макетом.
		expect(screen.getByText("Выберите линейную сделку…")).toBeTruthy();
		expect(screen.getByText("Выберите встречный опцион…")).toBeTruthy();
		expect(screen.getByPlaceholderText("Не больше каждой из сделок")).toBeTruthy();
		expect(screen.getByText("страйк и экспирация должны совпадать с закрываемой ногой")).toBeTruthy();
	});

	it("предлагает обе стороны связки из сделок конструкции, включая ручные", () => {
		// Act: рендер формы с тремя сделками конструкции.
		renderForm();

		// Assert: подписи опций включают биржевые и ручную сделку — связка
		// соединяет любые две сделки конструкции по единому ключу.
		const linearOptions = [...screen.getByLabelText<HTMLSelectElement>("Линейная сделка связки").options];
		expect(linearOptions.map((option) => option.value)).toEqual(["", "exec-linear-1", "exec-option-1", "manual:5"]);
	});

	it("отправляет выбранную пару и количество с едиными ключами сделок", async () => {
		// Сценарий «Связка создаётся из двух сделок конструкции»: выбор
		// линейной сделки, встречного опциона и количества уходит команде
		// создания; запятая разобрана десятичным разделителем.
		// Traceability: openspec:domain/synthetic-close#scenario-manual-link-created
		// Arrange: форма с выбранной парой сделок.
		const user = userEvent.setup();
		const { onSubmit } = renderForm();
		await user.selectOptions(screen.getByLabelText("Линейная сделка связки"), "exec-linear-1");
		await user.selectOptions(screen.getByLabelText("Встречный опцион связки"), "manual:5");
		await user.type(screen.getByLabelText("Количество связки"), "0,3");

		// Act: отправка формы.
		await user.click(screen.getByRole("button", { name: "Создать связку" }));

		// Assert: ключи сделок и количество переданы как в контракте API.
		expect(onSubmit).toHaveBeenCalledTimes(1);
		expect(onSubmit.mock.calls[0][0]).toEqual({
			linearTradeId: "exec-linear-1",
			optionTradeId: "manual:5",
			quantity: 0.3,
		});
	});

	it("не отправляет форму, пока не выбраны обе сделки", () => {
		// Arrange: количество не влияет на блокировку — сделки не выбраны.
		renderForm();

		// Assert: действие создания заблокировано пустыми выборами.
		expect(screen.getByRole("button", { name: "Создать связку" })).toBeDisabled();
	});

	it("оставляет некорректное количество в форме с пояснением", async () => {
		// Arrange: выбранная пара и нечисловое количество.
		const user = userEvent.setup();
		const { onSubmit } = renderForm();
		await user.selectOptions(screen.getByLabelText("Линейная сделка связки"), "exec-linear-1");
		await user.selectOptions(screen.getByLabelText("Встречный опцион связки"), "exec-option-1");
		await user.type(screen.getByLabelText("Количество связки"), "пол-ноги");

		// Act: попытка отправки.
		await user.click(screen.getByRole("button", { name: "Создать связку" }));

		// Assert: форма не отправлена, показано локальное пояснение.
		expect(onSubmit).not.toHaveBeenCalled();
		expect(screen.getByRole("alert")).toHaveTextContent("Введите количество связки числом.");
	});

	it("показывает причину отказа серверной валидации", () => {
		// Сценарий «Рассогласованные инструменты или количества отклоняются»:
		// причина отказа сервера доступна как alert в форме.
		// Traceability: openspec:domain/synthetic-close#scenario-manual-link-rejected-on-mismatch
		// Act: форма с ошибкой сервера.
		renderForm({ error: "Количество связки 0.9 не согласовано со сделками: линейная 0.5, встречный опцион 0.5." });

		// Assert: человекочитаемая причина доступна как alert.
		expect(screen.getByRole("alert")).toHaveTextContent("не согласовано со сделками");
	});

	it("показывает пояснение правила связки в заполненном состоянии", async () => {
		// Arrange: форма с заполненными полями, как в мастере vG1f8.
		const user = userEvent.setup();
		renderForm();
		await user.selectOptions(screen.getByLabelText("Линейная сделка связки"), "exec-linear-1");
		expect(screen.queryByText(/Связка фиксирует паритетный лок/)).toBeNull();

		// Act: выбор опциона и количества доводит форму до заполненного состояния.
		await user.selectOptions(screen.getByLabelText("Встречный опцион связки"), "exec-option-1");
		await user.type(screen.getByLabelText("Количество связки"), "0.3");

		// Assert: подпись-пояснение правила появилась.
		expect(screen.getByText(/Связка фиксирует паритетный лок/)).toBeTruthy();
	});

	it("закрывает форму отменой и крестиком без отправки", async () => {
		// Arrange: форма с обработчиками.
		const user = userEvent.setup();
		const { onCancel, onSubmit } = renderForm();

		// Act: отмена футером и закрытие по крестику.
		await user.click(screen.getByRole("button", { name: "Отмена" }));
		await user.click(screen.getByRole("button", { name: "Закрыть форму ручной синтетической связки" }));

		// Assert: форма закрыта дважды, отправки не было.
		expect(onCancel).toHaveBeenCalledTimes(2);
		expect(onSubmit).not.toHaveBeenCalled();
	});
});
