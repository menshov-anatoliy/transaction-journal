# Tasks

## 1. Общий резолвер провайдера (composition root)

- [ ] 1.1 Создать `src/TransactionJournal/LlmProviderSettings.cs`: статический `Resolve(provider, baseUrl, apiKey)` — дефолты `zai` и `https://api.z.ai/api/paas/v4`, правило «провайдер ≠ `zai` без явного `BaseUrl` → `InvalidOperationException`», нормализация trailing slash, тримминг значений; XML-док и traceability-комментарий `openspec:config/llm-provider#requirement-llm-provider-shared-section` с человекочитаемым пояснением. Проверить: `dotnet build` проекта Web.
- [ ] 1.2 Тесты `tests/TransactionJournal.Tests/Web/LlmProviderSettingsTests.cs`: дефолты без секции (`scenario-llm-provider-defaults`), сторонний провайдер без эндпоинта отклоняется (`scenario-llm-provider-foreign-requires-base-url`), эндпоинт без слэша нормализуется (`scenario-llm-provider-base-url-slash-normalized`), значения триммируются; стиль как в `ConsultationChatModelOptionsTests`. Проверить: `dotnet test --filter LlmProviderSettingsTests` зелёный.

## 2. Модель чата консультаций на Llm:Chat

- [ ] 2.1 `ConsultationChatModelOptions`: сборка из общих настроек резолвера + `Llm:Chat:Model` (дефолт `glm-5.3`, тримминг); правило стороннего провайдера из класса удалить (переехало в `LlmProviderSettings`); обновить XML-док и traceability на `openspec:config/llm-provider#requirement-llm-model-subsections`. Проверить: `dotnet build` решения.
- [ ] 2.2 Обновить `ConsultationChatModelOptionsTests`: дефолт модели без подсекции, смена модели конфигурацией (`scenario-llm-models-chat-switch`), пустая подсекция даёт дефолт (`scenario-llm-models-blank-defaults`); кейс стороннего провайдера перенесён в 1.2. Проверить: `dotnet test --filter ConsultationChatModelOptionsTests` зелёный.
- [ ] 2.3 `Program.cs`: резолв общих настроек и модели чата из `Llm:Chat:Model` (запасной источник ключа упразднён), текст ошибки в `CreateConsultationChatClient` — про `Llm:ApiKey`; обновить traceability-комментарии (`consultations/tools` + `config/llm-provider`). Проверить: `dotnet build`, `rg "Consultations:ChatModel" src` пуст.

## 3. Контракт Llm:Hint

- [ ] 3.1 Создать `src/TransactionJournal.Hints/HintChatModelOptions.cs`: record с дефолтом `glm-5.3-flash`, сборка из общих настроек + `Llm:Hint:Model`; XML-док с пояснением, что потребитель — изложение сводок (`add-summary-channels`, задача 4.3), traceability `openspec:config/llm-provider#requirement-llm-model-subsections`. Проверить: `dotnet build` решения.
- [ ] 3.2 Зарегистрировать singleton `HintChatModelOptions` в `Program.cs` из общих настроек и `Llm:Hint:Model`. Проверить: `dotnet build`, приложение стартует.
- [ ] 3.3 Тесты `tests/TransactionJournal.Tests/Hints/HintChatModelOptionsTests.cs`: дефолт Flash без подсекции, смена модели конфигурацией (`scenario-llm-models-hint-switch`), пустая подсекция даёт дефолт (`scenario-llm-models-blank-defaults`). Проверить: `dotnet test --filter HintChatModelOptionsTests` зелёный.

## 4. Конфигурация и шаблон

- [ ] 4.1 `src/TransactionJournal/appsettings.Local.json.example`: секция `Llm` = `Provider`, `BaseUrl` (`https://api.z.ai/api/paas/v4`), `ApiKey` и подсекции `Chat`/`Hint` с заглушками моделей; упоминание `Consultations:ChatModel` и `Llm:ZaiApiKey` убрать. Проверить: секции шаблона соответствуют `config/llm-provider`, рассинхрон устранён.
- [ ] 4.2 Локальный `appsettings.Local.json` (вне репозитория): ключ в `Llm:ApiKey`, модели в `Llm:Chat:Model`/`Llm:Hint:Model`, секция `Consultations` и мёртвые ключи удалены. Проверить: валидный JSON, приложение стартует, чат консультаций отвечает на первый вопрос.

## 5. Интеграционная верификация

- [ ] 5.1 Прогнать целевые тесты change'а одним запуском (`--filter` по `LlmProviderSettingsTests|ConsultationChatModelOptionsTests|HintChatModelOptionsTests`), полный `dotnet build` и `openspec validate refactor-llm-config --strict`. Проверить: тесты зелёные, валидация проходит.
