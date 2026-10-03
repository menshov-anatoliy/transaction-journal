# Design

## Context

Сейчас ключи Bybit доходят до потребителя цепочкой `.env` → `AppEnvFile.Load()` → переменные процесса → `EnvironmentBybitCredentialsProvider` (порт `IBybitCredentialsProvider`), а экран «Настройки» (`SettingsReadModel`, `Settings.razor`) показывает имена переменных `BYBIT_API_KEY`/`BYBIT_API_SECRET` как место хранения секрета. Секреты каналов сводки уже живут в `appsettings.Local.json` (тикет [#35](https://github.com/menshov-anatoliy/transaction-journal/issues/35)). Мотивация консолидации — в proposal.md (Why).

## Goals / Non-Goals

Goals:
- Один механизм локальных секретов: `appsettings.Local.json` как optional-оверрайд `IConfiguration` с высшим приоритетом.
- Убрать `AppEnvFile`, пакет DotNetEnv и capability `config/env-file` до старта `restructure-ddd-solution`, чтобы фаза 1 не перевозила doomed-код.

Non-Goals:
- UI ввода и шифрования секретов — секрет по-прежнему read-only и правится только в файле.
- Потребители секций `Telegram`/`Llm` — фаза 3 (change `add-summary-channels`); этот change только фиксирует файл и секции как контракт (`config/local-secrets`).
- Изменение порта `IBybitCredentialsProvider` и доменной части — не требуется.
- Hot-reload секретов без перезапуска — перезапуск локального приложения тривиален.

## Decisions

### D1. Конфигурационная реализация поставщика вместо переменных окружения
Новый `ConfigurationBybitCredentialsProvider : IBybitCredentialsProvider` читает `Bybit:ApiKey` и `Bybit:ApiSecret` из `IConfiguration`; имена ключей — публичные константы (используются сообщениями об ошибках и экраном «Настроек»). Отсутствие или пустота значения — `InvalidOperationException` с текстом, называющим файл и ключ (текущий контракт поставщика сохранён). Альтернатива — оставить env-реализацию и маппить переменные в конфигурацию — отклонена: она и есть второй механизм, который убираем.

### D2. Подключение local-оверрайда в Program.cs
`builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false)` сразу после `WebApplication.CreateBuilder`: относительный путь резолвится против ContentRoot (в dev — каталог проекта рядом с `appsettings.json`, у опубликованного exe — каталог приложения), а провайдер, добавленный последним, получает высший приоритет. `optional: true` — отсутствие файла штатно (сценарий `scenario-local-secrets-missing-file-normal`). `reloadOnChange: false` — секреты меняются редко, перезапуск тривиален. Вызов `AppEnvFile.Load()` и сам класс удаляются.

### D3. Приоритет: local-файл > переменные окружения > appsettings.json
Стандартный порядок `CreateBuilder` (appsettings.json → appsettings.{Env}.json → user-secrets в dev → env vars → command line) плюс наш файл последним. Системные переменные `Bybit__ApiKey`/`Bybit__ApiSecret` (решение [#4](https://github.com/menshov-anatoliy/transaction-journal/issues/4)) остаются рабочим резервом: env-провайдер конфигурации читает их напрямую, но local-файл их перекрывает (сценарий `scenario-local-secrets-override-system-vars`). Побочный эффект: local-файл перекрывает и аргументы командной строки — для локального настольного приложения приемлемо.

### D4. Полное удаление механизма .env
Удаляются `AppEnvFile.cs`, `AppEnvFileTests.cs`, ссылка на пакет DotNetEnv в csproj и `.env.example`. Строка `.env` в `.gitignore` сохраняется: защищает от случайного коммита осиротевшего файла с реальными значениями, пока владелец его не удалит.

### D5. Тексты экрана «Настроек»
`SettingsReadModel` формирует место хранения секрета из констант новых конфигурационных ключей (`appsettings.Local.json, секция Bybit`); блок «Ключ не настроен» в `Settings.razor` указывает заполнить `Bybit:ApiKey`/`Bybit:ApiSecret` в `appsettings.Local.json`. Маскирование и запрет ввода секрета через UI не меняются (`scenario-settings-secret-never-displayed`).

### D6. Шаблон appsettings.Local.json.example
Коммитится рядом с `appsettings.json` (`src/TransactionJournal/`), содержит секции `Bybit`, `Telegram`, `Llm` с заглушками и русскими комментариями о назначении и приоритете. Реальный файл у владельца уже существует ([#35](https://github.com/menshov-anatoliy/transaction-journal/issues/35)) — он лишь дополняет его секцией `Bybit` вручную: значения секрета переносит владелец, не агент.

## Risks / Trade-offs

- [Владелец не перенесёт ключи до первого запуска синхронизации] → Понятная ошибка конфигурации с именем файла и ключа; экран «Настроек» показывает «Ключ не настроен» с указанием, что заполнить.
- [Local-файл перекрывает аргументы командной строки] → Задокументировано в D3; CLI-override конфигурации в приложении не используется.
- [Осиротевший `.env` с реальным секретом остаётся на диске] → Миграционный шаг владельца: удалить `.env` после переноса значений; `.gitignore` продолжает его прикрывать.
- [Тесты поставщика мутируют процессное окружение] → Уходят вместе с env-реализацией: тесты новой реализации строят in-memory `IConfiguration`, изоляции проще.

## Migration Plan

1. Релиз change: код, тесты, шаблон `appsettings.Local.json.example`, `openspec validate --strict`.
2. Владелец дополняет свой `appsettings.Local.json` секцией `Bybit` (значения из `.env`), удаляет `.env`.
3. Smoke: экран «Настроек» показывает маску ключа; ручная синхронизация проходит.
4. Откат: revert коммита — env-механизм возвращается, если `.env` сохранён.
