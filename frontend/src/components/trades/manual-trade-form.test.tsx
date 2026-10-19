import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { ManualTradeForm } from "./manual-trade-form";

// Форма ручной сделки по мастеру «Мастер/Форма ручной сделки · состояния»
// (XJapZ) design.pen: чип цели строки «Попадёт», поля даты/времени, символа,
// стороны, количества, цены и комиссии, футер «Отмена» + действие; валидация
// значений выполняется на сервере, причина отказа показывается под формой.
// Traceability: openspec:ui/screens#requirement-manual-trade-creation-ui

function renderForm(overrides?: Partial<Parameters<typeof ManualTradeForm>[0]>) {
	const onSubmit = vi.fn();
	const onCancel = vi.fn();
	render(
		<ManualTradeForm
			target={{ constructionName: null }}
			submitLabel="Создать"
			pending={false}
			error={null}
			onSubmit={onSubmit}
			onCancel={onCancel}
			{...overrides}
		/>,
	);
	return { onSubmit, onCancel };
}

describe("форма ручной сделки", () => {
	it("показывает чип цели «Во «Входящие»» для общего пула", () => {
		// Act: форма создания из «Входящих».
		renderForm();

		// Assert: строка «Попадёт» называет общий пул.
		expect(screen.getByText("Попадёт")).toBeInTheDocument();
		expect(screen.getByText("Во «Входящие»")).toBeInTheDocument();
	});

	it("показывает чип цели с именем конструкции", () => {
		// Act: форма из карточки конструкции.
		renderForm({ target: { constructionName: "Контртренд ETH" } });

		// Assert: чип цели показывает конструкцию, в которую попадёт сделка.
		expect(screen.getByText("Контртренд ETH")).toBeInTheDocument();
	});

	it("отправляет заполненные значения действия создания", async () => {
		// Arrange: форма с заполненными полями.
		const user = userEvent.setup();
		const { onSubmit } = renderForm();
		await user.type(screen.getByLabelText("Символ инструмента"), "BTCUSDT");
		await user.type(screen.getByLabelText("Количество"), "0,5");
		await user.type(screen.getByLabelText("Цена, USDT"), "2412");
		await user.type(screen.getByLabelText("Комиссия, USDT"), "1,2");

		// Act: сторона «Покупка» выбрана по умолчанию, отправляем форму.
		expect(screen.getByRole("radio", { name: "Покупка" })).toBeChecked();
		await user.click(screen.getByRole("button", { name: "Создать" }));

		// Assert: дата уходит ISO-строкой, запятая разобрана десятичным
		// разделителем, комиссия передана числом.
		expect(onSubmit).toHaveBeenCalledTimes(1);
		const input = onSubmit.mock.calls[0][0];
		expect(input.symbol).toBe("BTCUSDT");
		expect(input.side).toBe("Buy");
		expect(input.quantity).toBe(0.5);
		expect(input.price).toBe(2412);
		expect(input.feeUsdt).toBe(1.2);
		expect(Number.isNaN(new Date(input.executedAt).getTime())).toBe(false);
	});

	it("переключает сторону сегментом и отправляет Sell", async () => {
		// Arrange: форма с символом.
		const user = userEvent.setup();
		const { onSubmit } = renderForm();
		await user.type(screen.getByLabelText("Символ инструмента"), "ETHUSDT");

		// Act: выбор «Продажа» в сегменте стороны.
		await user.click(screen.getByRole("radio", { name: "Продажа" }));
		await user.type(screen.getByLabelText("Количество"), "1");
		await user.type(screen.getByLabelText("Цена, USDT"), "2400");
		await user.click(screen.getByRole("button", { name: "Создать" }));

		// Assert: сегмент переключил сторону без перезагрузки формы.
		expect(screen.getByRole("radio", { name: "Продажа" })).toBeChecked();
		expect(onSubmit.mock.calls[0][0].side).toBe("Sell");
	});

	it("оставляет непарсируемое число в форме с пояснением", async () => {
		// Arrange: количество с нечисловым значением.
		const user = userEvent.setup();
		const { onSubmit } = renderForm();
		await user.type(screen.getByLabelText("Символ инструмента"), "BTCUSDT");
		await user.type(screen.getByLabelText("Количество"), "ноль");

		// Act: попытка отправки.
		await user.click(screen.getByRole("button", { name: "Создать" }));

		// Assert: форма не отправлена, показано локальное пояснение.
		expect(onSubmit).not.toHaveBeenCalled();
		expect(screen.getByRole("alert")).toHaveTextContent("Введите корректные дату и числовые значения");
	});

	it("показывает причину отказа серверной валидации", () => {
		// Act: форма с ошибкой сервера (дата в будущем).
		renderForm({ error: "Дата сделки не может быть в будущем." });

		// Assert: человекочитаемая причина доступна как alert.
		expect(screen.getByRole("alert")).toHaveTextContent("Дата сделки не может быть в будущем.");
	});

	it("предзаполняет значения для правки и отправляет сохранение", async () => {
		// Arrange: форма правки с атрибутами существующей записи.
		const user = userEvent.setup();
		const { onSubmit } = renderForm({
			submitLabel: "Сохранить",
			initialValues: {
				executedAt: "2026-06-01T14:30",
				symbol: "BTCUSDT",
				side: "Sell",
				quantity: "1,2",
				price: "42000",
				feeUsdt: "0,5",
			},
		});

		// Assert: поля предзаполнены, символ отредактирован.
		expect(screen.getByLabelText("Символ инструмента")).toHaveValue("BTCUSDT");
		expect(screen.getByRole("radio", { name: "Продажа" })).toBeChecked();
		await user.clear(screen.getByLabelText("Символ инструмента"));
		await user.type(screen.getByLabelText("Символ инструмента"), "ETHUSDT");

		// Act: сохранение правки.
		await user.click(screen.getByRole("button", { name: "Сохранить" }));

		// Assert: значения ушли с правкой символа.
		expect(onSubmit.mock.calls[0][0].symbol).toBe("ETHUSDT");
		expect(onSubmit.mock.calls[0][0].side).toBe("Sell");
	});

	it("закрывает форму кнопкой отмены без отправки", async () => {
		// Arrange: форма с обработчиками.
		const user = userEvent.setup();
		const { onCancel, onSubmit } = renderForm();

		// Act: отмена и закрытие по крестику.
		await user.click(screen.getByRole("button", { name: "Отмена" }));

		// Assert: форма закрыта, отправки не было.
		expect(onCancel).toHaveBeenCalledTimes(1);
		expect(onSubmit).not.toHaveBeenCalled();
	});
});
