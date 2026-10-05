# Протокол приёмки

- **Дата прогона:** 2026-10-05
- **Команды:** `dotnet test`; `openspec validate add-assistant-chat --strict`; живой прогон панели «Консультации» на живом журнале владельца через Playwright (Chromium недоступен из сети — использован локальный Edge, `channel=msedge`, headless) против `http://localhost:5019`
- **Итог тестов:** пройдено 990, не пройдено 1, пропущено 0 (всего 991). Единственный отказ — `BybitStatementDiagnosticRunTests.TryIfLiveDataReconcilesWithBybitStatement`: разовая сверка живой базы `App_Data\journal.db` с CSV-выгрузкой Bybit `examples\2026-01-01 до 2026-09-22` (16 TRADE-строк выгрузки отсутствуют в журнале). К change отношения не имеет: change не трогает код синхронизации (из существующих файлов — только DTO `BybitTicker`/`BybitTickerQuery`); в worktree пре-change коммита `0f151a6` тест пропускается (нет живых данных по пути сборки). Данные, а не код.
- **Валидация:** `openspec validate add-assistant-chat --strict` — change валиден.
- **Конфигурация живого прогона:** модель GLM-5.3, провайдер z.ai (дефолт кода); ключ и эндпоинт — в gitignored `appsettings.Local.json`: ключ GLM Coding Plan работает только на coding-эндпоинте, задан `Consultations:ChatModel:BaseUrl=https://api.z.ai/api/coding/paas/v4/` — живое подтверждение `scenario-tools-model-switch-config` (смена эндпоинта/модели конфигурацией без правки кода; также в ходе прогона переключались модель glm-5.3-flash и, до получения нового ключа, диагностировался 429 общего эндпоинта).
- **Тестовые данные живого прогона:** живой журнал владельца; открытая конструкция 6 «ETH стреддл 25DEC26 1900» (страйк 1900, колл-нога с частичной фиксацией, пут-нога открыта), закрытая конструкция 7 «ETH направленная PUT 25SEP26 1600» (пост-мортем); тёплый кэш марок `InstrumentMarkProvider` в журнале; следы консультаций — SQLite `App_Data\consultations\construction-6.db` (диалоги 5, 6, 7, 8) и `construction-7.db` (диалог 1).

## Чек-лист по сценариям `specs/ui/screens/spec.md`

### Requirement: Панель «Консультации» в деталях конструкции

| Сценарий | Проверяющие тесты | Живой прогон | Результат |
|---|---|---|---|
| `scenario-ui-dialogue-started-by-message` | `ConsultationsPanelTests.TryIfFirstMessageSent_DialogueAppearsInListWithAnswer`; `ConsultationStoreTests.TryIfFirstMessage_CreatesNewDialogue` | Диалоги 5/6/7/8 (конструкция 6) и 1 (конструкция 7) каждый создан первым сообщением, появился в списке с таймстампом | ✅ |
| `scenario-ui-streaming-with-tool-status` | `ConsultationsPanelTests.TryIfStreamHasToolCalls_StatusLinesShownAndFragmentsNotRendered`; `ConsultationStreamThrottleTests.TryIfChunksArriveFasterThanInterval_RenderPermittedOnlyPerInterval` | Сценарий A: стриминг ответа 0→431→1306→1793→3333→4344→5389 знаков; статусные строки `get_market_snapshot(baseCoin=ETH) — готово`, `read_rule_card(cardId=ac-02) — готово` | ✅ |
| `scenario-ui-cancel-generation` | `ConsultationsPanelTests.TryIfCancelClickedDuringGeneration_StreamStopsAndInputReturns` | Диалог 6: «Отмена» нажата в начале стрима (79 знаков текста); плашка «Генерация отменена — вопрос остался в диалоге без ответа помощника», ввод вернулся, кнопка «Отмена» исчезла; в БД диалога только сообщение «Вы» | ✅ |
| `scenario-ui-closed-construction-postmortem` | `ConsultationsPanelTests.TryIfConstructionClosed_PanelShowsPostmortemMode` | Сценарий B (конструкция 7): панель показала пометку «Конструкция закрыта — консультации ведутся в режиме пост-мортема», ответ — разбор «что сработало / где ошибки исполнения / что изменить» | ✅ |
| `scenario-ui-chat-read-only` | `ConsultationsPanelTests.TryIfChatReadOnly_PanelOffersNoJournalWritesOrPlanSaving` | Панель не предлагает мутаций журнала; записи конструкций 6/7 за три консультации не изменились (записи читаются, тулы реестра read-only) | ✅ |

## Чек-лист по сценариям `specs/consultations/tools/spec.md`

### Requirement: Инструменты строго read-only из одного реестра

| Сценарий | Проверяющие тесты | Живой прогон | Результат |
|---|---|---|---|
| `scenario-tools-card-by-id` | `ConsultationToolsTests.ReadRuleCardAsync_ReturnsFullTextOrNotFoundMessage`; `RulesCorpusConsultationAdapterTests` (5 тестов) | Сценарий A: ~20 карточек процитировано по id (ac-02, ac-12, ac-14…ac-59); сценарий C: 4–5 вызовов `read_rule_card` на ответ, тексты карточек в ответах совпадают с корпусом | ✅ |
| `scenario-tools-option-board-projection` | `ConsultationToolsTests.GetOptionBoardAsync_RendersCompactProjection`; `BybitConsultationMarketReaderTests.ReadOptionBoardAsync_SingleOptionRequestAndQuotesProjection` | Сценарий A: доска ETH спроецирована компактно, as-of в шапке ответа («рынок as-of 2026-10-05 09:05 UTC»); ассистент честно пометил «страйк 1900 вне окна проекции доски ±20%» | ✅ |
| `scenario-tools-no-write-tools` | `ConsultationToolsTests.CreateTools_ExactlyThreeReadOnlyTools` | Во всех следах живых ответов только три имени: `get_market_snapshot`, `get_option_board`, `read_rule_card` | ✅ |

### Requirement: Один вызов рыночного инструмента — один биржевой запрос

| Сценарий | Проверяющие тесты | Живой прогон | Результат |
|---|---|---|---|
| `scenario-tools-iteration-cap` | `ConsultationAgentTests.TryIfNeverEndingToolCallsStopAtIterationCap`; `ConsultationToolsTests.GetMarketSnapshotAsync_SingleCall_PerformsSinglePortRead`; `BybitConsultationMarketReaderTests.ReadSnapshotAsync_SingleLinearRequestAndProjection` | Максимум 2 тул-вызова на ответ (кап 6 не достигнут); вызовы журнальных тулов не порождают видимых лишних биржевых обращений | ✅ |

### Requirement: Недоступность рынка деградирует в кэш с явным as-of

| Сценарий | Проверяющие тесты | Живой прогон | Результат |
|---|---|---|---|
| `scenario-tools-market-down-cached-projection` | `BybitConsultationMarketReaderTests` — 7 тестов деградации, включая транспортные `ReadSnapshotAsync_TransportOutage_*` и `ReadOptionBoardAsync_TransportOutage_*`; `ConsultationToolsTests.GetMarketSnapshotAsync_UnavailableWithCache_RendersCachedMarkWithItsAsOf`, `GetOptionBoardAsync_UnavailableWithCache_RendersCachedMarksTableWithItsAsOf`, `GetMarketSnapshotAsync_UnavailableWithoutCache_SaysNoCachedMark` | Сценарий C: `Bybit:BaseUrl=http://127.0.0.1:9` (отказ соединения) → инструмент ответил структурированным «биржа недоступна (подключение отвергнуто)»; ассистент передал кэш-марку ETHUSDT 2698.87 с as-of 2026-10-04 12:44 UTC («устарела примерно на сутки») и кэш-проекцию доски — 2 котировки ETH-25DEC26 as-of 09:10 UTC, без IV; след (`MarketTraceJson` диалога 8): `get_market_snapshot` as-of кэша, `get_option_board` as-of кэша | ✅ (после дефекта, см. «Замечания») |
| `scenario-tools-stale-asof-no-market-advice` | `ConsultationInstructionsTests.TryIfDefaultInstructionsCarryMandatoryPrinciples` | Сценарий C: «Рыночно-зависимых рекомендаций не даю — отвечаю по журналу и корпусу правил»; сценарий «если биржа всё ещё недоступна или as-of старше текущей сессии — рыночно-зависимые решения не принимать»; докупка колла по прогнозу помечена «вне корпуса правил» | ✅ |

### Requirement: Модель чата задаётся отдельной конфигурацией

| Сценарий | Проверяющие тесты | Живой прогон | Результат |
|---|---|---|---|
| `scenario-tools-model-switch-config` | `ConsultationChatModelOptionsTests.TryIfSectionAbsentDefaultsToZaiGlm`; `TryIfConfigNamesOtherProviderThenResolved` | В ходе прогона конфигурацией сменились: модель glm-5.3→glm-5.3-flash и обратно, эндпоинт z.ai общий→coding; всё — правкой `appsettings.Local.json` без пересборки | ✅ |

## Чек-лист по сценариям `specs/consultations/history/spec.md`

### Requirement: Консультация — контейнер диалогов «один вопрос — один диалог»

| Сценарий | Проверяющие тесты | Живой прогон | Результат |
|---|---|---|---|
| `scenario-history-created-by-first-message` | `ConsultationStoreTests.TryIfFirstMessage_CreatesNewDialogue` | См. `scenario-ui-dialogue-started-by-message` | ✅ |
| `scenario-history-hard-delete-dialogue` | `ConsultationStoreTests.TryIfDeleteDialogue_RemovesDialogueWithAllMessages`; `ConsultationsPanelTests.TryIfDialogueDeleted_ConfirmedHardDeleteRemovesThread` | Диалог первой попытки прогона (429 z.ai) удалён через «удалить…» + подтверждение; строка и тред исчезли | ✅ |
| `scenario-history-dialogues-isolated` | `ConsultationStoreTests.TryIfDialogues_ListMessagesOnlyOwnHistory`; `ConsultationChatServiceTests.TryIfFollowUpWithoutTools_HistoryPrecedesQuestionAndTraceStaysNull` | Диалоги 5–8 в списке конструкции 6 открываются каждый со своим тредом, чужих сообщений нет | ✅ |

### Requirement: Сообщение хранит роль, текст, as-of и рыночный след

| Сценарий | Проверяющие тесты | Живой прогон | Результат |
|---|---|---|---|
| `scenario-history-market-trace-persisted` | `ConsultationChatServiceTests.TryIfAssistantUsedMarketTool_TracePersistedInMessage`; `ConsultationAgentTests.TryIfMarketToolInvoked_TraceRecordsInvocationWithAsOf`, `TryIfAnswerWithoutTools_TraceStaysEmpty`; `ConsultationStoreTests.TryIfAssistantMessage_KeepsMarketTrace` | SQLite-след диалога 5 (A): `get_market_snapshot` as-of 09:05:54 UTC, `get_option_board` as-of 09:05:55 UTC, `read_rule_card` — null; у user-сообщений след null. Диалог 8 (C): as-of обеих рыночных позиций следа — из кэша марок. Диалог 1 (B): модель отказалась от рыночных тулов по истёкшей серии — след из одного вызова карточки | ✅ |

### Requirement: История — запись окружения per construction / Компакция отсутствует

| Сценарий | Проверяющие тесты | Живой прогон | Результат |
|---|---|---|---|
| `scenario-history-rebuild-wipes` | `ConstructionAssemblyServiceTests` (тест `scenario-history-rebuild-wipes`); `ConsultationStoreTests.TryIfDeleteForConstruction_WipesAllDialoguesAndMessages` | Не прогонялся живьём (разрушительный); покрыт тестами per-construction окружения | ✅ (тесты) |
| `scenario-history-domain-agnostic` | `SolutionStructureTests.ConsultationsReferencesOnlyDomain`, `EnvironmentProjectsAreNotLinked` | — | ✅ (сборка/тесты) |
| `scenario-history-fresh-dialogue` | `ConsultationStoreTests.TryIfFollowUpMessage_AppendsToExistingDialogue`; `ConsultationAgentTests.TryIfHistoryPrecedesQuestion` | Диалог 8 начат «с чистого листа» после пост-мортемных 7: ответ не тащит контекст предыдущего диалога, история предыдущих не компактится | ✅ |

## Чек-лист по сценариям `specs/consultations/context/spec.md`

### Requirement: Снимок контекста собирается детерминированно кодом; инструкции — файл

| Сценарий | Проверяющие тесты | Живой прогон | Результат |
|---|---|---|---|
| `scenario-context-primary-facts-with-asof` | `ConsultationContextReaderTests.TryIfSnapshotCarriesPrimaryFactsWithPerSectionAsOf` | Ответы опираются на первичные факты журнала: вход колла 285.6, частичный выход 694.9 (+143% на закрытой части), реализованный PnL +31.3473 USDT, «нереализованный не считается (сбой марок)» | ✅ |
| `scenario-context-hints-excluded` | `ConsultationContextReaderTests.TryIfLiveHintsStayOutOfSnapshot` | Живые подсказки движка в ответах не фигурируют | ✅ |
| `scenario-context-card-index-only` | `ConsultationContextReaderTests.TryIfRuleIndexStaysCompactWithoutFullText`; `RulesCorpusConsultationAdapterTests` | Карточки читаются тулаом по id из индекса, полный текст карточек в контексте не раздаётся | ✅ |
| `scenario-context-instructions-override` | `ConsultationInstructionsTests.TryIfValidFileOverridesBuiltInDefault` | Прогон на дефолтном `consultation-prompt.md` (переопределение не задавалось) | ✅ (тест) |
| `scenario-context-instructions-missing-ok` | `ConsultationInstructionsTests` — 3 теста деградации файла | — | ✅ (тесты) |

### Requirement: Ответы — сценарии «если/то» с опорой на корпус; пост-мортем

| Сценарий | Проверяющие тесты | Живой прогон | Результат |
|---|---|---|---|
| `scenario-context-in-corpus-citation` | `ConsultationInstructionsTests.TryIfDefaultInstructionsCarryMandatoryPrinciples` | Сценарий A: каждый внутрекорпусный вариант сопровождён карточкой (ac-02…ac-59); сценарий C: ac-01, ac-02, ac-07, ac-17, ac-28, ac-29, ac-34, ac-47, ac-53, ac-62, ac-63 | ✅ |
| `scenario-context-out-of-corpus-marked` | — (принцип дефолтных инструкций) | Сценарий A: «держать до экспирации» — **вне корпуса правил**; B: две пометки; C: «докупка по направленному прогнозу — вне корпуса правил» | ✅ |
| `scenario-context-no-price-forecasts` | — (принцип дефолтных инструкций) | Прогнозов цены нет; вместо них условия «если марка окажется выше/ниже…»; окно проекции доски честно ограничено (страйк 1900 — вне окна, помечено) | ✅ |
| `scenario-context-closed-postmortem` | `ConsultationContextReaderTests.TryIfClosedConstructionSwitchesSnapshotToPostmortem` | Сценарий B: полный пост-мортем закрытой конструкции 7; рыночные тулы по истёкшей серии не вызывались («серия ETH-25SEP26 истекла») | ✅ |

## Чек-лист `specs/architecture/solution-structure/spec.md` (требования change)

| Сценарий | Проверяющие тесты | Результат |
|---|---|---|
| `scenario-consultations-own-environment-project` | `SolutionStructureTests.SolutionIncludesConsultationsProject`, `ConsultationsReferencesOnlyDomain` | ✅ |
| `scenario-environments-not-linked` | `SolutionStructureTests.EnvironmentProjectsAreNotLinked` | ✅ |

## Живой прогон: протокол сценариев

### Сценарий A — открытая конструкция 6 «ETH стреддл 25DEC26 1900» (12:05–12:12 UTC)

Вопрос владельческий: «Дай сценарии если/то по управлению конструкцией». Наблюдения:

- Диалог 5 создан первым сообщением; стриминг с нарастанием 0→5389 знаков; статусные строки тулов с переходом pending→completed.
- Ответ: сценарии «если/то» по всему тексту (смещение, интрадей, экспирация, ролл, перестройка), ~20 процитированных карточек, вариант «держать до экспирации» помечен «вне корпуса правил», as-of в шапке («рынок as-of 2026-10-05 09:05 UTC»), честная пометка «страйк 1900 вне окна проекции доски ±20%».
- След в `construction-6.db` (диалог 5): `get_market_snapshot(ETH)` as-of 09:05:54, `get_option_board(ETH)` as-of 09:05:55, далее `read_rule_card` — as-of null; у user-сообщения след отсутствует.

### Сценарий B — закрытая конструкция 7 «ETH направленная PUT 25SEP26 1600» (12:10 UTC)

- Панель показала режим пост-мортема; ответ — структурированный разбор: что сработало, где ошибки исполнения, что изменить; сценарии «если/то» с карточками; две пометки «вне корпуса правил».
- Модель отказалась от рыночных тулов: серия ETH-25SEP26 истекла; след — один вызов карточки.

### Сценарий C — отмена генерации и деградация при недоступной бирже (12:29 и 12:54 UTC)

1. **Отмена (диалог 6):** вопрос отправлен, «Отмена» нажата на начале стрима; плашка «Генерация отменена — вопрос остался в диалоге без ответа помощника», ввод вернулся, повторная отправка возможна. В БД диалога только User-сообщение.
2. **Деградация (диалоги 7, 8):** `Bybit:BaseUrl` переключён на `http://127.0.0.1:9` (отказ соединения, транспортный сбой после ретраев resilience). Вопрос: марка ETHUSDT, IV декабрьской доски, можно ли докупать колл.
   - Первая попытка (диалог 7, до фикса) выявила дефект — см. «Замечания»: инструмент вернул модели ошибку вместо структурированной недоступности; ассистент тем не менее ответил безопасно (по журналу и корпусу, без рыночных рекомендаций).
   - После фикса (диалог 8): инструмент отдал структурированное «биржа недоступна (подключение отвергнуто)» с кэш-маркой 2698.87 (as-of 2026-10-04 12:44 UTC, «устарела примерно на сутки») и кэш-проекцией доски (2 котировки ETH-25DEC26, as-of 09:10 UTC, без IV). Ассистент: «рыночно-зависимых решений не принимать», сценарий «если/то» по журналу и корпусу, «докупка по прогнозу — вне корпуса правил», замечания по пробелам журнала (лимиты, капитал, страйки усреднения не заданы).
   - След диалога 8: обе рыночные позиции с as-of из кэша марок, карточки — null.

## Замечания прогона

- **Дефект, найденный и исправленный живым прогоном.** Деградация рыночных тулов ловила только `BybitApiException` (ошибки конверта/HTTP-статуса биржи); транспортные сбои после ретраев Polly (`HttpRequestException`, таймаут HTTP) улетали исключением в тул — модель получала error-результат вместо структурированного «недоступно + кэш», вызов не попадал в рыночный след. Это противоречит `requirement-tools-degradation-cached-asof`: «биржа недоступна» не ограничена классом сбоя. Исправлено в `BybitConsultationMarketReader`: деградация вынесена в общие хелперы, покрыта и транспортными сбоями (отмена вызывающим кодом не глотается); добавлены тесты `ReadSnapshotAsync_TransportOutage_*`, `ReadOptionBoardAsync_TransportOutage_*`. Живой перепрогон подтверждает.
- Пользовательское поведение ассистента оставалось безопасным и до фикса: на error-результат тулов модель сама отказалась от рыночных рекомендаций — принципы корпуса в инструкции работают как страховка, но структурированную деградацию это не заменяет.
- Прочие наблюдения: панель и страница конструкции рендерятся после отработки ретраев марок недоступной биржи (медленный первый рендер страницы при лежащем Bybit — известная деградация чтения журнала, вне этого change); UI-статусы тулов отражают стриминговые вызовы функции, включая неудавшиеся (полный состав вызовов виден в следе сообщения).
- Конфигурация прогона: ключ GLM Coding Plan работает только на coding-эндпоинте `https://api.z.ai/api/coding/paas/v4/`; `appsettings.Local.json` gitignored и в репозиторий не попадает. Движку подсказок (`Llm:*`) по-прежнему указан общий эндпоинт — вне рамок этого change.
- Покрытие признано достаточным: все 27 сценариев change имеют целевые тесты с метками `Traceability: openspec:...#scenario-*`, ключевые принципы («если/то», цитирование, «вне корпуса», as-of и деградация, read-only, изоляция диалогов) дополнительно подтверждены живым прогоном на реальных данных владельца.

## Вердикт владельца

*(заполняется владельцем по результатам живой приёмки)*

- Принцип «если/то» в ответах: ________
- Цитирование карточек корпуса: ________
- Маркировка «вне корпуса правил»: ________
- Поведение при недоступной бирже (кэш с as-of, без рыночных рекомендаций): ________
- Итоговое решение change: ________
