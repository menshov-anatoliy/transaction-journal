# Tasks

## 1. Проект окружения и порты

- [x] 1.1 Создать проект `src/TransactionJournal.Consultations` (зависимость только от Domain), добавить в solution и проверить сборку; зеркальные тесты структуры расширить на новый проект. Проверить: `requirement-solution-five-projects` — сценарий `scenario-consultations-own-environment-project`; сборка решения зелёная.
- [x] 1.2 Объявить порты в проекте: `IConsultationContextReader` (снимок контекста), `IRuleCorpusReader` (индекс и карточки корпуса), `IConsultationStore` (диалоги и сообщения); убедиться в отсутствии ссылки Consultations → Hints. Проверить: `requirement-dependencies-point-inward` — сценарий `scenario-environments-not-linked`.

## 2. Снимок контекста и инструкции агента

- [x] 2.1 Адаптер `IConsultationContextReader` в Infrastructure поверх read-моделей ConstructionDetail/JournalMetrics: markdown-снимок «конструкция + портфельные агрегаты + лимиты» с as-of каждого раздела; живые подсказки движка в снимок не попадают. Проверить: `requirement-context-deterministic-snapshot` — сценарии `scenario-context-primary-facts-with-asof`, `scenario-context-hints-excluded`.
- [x] 2.2 Адаптер `IRuleCorpusReader` в composition root поверх `RulesCorpusLoader`: компактный индекс всех карточек (id + краткое содержание); полный текст — только по id. Проверить: `scenario-context-card-index-only`.
- [x] 2.3 Инструкции агента `consultation-prompt.md`: настраиваемый путь, дефолт рядом с `rules/`, встроенный дефолт в коде; обязательные принципы — сценарии «если/то» без прогнозов цены, цитирование id карточек, маркировка «вне корпуса правил», пост-мортем закрытых конструкций. Проверить: `requirement-context-agent-instructions-file`, `requirement-context-scenario-conduct`, `requirement-context-postmortem-mode` — контракт-тест состава дефолтных инструкций и сценарии `scenario-context-instructions-override`, `scenario-context-instructions-missing-ok`.

## 3. Инструменты и агентный цикл

- [x] 3.1 Расширить DTO `BybitTicker` опциональными полями (греки/IV/OI/бид-аск/фандинг) тем же эндпоинтом; убедиться, что поведение провайдера марок и PnL не изменилось (существующие тесты зелёные). Проверить: регресс существующих потребителей `BybitTickersClient`.
- [ ] 3.2 Реализовать три read-only инструмента `read_rule_card`, `get_market_snapshot`, `get_option_board` через один `BybitTickersClient`: один вызов = один HTTP-запрос, доска — компактная проекция. Проверить: `requirement-tools-read-only-registry`, `requirement-tools-single-request-per-call` — сценарии `scenario-tools-card-by-id`, `scenario-tools-option-board-projection`, `scenario-tools-no-write-tools`, `scenario-tools-iteration-cap`.
- [ ] 3.3 Агентный цикл на keyed `IChatClient` с `UseFunctionInvocation`, `MaximumIterationsPerRequest = 6`; конфигурация модели отдельной секцией (дефолт z.ai GLM-5.3), регистрация в composition root. Проверить: `requirement-tools-chat-model-configurable` — сценарий `scenario-tools-model-switch-config`.
- [ ] 3.4 Деградация при недоступности Bybit: тул возвращает «недоступно + кэшированная проекция с as-of» из кэша `InstrumentMarkProvider`; агент продолжает ответ по журналу и корпусу. Проверить: `requirement-tools-degradation-cached-asof` — сценарии `scenario-tools-market-down-cached-projection`, `scenario-tools-stale-asof-no-market-advice`.

## 4. Персистентность истории

- [ ] 4.1 `ConsultationStore` в Infrastructure по образцу `HintStore`: SQLite per construction, диалоги и сообщения (роль, текст, as-of); диалог создаётся первым сообщением, удаляется целиком; изоляция историй диалогов. Проверить: `requirement-history-dialogue-per-question` — сценарии `scenario-history-created-by-first-message`, `scenario-history-hard-delete-dialogue`, `scenario-history-dialogues-isolated`.
- [ ] 4.2 Рыночный след ответа ассистента (вызванные инструменты + as-of) сохраняется в сообщении; пересбор конструкции стирает её консультации. Проверить: `requirement-history-message-composition` — `scenario-history-market-trace-persisted`; `requirement-history-environment-record` — `scenario-history-rebuild-wipes`.

## 5. Панель UI и сквозная приёмка

- [ ] 5.1 Панель «Консультации» на экране деталей конструкции: список диалогов, ввод, создание диалога первым сообщением, удаление диалога; read-only — без мутаций журнала и сохранения планов. Проверить: `requirement-ui-consultation-panel` — сценарии `scenario-ui-dialogue-started-by-message`, `scenario-ui-chat-read-only`.
- [ ] 5.2 Стриминг ответа: троттлинг перерисовок ~150 мс, статусные строки tool-вызовов между сегментами без рендера tool-call фрагментов, отмена кнопкой; закрытая конструкция — пост-мортем. Проверить: `scenario-ui-streaming-with-tool-status`, `scenario-ui-cancel-generation`, `scenario-ui-closed-construction-postmortem`.
- [ ] 5.3 Сквозной прогон на живом журнале владельца: консультация открытой и закрытой конструкции, деградация при недоступной бирже, проверка as-of и рыночного следа; живая приёмка владельцем принципов «если/то», цитирования и маркировки «вне корпуса». Проверить: протокол приёмки владельцем; `openspec validate add-assistant-chat --strict`.
