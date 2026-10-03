# Spec Delta

## REMOVED Requirements

### Requirement: Приложение загружает .env при запуске
**Reason**: Механизм `.env` заменён единым локальным файлом `appsettings.Local.json` (change `consolidate-secrets-local-json`, решение [#35](https://github.com/menshov-anatoliy/transaction-journal/issues/35)); поддержка двух магазинов секретов у одного локального приложения не оправдана.
**Migration**: Значения `BYBIT_API_KEY`/`BYBIT_API_SECRET` переносятся в секцию `Bybit` файла `appsettings.Local.json` (создаётся из шаблона `appsettings.Local.json.example`); файл `.env` удаляется. Альтернатива — системные переменные `Bybit__ApiKey`/`Bybit__ApiSecret`, которые читает env-провайдер конфигурации напрямую.

### Requirement: Системные переменные имеют приоритет над .env
**Reason**: Правило приоритета принадлежит механизму `.env`, который выводится из эксплуатации; приоритеты нового механизма определяет capability `config/local-secrets` (local-файл главнее системных переменных).
**Migration**: Приоритет задаётся требованием `requirement-local-secrets-single-file` capability `config/local-secrets`.

### Requirement: Секреты остаются вне репозитория
**Reason**: Требование о шаблоне и игнорировании перенесено в capability `config/local-secrets` с шаблоном `appsettings.Local.json.example` вместо `.env.example`.
**Migration**: Роль шаблона выполняет `appsettings.Local.json.example` (требование `requirement-local-secrets-stay-out-of-repo`); `.env.example` удаляется из репозитория.
