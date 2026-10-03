# Proposal

## Why

После решения тикета [#35](https://github.com/menshov-anatoliy/transaction-journal/issues/35) секреты каналов сводки (токен Telegram-бота, ключ z.ai) живут в `appsettings.Local.json` рядом с exe, а ключи Bybit остались в параллельном механизме `.env` → переменные окружения. У одного локального приложения два магазина секретов и два способа их загрузки. Консолидацию делаем сейчас, до старта DDD-реструктуризации (change `restructure-ddd-solution`), чтобы фаза 1 не перевозила обречённый код `AppEnvFile` и все фазы строились на одном механизме.

## What Changes

- Провайдер учётных данных Bybit читает `IConfiguration` (ключи `Bybit:ApiKey` / `Bybit:ApiSecret`) вместо переменных окружения `BYBIT_API_KEY` / `BYBIT_API_SECRET`; порт `IBybitCredentialsProvider` не меняется.
- `appsettings.Local.json` рядом с exe подключается в `Program.cs` как optional-оверрайд конфигурации и становится единым файлом локальных секретов (Bybit, Telegram, z.ai — секции Telegram/Llm уже там по [#35](https://github.com/menshov-anatoliy/transaction-journal/issues/35)).
- Удаляются `AppEnvFile`, пакет DotNetEnv и шаблон `.env.example`; capability `config/env-file` выводится из эксплуатации целиком. Роль шаблона принимает `appsettings.Local.json.example` с секциями `Bybit`, `Telegram` и `Llm`.
- Экран «Настройки» указывает `appsettings.Local.json` как место хранения секрета вместо переменных окружения.
- **BREAKING** (локальное развёртывание): значения из `.env` больше не подхватываются — владельцу нужно перенести `BYBIT_API_KEY`/`BYBIT_API_SECRET` в секцию `Bybit` файла `appsettings.Local.json` и удалить `.env`; системные переменные `Bybit__ApiKey`/`Bybit__ApiSecret` (решение [#4](https://github.com/menshov-anatoliy/transaction-journal/issues/4)) остаются рабочим резервом через env-провайдера конфигурации.

## Capabilities

### New Capabilities
- `config/local-secrets`: локальные секреты приложения в одном файле `appsettings.Local.json` рядом с exe — optional-оверрайд конфигурации с высшим приоритетом, вне системы контроля версий; отсутствие файла — штатная ситуация.

### Modified Capabilities
- `ui/screens`: требование `requirement-settings-screen` — экран «Настройки» указывает `appsettings.Local.json` (а не переменные окружения) как место хранения секрета.
- `config/env-file`: capability удаляется целиком — все три требования выводятся (REMOVED) вместе с механизмом `.env`.

## Impact

- Код: `src/TransactionJournal/Bybit/EnvironmentBybitCredentialsProvider.cs` (замена на конфигурационную реализацию), `src/TransactionJournal/Program.cs` (`AddJsonFile` для local-оверрайда, удаление `AppEnvFile.Load()`, регистрация поставщика), удаление `src/TransactionJournal/AppEnvFile.cs`, `Components/Pages/SettingsReadModel.cs` и `Components/Pages/Settings.razor` (тексты о месте хранения секрета), удаление `.env.example`, отказ от пакета DotNetEnv в csproj.
- Тесты: удаление `AppEnvFileTests`, переработка `EnvironmentBybitCredentialsProviderTests` на конфигурацию, правка ожиданий `SettingsScreenTests`.
- Ручной шаг владельца: перенести значения ключей Bybit в секцию `Bybit` файла `appsettings.Local.json`, удалить `.env` (файл становится мёртвым).
- Не затрагивается: авторинг-change'ы фаз (`restructure-ddd-solution`, `add-hints-engine`, `add-summary-channels`) механизм секретов не упоминают; подключение `appsettings.Local.json` в задаче 2.1 `add-summary-channels` переносится сюда и там не дублируется.
