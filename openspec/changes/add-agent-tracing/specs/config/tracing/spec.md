# Spec Delta

## ADDED Requirements

### Requirement: Секция Tracing задаёт журнал трасс без правки кода

Параметры журнала трасс SHALL задаваться секцией `Tracing`: `Detail` (`basic|detailed`, дефолт `detailed`), `Dir` (каталог трасс, дефолт `App_Data/traces`), `MaxFileBytes` (предел размера файла до ротации, дефолт 10 МБ), `KeepFiles` (число хранимых архивов, дефолт 5) ([#82](https://github.com/menshov-anatoliy/transaction-journal/issues/82)). Отсутствие секции SHALL давать дефолты. Смена параметров SHALL выполняться правкой конфигурации без правки кода. Секция SHALL также нести настройку приёмника OTLP (dev-опция Phoenix): не задана — экспорт выключен.

Traceability ID: requirement-tracing-config-section

#### Scenario: Дефолты без секции

Traceability ID: scenario-tracing-config-defaults

- **WHEN** секция `Tracing` в конфигурации отсутствует или пуста
- **THEN** трассы пишутся в `App_Data/traces/traces.jsonl` на уровне `detailed`, ротация при 10 МБ, хранится 5 архивов, экспорт OTLP выключен

#### Scenario: Смена уровня и лимитов без кода

Traceability ID: scenario-tracing-config-switch-without-code

- **WHEN** конфигурация задаёт `Tracing:Detail=basic`, другой каталог или лимиты ротации
- **THEN** журнал трасс работает с указанными параметрами без правки кода
