import { useMutation, useQueryClient } from "@tanstack/react-query";
import { completeChat, resumeChat } from "@/chat/api/chat-api";
import { chatKeys } from "@/chat/chat-queries";

/**
 * Мутация ручного завершения чата: активный чат уходит в завершённые;
 * после успеха списки активных и завершённых чатов перечитываются,
 * чтобы переезд между списками доехал до интерфейса без ручного обновления.
 */
// Traceability: change:add-agent-chat/proposal#what-changes
// Traceability: openspec:chats/history#scenario-chat-completion-hides-to-completed-list
export function useCompleteChatMutation() {
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (chatId: string) => completeChat(chatId),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: chatKeys.lists() });
		},
	});
}

/**
 * Мутация продолжения завершённого чата: снятие метки завершения и
 * возврат чата в активные. Продолжение также происходит отправкой
 * сообщения в завершённый чат — этот путь закрывает стриминговый ход.
 */
// Traceability: change:add-agent-chat/proposal#what-changes
// Traceability: openspec:chats/history#scenario-chat-resume-returns-to-active
export function useResumeChatMutation() {
	const queryClient = useQueryClient();
	return useMutation({
		mutationFn: (chatId: string) => resumeChat(chatId),
		onSuccess: () => {
			void queryClient.invalidateQueries({ queryKey: chatKeys.lists() });
		},
	});
}
