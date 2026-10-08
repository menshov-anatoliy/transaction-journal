import type { ChatDataSource, ChatParams } from "./types";

/** Позиция закрытого справочника источников данных для формы нового чата. */
export interface ChatDataSourceInfo {
	readonly id: ChatDataSource;
	/** Подпись категории в интерфейсе. */
	readonly label: string;
	/** Пояснение, что именно читает ИИ-помощник из этой категории. */
	readonly hint: string;
}

/**
 * Закрытый справочник источников данных чата агента: журнал, корпус правил
 * и рынок Bybit — ровно три категории, только для чтения. Расширение
 * справочника — только через изменение домена (change add-agent-chat).
 */
// Traceability: change:add-agent-chat/proposal#what-changes
// Traceability: openspec:chats/sources#scenario-sources-three-categories
export const CHAT_DATA_SOURCES: readonly ChatDataSourceInfo[] = [
	{
		id: "journal",
		label: "Журнал",
		hint: "Снимок конструкции или портфельный уровень журнала с агрегатами и лимитами",
	},
	{
		id: "rules-corpus",
		label: "Корпус правил",
		hint: "Индекс карточек правил; полная карточка читается по требованию",
	},
	{
		id: "market",
		label: "Рынок Bybit",
		hint: "Спот/фьючерсы, фандинг и доска опционов на момент сообщения",
	},
];

/** Идентификаторы категорий справочника в каноническом порядке. */
const ALL_SOURCE_IDS: readonly ChatDataSource[] = CHAT_DATA_SOURCES.map((source) => source.id);

/**
 * Модель чата по умолчанию: GLM-5.3. Провайдер и модель задаёт конфигурация
 * бэкенда, в форме нового чата это предвыбранное значение.
 */
// Traceability: change:add-agent-chat/proposal#what-changes
export const DEFAULT_CHAT_MODEL = "GLM-5.3";

/**
 * Дефолтные параметры нового чата: модель GLM-5.3, все три источника
 * данных и отсутствие привязки к конструкции (портфельный уровень журнала).
 */
// Traceability: change:add-agent-chat/proposal#what-changes
// Traceability: openspec:chats/sources#scenario-sources-default-model-glm
export function defaultChatParams(): ChatParams {
	return {
		model: DEFAULT_CHAT_MODEL,
		constructionId: null,
		sources: [...ALL_SOURCE_IDS],
	};
}

/** Выбор формы нового чата: любое поле можно не трогать — возьмётся дефолт. */
export interface ChatParamsFormInput {
	readonly model?: string;
	readonly constructionId?: string | null;
	readonly sources?: readonly ChatDataSource[];
}

/**
 * Превращает выбор формы нового чата в параметры: незаполненное наследует
 * дефолт. Набор источников нормализуется по закрытому справочнику — без
 * повторов и посторонних идентификаторов; пустой набор откатывается
 * к полному, потому что чат без источников не имеет смысла.
 */
// Traceability: change:add-agent-chat/proposal#what-changes
// Traceability: openspec:chats/sources#scenario-sources-subset-parameter
export function chatParamsFromForm(input: ChatParamsFormInput): ChatParams {
	const sources = normalizeSources(input.sources);
	const model = input.model?.trim() === "" ? DEFAULT_CHAT_MODEL : (input.model ?? DEFAULT_CHAT_MODEL);

	return {
		model,
		constructionId: input.constructionId ?? null,
		sources,
	};
}

function normalizeSources(sources: readonly ChatDataSource[] | undefined): ChatDataSource[] {
	if (sources === undefined)
		return [...ALL_SOURCE_IDS];

	const known = sources.filter((id) => (ALL_SOURCE_IDS as readonly string[]).includes(id));
	const unique = [...new Set(known)];
	return unique.length === 0 ? [...ALL_SOURCE_IDS] : unique;
}
