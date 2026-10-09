import { queryOptions } from "@tanstack/react-query";
import { listChatMessages } from "@/chat/api/chat-api";

/**
 * Ключи запросов чата агента в TanStack Query: единое дерево «chats»,
 * чтобы мутации жизненного цикла инвалидировали и списки, и истории.
 */
export const chatKeys = {
	all: ["chats"] as const,
	lists: () => [...chatKeys.all, "lists"] as const,
	list: (status: "active" | "completed") => [...chatKeys.lists(), status] as const,
	messages: (chatId: string) => [...chatKeys.all, "messages", chatId] as const,
};

/** Опции запроса плоской истории сообщений чата. */
export function chatMessagesOptions(chatId: string) {
	return queryOptions({
		queryKey: chatKeys.messages(chatId),
		queryFn: ({ signal }) => listChatMessages(chatId, signal),
	});
}
