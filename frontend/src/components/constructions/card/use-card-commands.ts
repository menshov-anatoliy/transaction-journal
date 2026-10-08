import { useMutation, useQueryClient } from "@tanstack/react-query";
import * as cardApi from "@/lib/api/construction-card";

/**
 * Команды карточки конструкции: мутации действий шапки, комментариев,
 * пометок закрытия, сделок и корректировок. Успех любой команды перечитывает
 * снимок карточки целиком — экран всегда показывает согласованное состояние,
 * как блазор-референс после каждого действия.
 */
// Команды карточки идут через единый версионированный API.
// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
export function useCardCommands(constructionId: number) {
	const queryClient = useQueryClient();
	const cardKey = ["construction-card", constructionId] as const;

	const invalidate = () => queryClient.invalidateQueries({ queryKey: cardKey });

	const rename = useMutation({
		mutationFn: (name: string) => cardApi.renameConstruction(constructionId, name),
		onSuccess: invalidate,
	});

	const status = useMutation({
		mutationFn: (next: cardApi.ConstructionStatus) => cardApi.changeConstructionStatus(constructionId, next),
		onSuccess: invalidate,
	});

	const capital = useMutation({
		mutationFn: (value: number | null) => cardApi.changeAllocatedCapital(constructionId, value),
		onSuccess: invalidate,
	});

	const risk = useMutation({
		mutationFn: (input: { value: number | null; unit: cardApi.TargetUnit | null }) =>
			cardApi.changeRisk(constructionId, input.value, input.unit),
		onSuccess: invalidate,
	});

	const profit = useMutation({
		mutationFn: (input: { value: number | null; unit: cardApi.TargetUnit | null }) =>
			cardApi.changeProfit(constructionId, input.value, input.unit),
		onSuccess: invalidate,
	});

	const remove = useMutation({
		mutationFn: (makeBackup: boolean) => cardApi.deleteConstruction(constructionId, makeBackup),
	});

	const constructionComment = useMutation({
		mutationFn: (text: string | null) => cardApi.setConstructionComment(constructionId, text),
		onSuccess: invalidate,
	});

	const positionComment = useMutation({
		mutationFn: (input: { symbol: string; text: string | null }) =>
			cardApi.setPositionComment(constructionId, input.symbol, input.text),
		onSuccess: invalidate,
	});

	const tradeComment = useMutation({
		mutationFn: (input: { execId: string; text: string | null }) =>
			cardApi.setTradeComment(input.execId, input.text),
		onSuccess: invalidate,
	});

	const addCloseMark = useMutation({
		mutationFn: (mark: cardApi.ManualCloseMarkInput) => cardApi.addManualCloseMark(constructionId, mark),
		onSuccess: invalidate,
	});

	const editCloseMark = useMutation({
		mutationFn: (input: { markId: number; mark: cardApi.ManualCloseMarkInput }) =>
			cardApi.editManualCloseMark(input.markId, input.mark),
		onSuccess: invalidate,
	});

	const deleteCloseMark = useMutation({
		mutationFn: (markId: number) => cardApi.deleteManualCloseMark(markId),
		onSuccess: invalidate,
	});

	const returnTrade = useMutation({
		mutationFn: (execId: string) => cardApi.returnTradeToInbox(execId),
		onSuccess: invalidate,
	});

	const moveTrade = useMutation({
		mutationFn: (input: { execId: string; targetConstructionId: number }) =>
			cardApi.moveTrade(input.execId, input.targetConstructionId),
		onSuccess: invalidate,
	});

	const addAdjustment = useMutation({
		mutationFn: (adjustment: cardApi.AdjustmentInput) => cardApi.addAdjustment(constructionId, adjustment),
		onSuccess: invalidate,
	});

	const editAdjustment = useMutation({
		mutationFn: (input: { adjustmentId: number; adjustment: cardApi.AdjustmentInput }) =>
			cardApi.editAdjustment(input.adjustmentId, input.adjustment),
		onSuccess: invalidate,
	});

	const deleteAdjustment = useMutation({
		mutationFn: (adjustmentId: number) => cardApi.deleteAdjustment(adjustmentId),
		onSuccess: invalidate,
	});

	return {
		rename,
		status,
		capital,
		risk,
		profit,
		remove,
		constructionComment,
		positionComment,
		tradeComment,
		addCloseMark,
		editCloseMark,
		deleteCloseMark,
		returnTrade,
		moveTrade,
		addAdjustment,
		editAdjustment,
		deleteAdjustment,
	};
}
