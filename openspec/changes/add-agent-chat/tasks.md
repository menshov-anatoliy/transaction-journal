# Tasks

## 1. Словарь и проект окружения

- [x] 1.1 Переименовать проект окружения `src/TransactionJournal.Consultations` → `src/TransactionJournal.Chats` (зависимость только от Domain), порты `IConsultationContextReader` → `IChatContextReader`, `IConsultationStore` → `IChatStore`; зеркальные тесты структуры обновить. Проверить: `requirement-chat-environment-record` — сценарий `scenario-chat-domain-agnostic`; сборка решения зелёная.
- [x] 1.2 Переименовать файл инструкций `consultation-prompt.md` → `agent-prompt.md` (настраиваемый путь, дефолт рядом с `rules/`, встроенный дефолт в коде, битость не ломает чат). Проверить: `requirement-chat-context-agent-instructions-file` — сценарии `scenario-chat-context-instructions-override`, `scenario-chat-context-instructions-missing-ok`.

## 2. Хранилище чатов

- [ ] 2.1 Заменить per-construction базы единым SQLite-хранилищем: чат (ИИ-модель, опциональная неизменяемая привязка к конструкции, набор источников, статус «активен/завершён») и сообщения (роль, текст, as-of, след источников); чат создаётся первым сообщением, ИИ-помощник видит только свою историю. Проверить: `requirement-chat-flat-full-history` — `scenario-chat-created-by-first-message`, `scenario-chat-neighbour-isolation`; `requirement-chat-construction-binding-immutable` — `scenario-chat-without-construction-allowed`, `scenario-chat-binding-cannot-change`.
- [ ] 2.2 Жизненный цикл: ручное завершение (скрытие в список завершённых), продолжение с возвратом в активные, удаление целиком без корзины; автоматического завершения нет. Проверить: `requirement-chat-manual-completion-and-deletion` — сценарии `scenario-chat-completion-hides-to-completed-list`, `scenario-chat-resume-returns-to-active`, `scenario-chat-hard-delete`.
- [ ] 2.3 Пересбор: стирает только чаты привязанных конструкций, непривязанные переживают; старые базы консультаций удаляются без миграции (чистый лист). Проверить: `requirement-chat-environment-record` — `scenario-chat-rebuild-wipes-bound-chats`, `scenario-chat-unbound-chat-survives-rebuild`.

## 3. Контекст и источники

- [ ] 3.1 Портфельная ветка снимка контекста для чатов без конструкции: агрегаты и лимиты журнала + индекс корпуса с as-of, без раздела конструкции. Проверить: `requirement-chat-context-deterministic-snapshot` — `scenario-chat-context-portfolio-snapshot-without-construction`; регресс `scenario-chat-context-construction-snapshot-with-asof`.
- [ ] 3.2 Справочник источников данных (журнал, корпус правил, рынок Bybit) и параметр чата: фильтрация реестра инструментов по набору источников, дефолт — все три. Проверить: `requirement-sources-closed-catalog` — `scenario-sources-three-categories`, `scenario-sources-subset-parameter`, `scenario-sources-write-never`; `requirement-sources-read-only-tool-registry` — `scenario-sources-registry-matches-chat-sources`.
- [ ] 3.3 ИИ-модель как параметр чата: дефолт GLM-5.3 из `Llm:Chat:Model`, смена на лету без переписывания истории. Проверить: `requirement-sources-model-is-chat-parameter` — `scenario-sources-default-model-glm`, `scenario-sources-model-switch-mid-chat`.
- [ ] 3.4 След источников в ответе ИИ-помощника: обобщение рыночного следа — инструменты, ссылки на карточки правил и данные журнала с as-of. Проверить: `requirement-chat-message-composition` — `scenario-chat-source-trace-persisted`.

## 4. Приёмка

- [ ] 4.1 Контракт-тесты детерминированных частей (снимок, реестр инструментов, хранение, жизненный цикл, пересбор) зелёные; LLM-вывод фикстурно не тестируется. Проверить: `openspec validate add-agent-chat --strict`.
- [ ] 4.2 Живая приёмка владельцем: чат с конструкцией и без, завершение/продолжение, смена модели на лету, набор источников, деградация при недоступной бирже. Проверить: протокол приёмки владельцем.
