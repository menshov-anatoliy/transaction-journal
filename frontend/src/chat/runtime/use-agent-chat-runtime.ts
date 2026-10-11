import { useCallback, useMemo, useRef, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import type {
	AppendMessage,
	AssistantRuntime,
	ThreadMessageLike,
} from "@assistant-ui/react";
import { useExternalStoreRuntime } from "@assistant-ui/react";
import { ChatStreamFailure, streamChatReply } from "@/chat/api/chat-api";
import { chatMessagesOptions, chatKeys } from "@/chat/chat-queries";
import type { ChatStreamEvent } from "@/chat/sse/chat-stream-events";
import type { ChatMessageDto, SourceTrace } from "@/chat/types";

/**
 * Оверлей текущего хода: сообщение владельца и стримящийся ответ
 * ИИ-помощника живут локально поверх истории из TanStack Query —
 * токены не пишутся в кэш запросов, а после завершения хода
 * авторитетной становится перечитанная история сервера.
 */
interface ChatRunOverlay {
	readonly userMessage: ChatMessageDto;
	readonly assistantMessage: ChatMessageDto;
	/** Сообщение сбоя генерации; null, пока ход идёт штатно. */
	readonly error: string | null;
}

/**
 * Активный инструмент текущего хода: запись кадра tool start без парного
 * end — пилюля активности живёт, пока вызов инструмента идёт.
 */
export interface AgentChatActiveTool {
	/** Имя инструмента реестра из кадра активности. */
	readonly name: string;
	/** Опциональная деталь вызова из кадра активности. */
	readonly detail?: string;
}

/** Публичкий контроллер чата агента для базовых обёрток Thread/Composer. */
export interface AgentChatController {
	/** Runtime assistant-ui для AssistantRuntimeProvider. */
	readonly runtime: AssistantRuntime;
	/** Идёт ли генерация ответа прямо сейчас. */
	readonly isRunning: boolean;
	/** Сообщение последнего сбоя генерации для баннера деградации. */
	readonly error: string | null;
	/** Инструменты с идущим вызовом: кадры start без парного end. */
	readonly activeTools: readonly AgentChatActiveTool[];
	/** Отмена генерации владельцем. */
	readonly cancel: () => void;
}

/**
 * Runtime чата агента поверх ExternalStoreRuntime: TanStack Query —
 * источник истории, ходы(owner→SSE→ответ) — локальный оверлей. Изоляция
 * churn assistant-ui одним адаптером: экраны 5.3 видят только контроллер,
 * не типы фреймворка.
 */
// Traceability: adr:docs/adr/0010-frontend-spa-react-stack.md
// Traceability: change:add-agent-chat/proposal#what-changes
export function useAgentChatRuntime(chatId: string, model: string): AgentChatController {
	const queryClient = useQueryClient();
	const { data: history = [] } = useQuery(chatMessagesOptions(chatId));

	const [overlay, setOverlay] = useState<ChatRunOverlay | null>(null);
	const [isRunning, setIsRunning] = useState(false);
	const [activeTools, setActiveTools] = useState<readonly AgentChatActiveTool[]>([]);
	const abortRef = useRef<AbortController | null>(null);

	const cancel = useCallback(() => {
		abortRef.current?.abort();
	}, []);

	const onNew = useCallback(
		async (message: AppendMessage) => {
			const text = appendMessageText(message);
			const now = new Date().toISOString();
			setOverlay({
				userMessage: { id: `local-user-${now}`, role: "user", text, asOf: now },
				assistantMessage: { id: `local-assistant-${now}`, role: "assistant", text: "", asOf: now },
				error: null,
			});
			setIsRunning(true);
			// Новый ход начинается с чистым списком активности: пилюли
			// предыдущего хода не переносятся.
			setActiveTools([]);

			const abort = new AbortController();
			abortRef.current = abort;

			try {
				// Токены дописывают ответ инкрементально — оверлей обновляется
				// по каждому событию канала, не дожидаясь завершения.
				await streamChatReply({
					chatId,
					text,
					model,
					signal: abort.signal,
					onEvent: (event) => {
						if (event.type === "token") {
							setOverlay((current) => {
								if (current === null)
									return current;
								return {
									...current,
									assistantMessage: {
										...current.assistantMessage,
										text: current.assistantMessage.text + event.text,
									},
								};
							});
							return;
						}
						// Активность инструментов копится из кадров стрима: start
						// зажигает инструмент, end гасит, терминальный кадр чистит
						// список целиком.
						// Traceability: openspec:http-api/transport#scenario-chat-tool-activity-streamed
						if (event.type === "tool")
							setActiveTools((current) => applyToolActivity(current, event));
						else if (event.type === "done" || event.type === "error")
							setActiveTools([]);
					},
				});

				// Ход завершён: история сервера авторитетна — перечитываем её
				// и только затем убираем оверлей, чтобы ход не мигал и не дублировался.
				await queryClient.refetchQueries({ queryKey: chatKeys.messages(chatId) });
				void queryClient.invalidateQueries({ queryKey: chatKeys.lists() });
				setOverlay(null);
			} catch (error: unknown) {
				// Отмена владельцем — не сбой: состояние чата синхронизируется
				// с историей сервера так же, как после завершённого хода.
				if ((error as Error).name === "AbortError") {
					await queryClient.refetchQueries({ queryKey: chatKeys.messages(chatId) });
					setOverlay(null);
					return;
				}

				// Ошибка генерации деградирует: частичный текст и сообщение
				// владельца остаются на экране, прежняя история не теряется.
				// Оверлей живёт до следующего хода — серверу нельзя доверять
				// перечитку после сбоя, пока контракт ошибки не зафиксирован.
				const failure =
					error instanceof ChatStreamFailure
						? error
						: new ChatStreamFailure((error as Error).message, "");
				setOverlay((current) => {
					if (current === null)
						return current;
					return {
						...current,
						assistantMessage: { ...current.assistantMessage, text: failure.partialText },
						error: failure.message,
					};
				});
			} finally {
				// Ход завершён (штатно, сбоем или отменой) — активных инструментов
				// быть не должно даже без терминального кадра: отменённый стрим
				// терминала не доставляет.
				setActiveTools([]);
				setIsRunning(false);
				abortRef.current = null;
			}
		},
		[chatId, model, queryClient],
	);

	const runtime = useExternalStoreRuntime({
		messages: useMemo(
			() =>
				overlay === null
					? history
					: [...history, overlay.userMessage, overlay.assistantMessage],
			[history, overlay],
		),
		isRunning,
		onNew,
		onCancel: useCallback(async () => {
			abortRef.current?.abort();
		}, []),
		convertMessage: toThreadMessageLike,
	});

	return { runtime, isRunning, error: overlay?.error ?? null, activeTools, cancel };
}

/**
 * Накопление активных инструментов из кадров tool: start зажигает тул
 * (повторный start того же имени не плодит дублей — обновляет деталь),
 * end убирает тул по имени. Порядок сохраняется по моменту старта вызова,
 * чтобы статусы рендерились в порядке вызовов.
 */
// Список активных инструментов — источник живых статусов вызовов тулов
// реестра над потоком токенов.
// Traceability: openspec:http-api/transport#scenario-chat-tool-activity-streamed
function applyToolActivity(
	current: readonly AgentChatActiveTool[],
	event: Extract<ChatStreamEvent, { type: "tool" }>,
): readonly AgentChatActiveTool[] {
	// End гасит инструмент: без парного кадра в списке он не остаётся.
	if (event.phase === "end")
		return current.filter((tool) => tool.name !== event.name);
	const withoutName = current.filter((tool) => tool.name !== event.name);
	const entry: AgentChatActiveTool =
		event.detail === undefined ? { name: event.name } : { name: event.name, detail: event.detail };
	return [...withoutName, entry];
}

/** Текст сообщения composer: только текстовые части, прочее не поддержано. */
function appendMessageText(message: AppendMessage): string {
	const text = message.content
		.filter((part) => part.type === "text")
		.map((part) => (part.type === "text" ? part.text : ""))
		.join("")
		.trim();
	if (text === "")
		throw new Error("поддержаны только текстовые сообщения");
	return text;
}

/**
 * Конвертация сообщения чата в формат runtime: след источников ехал
 * в метаданных и доезжает до рендера ответа без фреймворк-зависимых типов.
 */
function toThreadMessageLike(message: ChatMessageDto): ThreadMessageLike {
	return {
		id: message.id,
		role: message.role,
		content: [{ type: "text", text: message.text }],
		createdAt: new Date(message.asOf),
		metadata: { custom: { sourceTrace: message.sourceTrace satisfies SourceTrace | undefined } },
	};
}
