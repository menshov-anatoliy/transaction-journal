import { useMutation, useQueryClient } from "@tanstack/react-query";
import * as cardApi from "@/lib/api/construction-card";
import type { ManualTradeInput } from "@/lib/api/manual-trades";
import { createManualTrade, deleteManualTrade, editManualTrade } from "@/lib/api/manual-trades";

/**
 * Команды карточки конструкции: мутации действий шапки, комментариев,
 * пометок закрытия, сделок, корректировок и спора синтетических связок.
 * Успех любой команды перечитывает
 * снимок карточки целиком — экран всегда показывает согласованное состояние,
 * как блазор-референс после каждого действия.
 */
// Команды карточки идут через единый версионированный API.
// Traceability: openspec:http-api/transport#scenario-spa-served-through-single-api
// Traceability: doc:.wf-research/ui-concept/concept.md#4-карточка-конструкции-маршрут-constructionsid
export function useCardCommands(constructionId: number) {
	const queryClient = useQueryClient();
	const cardKey = ["construction-card", constructionId] as const;

	// После мутации согласуются карточки, список, превью и «Входящие»:
	// перенос сделки меняет обе конструкции и счётчик непривязанных записей.
	// Traceability: doc:.wf-research/ui-concept/concept.md#1-рамка-и-принципы
	const invalidate = () => Promise.all([
		queryClient.invalidateQueries({ queryKey: ["construction-card"] }),
		queryClient.invalidateQueries({ queryKey: ["constructions-overview"] }),
		queryClient.invalidateQueries({ queryKey: ["construction-preview"] }),
		queryClient.invalidateQueries({ queryKey: ["construction-move-targets"] }),
		queryClient.invalidateQueries({ queryKey: ["inbox-overview"] }),
		queryClient.invalidateQueries({ queryKey: ["inbox-count"] }),
	]);

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
		onSuccess: async () => {
			queryClient.removeQueries({ queryKey: cardKey });
			await invalidate();
		},
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

	const addCloseAnnotation = useMutation({
		mutationFn: (annotation: cardApi.ManualCloseAnnotationInput) => cardApi.addManualCloseAnnotation(constructionId, annotation),
		onSuccess: invalidate,
	});

	const editCloseAnnotation = useMutation({
		mutationFn: (input: { annotationId: number; annotation: cardApi.ManualCloseAnnotationInput }) =>
			cardApi.editManualCloseAnnotation(input.annotationId, input.annotation),
		onSuccess: invalidate,
	});

	const deleteCloseAnnotation = useMutation({
		mutationFn: (annotationId: number) => cardApi.deleteManualCloseAnnotation(annotationId),
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

	// Ручная сделка из карточки сразу принадлежит этой конструкции; правка
	// и удаление адресуются ключом строки сырья, производные данные карточки
	// пересчитываются ближайшим чтением снимка.
	// Traceability: openspec:ui/screens#scenario-card-create-manual-trade
	// Traceability: openspec:ui/screens#scenario-detail-edit-manual-trade
	const createManualTradeCommand = useMutation({
		mutationFn: (input: ManualTradeInput) => createManualTrade({ ...input, constructionId }),
		onSuccess: invalidate,
	});

	const editManualTradeCommand = useMutation({
		mutationFn: (input: { id: number; trade: ManualTradeInput }) => editManualTrade(input.id, input.trade),
		onSuccess: invalidate,
	});

	const deleteManualTradeCommand = useMutation({
		mutationFn: (id: number) => deleteManualTrade(id),
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

	// Оспаривание распознанной связки: спор выводит связку из логического
	// слоя (лока и логического размера ноги) ближайшим перечитыванием
	// карточки — без оптимистичных правок, агрегаты пересчитываются из
	// свежего снимка (design.md, решение 4).
	// Traceability: openspec:ui/screens#scenario-dispute-action-updates-logical-layer
	// Traceability: change:add-synthetic-close-dispute-ui/design#d4
	const disputeSynthetic = useMutation({
		mutationFn: (key: cardApi.SyntheticCloseDisputeInput) => cardApi.disputeSyntheticClose(constructionId, key),
		onSuccess: invalidate,
	});

	// Возврат оспоренной связки стирает спор, и связка снова входит в лок
	// и логический размер ноги тем же перечитыванием снимка карточки.
	// Traceability: openspec:ui/screens#scenario-revert-action-restores-link
	const revertSynthetic = useMutation({
		mutationFn: (key: cardApi.SyntheticCloseDisputeInput) => cardApi.revertSyntheticClose(constructionId, key),
		onSuccess: invalidate,
	});

	// Ручная синтетическая связка: создание из двух сделок конструкции —
	// правила пары проверяет сервер, форма показывает причину отказа; удаление
	// свободно по идентификатору из ответа создания. Логический слой (лок и
	// логический размер ноги) пересчитывается ближайшим перечитыванием снимка.
	// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
	const createManualLink = useMutation({
		mutationFn: (input: cardApi.SyntheticCloseManualLinkInput) =>
			cardApi.createManualSyntheticLink(constructionId, input),
		onSuccess: invalidate,
	});

	const deleteManualLink = useMutation({
		mutationFn: (linkId: number) => cardApi.deleteManualSyntheticLink(constructionId, linkId),
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
		addCloseAnnotation,
		editCloseAnnotation,
		deleteCloseAnnotation,
		returnTrade,
		moveTrade,
		createManualTrade: createManualTradeCommand,
		editManualTrade: editManualTradeCommand,
		deleteManualTrade: deleteManualTradeCommand,
		addAdjustment,
		editAdjustment,
		deleteAdjustment,
		disputeSynthetic,
		revertSynthetic,
		createManualLink,
		deleteManualLink,
	};
}
