# Tasks

Порядок: сначала диагностика (1), затем протокол (2), потом UI (3–5); сверка дизайна — после change'а `revise-design-screens`. Тесты — по `.github/prompts/unittests.prompt.md`.

## 1. Диагностика «чат не отвечает»

- [ ] 1.1 Воспроизвести молчание прямым POST к `/api/v1/chats/{id}/messages/stream` (чтение SSE): перебрать причины — пустой `Llm:ApiKey` (`UnconfiguredChatClient`), недоступный `BaseUrl`, несуществующая модель, исключение генерации; зафиксировать фактическую причину в отчёте задачи. Проверить: `scenario-chat-error-delivered-as-sse-event`.
- [ ] 1.2 Устранить найденную причину; текст error-кадра сделать человекочитаемым для каждой причины; логировать отказ (chatId, тип причины) на бэкенде. Проверить: `scenario-chat-error-cause-visible-in-ui`, `scenario-chat-error-delivered-as-sse-event`.
- [ ] 1.3 Приёмка: при любой причине отказа UI показывает баннер с текстом ошибки, история чата не теряется; живой прогон с настроенным провайдером отвечает. Проверить: `scenario-chat-error-cause-visible-in-ui`.

## 2. События активности в SSE

- [ ] 2.1 `ChatSseStream`/`ChatStreamEvent`: кадр `event: tool` с payload `{ name, phase: "start"|"end", detail? }`; эмиссия из `ChatAgent` вокруг вызовов инструментов реестра; инвариант «ровно один терминальный кадр» сохраняется. Проверить: `scenario-chat-tool-activity-streamed`, `scenario-chat-tokens-stream-over-sse`.
- [ ] 2.2 Фронтенд `chat/sse`: парсинг `tool`-кадров; runtime хранит активные инструменты, чистит по `end`/терминальному кадру. Проверить: `scenario-chat-tool-activity-streamed`.

## 3. Чаты карточки в контекстной панели

- [ ] 3.1 `ConstructionChatsPanel` → слот `useContextPanel` страницы карточки: вся высота контента, геометрия каркаса (ширина/свёрнутость), развёрнута по умолчанию; убрать `?chats=*`, выбор чата оставить в `?chat=`. Проверить: `scenario-ui-chats-panel-in-context-panel`.

## 4. Активность и композер в UI

- [ ] 4.1 Тред: пилюли ToolStatus по `tool`-событиям (имя + фаза), гашение по `end`; удалить статичную надпись «источники — собираю данные…». Проверить: `scenario-ui-streaming-with-tool-status`.
- [ ] 4.2 Композер: textarea с авторостом (1–8 строк), Enter — отправка, Shift+Enter — перенос, отправка disabled при пустом вводе, Stop во время генерации (отмена стрима), чипы привязки/модели/источников тулбаром под полем. Проверить: `scenario-ui-composer-input-practices`, `scenario-ui-cancel-generation`.

## 5. Сверка дизайна

- [ ] 5.1 Скриншот-сверка чата с design.pen: сообщения пользователя/ассистента, tool-статусы, след источников, пункты сессий, композер. Проверить: `requirement-screenshot-acceptance`.
- [ ] 5.2 Живая приёмка владельцем: вопрос агенту с рыночными инструментами — видны пилюли активности, токены стримятся, отказ (при отключённом ключе) показывается баннером с причиной. Проверить: `scenario-chat-tool-activity-streamed`, `scenario-chat-error-cause-visible-in-ui`.
