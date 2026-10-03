# local-secrets Specification

## Purpose

Локальные секреты приложения (учётные данные Bybit, токен Telegram-бота, ключ LLM-провайдера) хранятся в едином файле `appsettings.Local.json` рядом с приложением — файл-оверрайд конфигурации с высшим приоритетом, не попадающий в систему контроля версий.

## Requirements

### Requirement: Секреты читаются из единого локального файла
Приложение SHALL подключать файл `appsettings.Local.json` из каталога приложения как необязательный оверрайд конфигурации с приоритетом над `appsettings.json` и переменными окружения. Все локальные секреты SHALL читаться из этого файла по своим секциям конфигурации: учётные данные Bybit — `Bybit:ApiKey` и `Bybit:ApiSecret`, токен Telegram-бота и chat id — секция `Telegram`, ключ LLM-провайдера — секция `Llm` ([#35](https://github.com/menshov-anatoliy/transaction-journal/issues/35)). Отсутствие файла SHALL быть штатной ситуацией: приложение запускается и работает, а отсутствие конкретного секрета обнаруживает только потребитель при обращении к нему.

Traceability ID: requirement-local-secrets-single-file

#### Scenario: Секреты Bybit из local-файла доезжают до потребителя
Traceability ID: scenario-local-secrets-reach-consumers
- **WHEN** `appsettings.Local.json` содержит `Bybit:ApiKey` и `Bybit:ApiSecret`, а системные переменные окружения не заданы
- **THEN** поставщик учётных данных Bybit получает ключ и секрет из файла
- **AND** синхронизация работает без предварительной настройки системных переменных

#### Scenario: Запуск без local-файла не является ошибкой
Traceability ID: scenario-local-secrets-missing-file-normal
- **WHEN** файла `appsettings.Local.json` нет в каталоге приложения
- **THEN** приложение запускается без сообщений об ошибке, связанных с отсутствием файла
- **AND** потребитель секрета сообщает о незастроенном ключе только в момент обращения к нему

#### Scenario: Local-файл главнее системных переменных
Traceability ID: scenario-local-secrets-override-system-vars
- **WHEN** значение задано и в `appsettings.Local.json`, и в системной переменной окружения вида `Bybit__ApiKey`
- **THEN** применяется значение из `appsettings.Local.json`

### Requirement: Файл секретов остаётся вне репозитория
Репозиторий SHALL содержать шаблон `appsettings.Local.json.example` с секциями `Bybit`, `Telegram` и `Llm` и заглушками вместо реальных значений. Файл `appsettings.Local.json` с реальными значениями SHALL игнорироваться системой контроля версий и не попадать в репозиторий.

Traceability ID: requirement-local-secrets-stay-out-of-repo

#### Scenario: Разработчик создаёт local-файл из шаблона
Traceability ID: scenario-local-secrets-template-copied
- **WHEN** разработчик копирует `appsettings.Local.json.example` в `appsettings.Local.json` и вписывает реальные значения
- **THEN** система контроля версий не отслеживает `appsettings.Local.json`
- **AND** шаблон с заглушками остаётся в репозитории
