# Spec Delta

## ADDED Requirements

### Requirement: Подсказки несут trace id прохода

Каждая созданная запись подсказки SHALL сохранять nullable trace id прохода — W3C TraceId (32 hex) корневого спана `hint_pass`; все записи одного прохода SHALL нести один id ([#83](https://github.com/menshov-anatoliy/transaction-journal/issues/83)). Гашение записи в `expired` SHALL не получать trace id — гашение не создание; записи до внедрения SHALL оставаться null. Колонка `hints.trace_id` SHALL быть метаданными отладки: ни одно доменное правило её не читает. Отдельной таблицы проходов SHALL не быть — журналом проходов остаётся файл трасс (спаны `hint_pass` несут исход и счётчики). `POST /api/hints/pass` SHALL возвращать trace id прохода.

Traceability ID: requirement-engine-trace-id-on-created-hints

#### Scenario: Все записи прохода несут один id

Traceability ID: scenario-engine-pass-single-trace-id

- **WHEN** проход создаёт несколько подсказок
- **THEN** все созданные записи несут один и тот же W3C TraceId корневого спана `hint_pass` этого прохода

#### Scenario: Гашение не получает trace id

Traceability ID: scenario-engine-expiry-no-trace-id

- **WHEN** живая запись гасится в `expired` (ушедшее условие, выведенное правило, закрытый субъект)
- **THEN** trace id записи не заполняется

#### Scenario: Ручной проход возвращает trace id

Traceability ID: scenario-engine-pass-response-trace-id

- **WHEN** выполняется `POST /api/hints/pass`
- **THEN** ответ несёт trace id прохода как опциональное поле (null у проходов без трассы)

#### Scenario: Записи до внедрения — null

Traceability ID: scenario-engine-hints-before-adoption-null

- **WHEN** читается подсказка, созданная до внедрения трассировки
- **THEN** её trace id остаётся null
