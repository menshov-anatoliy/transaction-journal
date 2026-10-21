import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import type { ConstructionCardTrade } from "@/lib/api/construction-card";
import type { ManualTradeInput } from "@/lib/api/manual-trades";
import { METRIC_TOOLTIPS } from "@/lib/metric-tooltips";
import { TooltipProvider } from "@/components/ui/tooltip";
import { TradesTable } from "./trades-table";

// Таблица сделок карточки: бейдж источника в строках, правка и удаление
// только у ручных записей (удаление — с подтверждением), создание ручной
// сделки из головки секции с попаданием сразу в показанную конструкцию.
// Traceability: openspec:ui/screens#requirement-manual-trade-creation-ui
// Traceability: openspec:ui/screens#scenario-detail-edit-manual-trade
// Traceability: openspec:ui/screens#scenario-detail-synced-trade-immutable

vi.mock("@/lib/api/construction-card", async (importOriginal) => ({
	...(await importOriginal<typeof import("@/lib/api/construction-card")>()),
	fetchMoveTargets: vi.fn().mockResolvedValue([]),
}));

/** Биржевая и ручная записи одного инструмента для таблицы. */
const trades: ConstructionCardTrade[] = [
	{
		execId: "exec-1",
		symbol: "ETHUSDT",
		executedAt: "2026-06-18T09:05:00+00:00",
		isBuy: true,
		quantity: 2,
		price: 30,
		amountUsdt: 60,
		fee: 0.1,
		comment: null,
		source: "exchange",
		manualTradeId: null,
	},
	{
		execId: null,
		symbol: "BTCUSDT",
		executedAt: "2026-06-18T10:00:00+00:00",
		isBuy: false,
		quantity: 0.5,
		price: 2400,
		amountUsdt: 1200,
		fee: 1.2,
		comment: null,
		source: "manual",
		manualTradeId: 42,
	},
];

function renderTable(overrides?: Partial<Parameters<typeof TradesTable>[0]>) {
	const commands = {
		onReturn: vi.fn(),
		onMove: vi.fn(),
		onSaveComment: vi.fn(),
		onCreateManualTrade: vi.fn(),
		onEditManualTrade: vi.fn(),
		onDeleteManualTrade: vi.fn(),
	};
	const props = {
		constructionId: 7,
		constructionName: "Контртренд ETH",
		trades,
		actionsPending: false,
		tradeCommandsPending: false,
		manualTradeError: null,
		...commands,
		...overrides,
	};
	render(
		<QueryClientProvider client={new QueryClient({ defaultOptions: { queries: { retry: false } } })}>
			<TooltipProvider delayDuration={0}>
				<TradesTable {...props} />
			</TooltipProvider>
		</QueryClientProvider>,
	);
	return { commands, props };
}

describe("таблица сделок карточки: ручные записи", () => {
	it("различает строки бейджем источника", () => {
		// Act: рендер таблицы с биржевой и ручной строками.
		renderTable();

		// Assert: оба источника подписаны бейджами мастера.
		expect(screen.getByText("биржа")).toBeInTheDocument();
		expect(screen.getByText("ручная")).toBeInTheDocument();
	});

	it("показывает прочерк execId у ручной записи", () => {
		// Act: рендер таблицы.
		renderTable();

		// Assert: вторая ячейка строки ручной записи — прочерк вместо execId.
		const manualRow = screen.getByRole("row", { name: /BTCUSDT/i });
		const execIdCell = manualRow.querySelectorAll("td")[1]!;
		expect(execIdCell.textContent).toBe("—");
	});

	it("даёт правку и удаление только ручной записи", () => {
		// Act: рендер таблицы.
		renderTable();

		// Assert: у ручной строки есть «Изменить» и «Удалить», у биржевой нет.
		const manualRow = screen.getByRole("row", { name: /BTCUSDT/i });
		expect(within(manualRow).getByRole("button", { name: "Изменить" })).toBeInTheDocument();
		expect(within(manualRow).getByRole("button", { name: "Удалить" })).toBeInTheDocument();
		const exchangeRow = screen.getByRole("row", { name: /ETHUSDT/i });
		expect(within(exchangeRow).queryByRole("button", { name: "Изменить" })).not.toBeInTheDocument();
		expect(within(exchangeRow).queryByRole("button", { name: "Удалить" })).not.toBeInTheDocument();
	});

	it("удаляет ручную запись только после подтверждения", async () => {
		// Arrange: таблица с ручной записью.
		const user = userEvent.setup();
		const { commands } = renderTable();

		// Act: клик «Удалить» открывает подтверждение, «Подтвердить удаление» исполняет.
		const manualRow = screen.getByRole("row", { name: /BTCUSDT/i });
		await user.click(within(manualRow).getByRole("button", { name: "Удалить" }));
		expect(screen.getByRole("dialog")).toBeInTheDocument();
		expect(commands.onDeleteManualTrade).not.toHaveBeenCalled();
		await user.click(screen.getByRole("button", { name: "Подтвердить удаление" }));

		// Assert: удаление адресовано ключом строки сырья ручной записи.
		expect(commands.onDeleteManualTrade).toHaveBeenCalledWith(42);
	});

	// Возврат и перенос доступны каждой строке таблицы: ручная запись
	// адресуется единым ключом «manual:{id}» теми же командами.
	// Traceability: openspec:ui/screens#requirement-trade-actions-in-detail
	it("адресует возврат и перенос ручной записи единым ключом", async () => {
		// Arrange: таблица с ручной записью и одной целью переноса.
		const cardApi = await import("@/lib/api/construction-card");
		vi.mocked(cardApi.fetchMoveTargets).mockResolvedValue([{ constructionId: 9, name: "Цель переноса" }]);
		const user = userEvent.setup();
		const { commands } = renderTable();
		const manualRow = screen.getByRole("row", { name: /BTCUSDT/i });

		// Act: возвращаем ручную запись во «Входящие».
		await user.click(within(manualRow).getByRole("button", { name: "Во «Входящие»" }));

		// Assert: команда получила единый ключ ручной записи.
		expect(commands.onReturn).toHaveBeenCalledWith("manual:42");

		// Act: переносим ручную запись в целевую конструкцию.
		await user.click(within(manualRow).getByRole("button", { name: "Перенести…" }));
		await user.click(screen.getByRole("button", { name: "Перенести" }));

		// Assert: команда переноса получила тот же единый ключ и выбранную цель.
		expect(commands.onMove).toHaveBeenCalledWith({ execId: "manual:42", targetConstructionId: 9 });
	});

	it("отменяет удаление без вызова команды", async () => {
		// Arrange: диалог подтверждения открыт.
		const user = userEvent.setup();
		const { commands } = renderTable();
		const manualRow = screen.getByRole("row", { name: /BTCUSDT/i });
		await user.click(within(manualRow).getByRole("button", { name: "Удалить" }));

		// Act: отмена подтверждения.
		await user.click(within(screen.getByRole("dialog")).getByRole("button", { name: "Отмена" }));

		// Assert: команда удаления не вызвана.
		expect(commands.onDeleteManualTrade).not.toHaveBeenCalled();
		expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
	});

	it("создаёт ручную сделку из головки секции в показанную конструкцию", async () => {
		// Arrange: таблица и открытая форма создания.
		const user = userEvent.setup();
		const { commands } = renderTable();
		await user.click(screen.getByRole("button", { name: "Ручная сделка" }));

		// Assert: чип цели показывает конструкцию карточки.
		expect(await screen.findByText("Контртренд ETH")).toBeInTheDocument();
		await user.type(screen.getByLabelText("Символ инструмента"), "BTCUSDT");
		await user.type(screen.getByLabelText("Количество"), "0,5");
		await user.type(screen.getByLabelText("Цена, USDT"), "2400");

		// Act: отправка формы.
		await user.click(screen.getByRole("button", { name: "Создать" }));

		// Assert: создание адресовано команде карточки.
		const input = commands.onCreateManualTrade.mock.calls[0][0] as ManualTradeInput;
		expect(input.symbol).toBe("BTCUSDT");
		expect(input.side).toBe("Buy");
		expect(input.quantity).toBe(0.5);
	});

	it("предзаполняет форму правки атрибутами ручной записи", async () => {
		// Arrange: таблица с ручной записью.
		const user = userEvent.setup();
		const { commands } = renderTable();

		// Act: открытие формы правки и сохранение без изменений полей.
		const manualRow = screen.getByRole("row", { name: /BTCUSDT/i });
		await user.click(within(manualRow).getByRole("button", { name: "Изменить" }));
		expect(screen.getByLabelText("Символ инструмента")).toHaveValue("BTCUSDT");
		expect(screen.getByRole("radio", { name: "Продажа" })).toBeChecked();
		await user.click(screen.getByRole("button", { name: "Сохранить" }));

		// Assert: правка адресована ключом строки сырья с исходными атрибутами.
		expect(commands.onEditManualTrade).toHaveBeenCalledTimes(1);
		const call = commands.onEditManualTrade.mock.calls[0][0];
		expect(call.id).toBe(42);
		expect(call.trade.symbol).toBe("BTCUSDT");
		expect(call.trade.side).toBe("Sell");
	});

		// Форма создания скрывается только после успешного создания; при отказе
		// сервера остаётся открытой с причиной отказа из команды родителя.
		// Traceability: change:manual-journal-management/specs/ui/screens/spec.md#requirement-manual-trade-creation-ui
		it("скрывает форму создания после успешного ответа", async () => {
			// Arrange: открытая форма с командой, разрешающейся успехом.
			const user = userEvent.setup();
			const onCreateManualTrade = vi.fn().mockResolvedValue(undefined);
			renderTable({ onCreateManualTrade });
			await user.click(screen.getByRole("button", { name: "Ручная сделка" }));
			await user.type(screen.getByLabelText("Символ инструмента"), "BTCUSDT");
			await user.type(screen.getByLabelText("Количество"), "0,5");
			await user.type(screen.getByLabelText("Цена, USDT"), "2400");

			// Act: отправка формы.
			await user.click(screen.getByRole("button", { name: "Создать" }));

			// Assert: команда выполнена, форма скрыта.
			await waitFor(() => expect(onCreateManualTrade).toHaveBeenCalledTimes(1));
			expect(screen.queryByLabelText("Символ инструмента")).not.toBeInTheDocument();
		});

		it("оставляет форму создания открытой с причиной отказа при ошибке", async () => {
			// Arrange: открытая форма с отклонённой командой и причиной от родителя.
			const user = userEvent.setup();
			const onCreateManualTrade = vi.fn().mockRejectedValue(new Error("сбой"));
			renderTable({ onCreateManualTrade, manualTradeError: "символ не распознан" });
			await user.click(screen.getByRole("button", { name: "Ручная сделка" }));
			await user.type(screen.getByLabelText("Символ инструмента"), "BTCUSDT");
			await user.type(screen.getByLabelText("Количество"), "1");
			await user.type(screen.getByLabelText("Цена, USDT"), "100");

			// Act: отправка формы.
			await user.click(screen.getByRole("button", { name: "Создать" }));

			// Assert: форма осталась открыта, причина отказа видна.
			expect(await screen.findByText("символ не распознан")).toBeInTheDocument();
			expect(screen.getByLabelText("Символ инструмента")).toBeInTheDocument();
		});
});

// Методы расчёта, зафиксированные в справочнике для других метрик: у сырых
// колонок сделок метода нет, и чужая формула в подсказке появляться не должна.
const knownMethodTexts = Object.values(METRIC_TOOLTIPS)
	.map((tooltip) => tooltip.method)
	.filter((method): method is string => method !== undefined);

describe("таблица сделок карточки: подсказки заголовков колонок", () => {
	// Числовые колонки сделок поясняются в заголовке подсказкой метрики;
	// сырые данные сделок — только назначением, без метода расчёта.
	// Traceability: openspec:ui/screens#requirement-column-header-tooltips
	it.each([
		["Кол-во", "tradeQuantity"],
		["Цена", "tradePrice"],
		["Сумма", "tradeAmount"],
		["Комиссия", "tradeCommission"],
	] as const)("даёт подсказку колонке «%s» с назначением без метода", async (header, key) => {
		// Arrange: таблица сделок под провайдером подсказок.
		const user = userEvent.setup();
		renderTable();

		// Act: наведение на заголовок колонки.
		await user.hover(screen.getByText(header));

		// Assert: попап с ролью tooltip содержит заголовок и назначение
		// из справочника и не содержит метода ни одной другой метрики.
		const tooltip = screen.getByRole("tooltip");
		expect(tooltip).toHaveTextContent(METRIC_TOOLTIPS[key].title);
		expect(tooltip).toHaveTextContent(METRIC_TOOLTIPS[key].purpose);
		for (const method of knownMethodTexts) {
			expect(tooltip).not.toHaveTextContent(method);
		}
	});

	// Колонка времени — дата/время сделки: подсказка метрики у дат нет.
	// Traceability: openspec:ui/screens#scenario-date-columns-skip-tooltip
	it("не даёт подсказки колонке времени", async () => {
		// Arrange: таблица сделок под провайдером подсказок.
		const user = userEvent.setup();
		renderTable();

		// Act: наведение на заголовок колонки времени.
		await user.hover(screen.getByText("Время"));

		// Assert: подсказка не открылась.
		expect(screen.queryByRole("tooltip")).not.toBeInTheDocument();
	});
});
