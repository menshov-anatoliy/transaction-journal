import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useCardCommands } from "./use-card-commands";

// Проверяются команды спора синтетической связки карточки конструкции:
// нажатие действия вызывает готовый эндпоинт спора/возврата и помечает
// запрос карточки устаревшим — логический слой (лок строки «зафиксировано
// синтетикой» и логический размер ноги) обновляется ближайшим перечитыванием
// снимка, без оптимистичных правок. Список связок из раскрывающегося списка
// здесь не реализуется: действия нажимаются минимальной тестовой обвязкой
// над useCardCommands.
// Traceability: openspec:ui/screens#scenario-dispute-action-updates-logical-layer

const cardApi = vi.hoisted(() => ({
	fetchConstructionCard: vi.fn(),
	renameConstruction: vi.fn(),
	changeConstructionStatus: vi.fn(),
	changeAllocatedCapital: vi.fn(),
	changeRisk: vi.fn(),
	changeProfit: vi.fn(),
	deleteConstruction: vi.fn(),
	setConstructionComment: vi.fn(),
	setPositionComment: vi.fn(),
	setTradeComment: vi.fn(),
	fetchLastInstrumentMark: vi.fn(),
	addManualCloseMark: vi.fn(),
	editManualCloseMark: vi.fn(),
	deleteManualCloseMark: vi.fn(),
	fetchMoveTargets: vi.fn(),
	returnTradeToInbox: vi.fn(),
	moveTrade: vi.fn(),
	addAdjustment: vi.fn(),
	editAdjustment: vi.fn(),
	deleteAdjustment: vi.fn(),
	disputeSyntheticClose: vi.fn(),
	revertSyntheticClose: vi.fn(),
}));

vi.mock("@/lib/api/construction-card", () => cardApi);

/** Минимальная обвязка команд спора: две кнопки действия без UI-списка связок. */
function DisputeCommandsHarness() {
	const commands = useCardCommands(7);
	return (
		<div>
			<button
				onClick={() =>
					commands.disputeSynthetic.mutate({ linearExecId: "exec-linear-1", counterOptionExecId: "exec-option-1" })}
			>
				Оспорить
			</button>
			<button
				onClick={() =>
					commands.revertSynthetic.mutate({ linearExecId: "exec-linear-1", counterOptionExecId: "exec-option-1" })}
			>
				Вернуть
			</button>
			{commands.disputeSynthetic.error !== null && (
				<span role="alert">{commands.disputeSynthetic.error.message}</span>
			)}
		</div>
	);
}

function renderCommands(
	queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
): ReturnType<typeof render> {
	return render(
		<QueryClientProvider client={queryClient}>
			<DisputeCommandsHarness />
		</QueryClientProvider>,
	);
}

/** Ключи представлений, которые перечитывает любая команда карточки. */
const viewKeys = [
	["construction-card", 7], ["constructions-overview"], ["construction-preview", 7],
	["construction-move-targets", 7], ["inbox-overview"], ["inbox-count"],
] as const;

describe("useCardCommands: спор синтетической связки", () => {
	beforeEach(() => {
		vi.resetAllMocks();
	});

	it("нажатие «Оспорить» вызывает эндпоинт спора и помечает запрос карточки устаревшим", async () => {
		// Сценарий «Оспаривание обновляет логический слой перечитыванием»:
		// после действия карточка перечитывается, лок строки и логический
		// размер ноги пересчитываются сервером при чтении.
		// Traceability: openspec:ui/screens#scenario-dispute-action-updates-logical-layer
		// Arrange: кэш карточки и связанных представлений уже прочитан;
		// эндпоинт спора отвечает успехом без тела.
		const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
		for (const key of viewKeys) client.setQueryData(key, {});
		cardApi.disputeSyntheticClose.mockResolvedValue(undefined);
		const user = userEvent.setup();
		renderCommands(client);

		// Act: владелец оспаривает неоспоренную связку с парой execId.
		await user.click(screen.getByRole("button", { name: "Оспорить" }));

		// Assert: вызван эндпоинт спора с ключом связки, все связанные
		// представления помечены для перечитывания при открытии.
		await waitFor(() => expect(cardApi.disputeSyntheticClose).toHaveBeenCalledWith(7, {
			linearExecId: "exec-linear-1",
			counterOptionExecId: "exec-option-1",
		}));
		await waitFor(() => {
			for (const key of viewKeys) expect(client.getQueryState(key)?.isInvalidated).toBe(true);
		});
	});

	it("нажатие «Вернуть» вызывает эндпоинт возврата и помечает запрос карточки устаревшим", async () => {
		// Сценарий «Возврат восстанавливает связку в логическом слое»:
		// спор стирается, и связка снова входит в лок и логический размер
		// ноги ближайшим перечитыванием карточки.
		// Traceability: openspec:ui/screens#scenario-revert-action-restores-link
		// Arrange: кэш представлений прочитан; эндпоинт возврата успешен.
		const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
		for (const key of viewKeys) client.setQueryData(key, {});
		cardApi.revertSyntheticClose.mockResolvedValue(undefined);
		const user = userEvent.setup();
		renderCommands(client);

		// Act: владелец возвращает оспоренную связку из карточки.
		await user.click(screen.getByRole("button", { name: "Вернуть" }));

		// Assert: вызван эндпоинт возврата с ключом связки, представления
		// помечены для перечитывания при открытии.
		await waitFor(() => expect(cardApi.revertSyntheticClose).toHaveBeenCalledWith(7, {
			linearExecId: "exec-linear-1",
			counterOptionExecId: "exec-option-1",
		}));
		await waitFor(() => {
			for (const key of viewKeys) expect(client.getQueryState(key)?.isInvalidated).toBe(true);
		});
	});

	it("сбой спора попадает в ошибку мутации для firstError страницы карточки", async () => {
		// Arrange: эндпоинт спора отвечает ошибкой с человекочитаемой причиной.
		cardApi.disputeSyntheticClose.mockRejectedValue(new Error("связка уже оспорена"));
		const user = userEvent.setup();
		renderCommands();

		// Act: нажатие «Оспорить» на неоспоренной связке.
		await user.click(screen.getByRole("button", { name: "Оспорить" }));

		// Assert: ошибка мутации всплывает в состоянии команды — её сообщение
		// и становится значением firstError страницы карточки у шапки.
		expect(await screen.findByRole("alert")).toHaveTextContent("связка уже оспорена");
	});
});
