# Tasks

## 1. Конфигурация и поставщик ключей

- [x] 1.1 Подключить в `Program.cs` оверрайд `builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)` (решение D2), удалить вызов `AppEnvFile.Load()` и файл `src/TransactionJournal/AppEnvFile.cs`; убедиться, что `dotnet build` проходит, а запуск без `appsettings.Local.json` не сообщает об ошибке
- [x] 1.2 Заменить `EnvironmentBybitCredentialsProvider` на `ConfigurationBybitCredentialsProvider` (решение D1): чтение `Bybit:ApiKey`/`Bybit:ApiSecret` из `IConfiguration`, публичные константы имён ключей, ошибка конфигурации называет файл и ключ; обновить регистрацию в `Program.cs`; убедиться, что сборка проходит

## 2. Очистка механизма .env

- [x] 2.1 Удалить `tests/TransactionJournal.Tests/AppEnvFileTests.cs`, `.env.example` и ссылку на пакет DotNetEnv из `src/TransactionJournal/TransactionJournal.csproj` (решение D4); убедиться, что `dotnet build` зелёный, а `git status` не показывает локальный `.env` (строка остаётся в `.gitignore`)

## 3. Тесты

- [x] 3.1 Переработать тесты поставщика на in-memory `IConfiguration`: значения `Bybit:ApiKey`/`Bybit:ApiSecret` доезжают до `GetCredentials()` (сценарий `scenario-local-secrets-reach-consumers`), отсутствие или пустота ключа — `InvalidOperationException` с именем файла и ключа (сценарий `scenario-local-secrets-missing-file-normal`); проставить метки Traceability и убедиться, что тесты проходят
- [x] 3.2 Добавить тест приоритета на композиции провайдеров как в `Program.cs`: значение из `appsettings.Local.json` (временный файл) побеждает системную переменную вида `Bybit__ApiKey` (сценарий `scenario-local-secrets-override-system-vars`); убедиться, что тест проходит
- [x] 3.3 Обновить `SettingsScreenTests`: экран указывает `appsettings.Local.json` как место хранения секрета, маска ключа и запрет ввода секрета не меняются (сценарий `scenario-settings-secret-never-displayed`); убедиться, что тесты проходят

## 4. Шаблон и валидация

- [x] 4.1 Создать `src/TransactionJournal/appsettings.Local.json.example` с секциями `Bybit`, `Telegram`, `Llm`, заглушками и русскими комментариями о назначении и приоритете (решение D6); проверить, что `git check-ignore src/TransactionJournal/appsettings.Local.json` подтверждает игнор реального файла, а шаблон коммитится (сценарий `scenario-local-secrets-template-copied`)
- [x] 4.2 Прогнать полный набор тестов решения (`dotnet test`) и `openspec validate consolidate-secrets-local-json --strict`; убедиться, что всё зелёное
- [ ] 4.3 Smoke-проверка вручную: владелец дополняет свой `appsettings.Local.json` секцией `Bybit` из `.env`, удаляет `.env`; экран «Настроек» показывает маску ключа, ручная синхронизация проходит (Migration Plan, шаги 2–3)
