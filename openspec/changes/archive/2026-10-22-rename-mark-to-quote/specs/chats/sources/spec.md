# Spec Delta

## MODIFIED Requirements

### Requirement: Недоступность рынка деградирует в кэш с явным as-of
Traceability ID: requirement-sources-degradation-cached-asof

При недоступности Bybit рыночный инструмент SHALL возвращать структурированный ответ «недоступно + последняя кэшированная проекция с явным as-of» из кэша котировок; ИИ-помощник SHALL продолжать отвечать по журналу и корпусу, помечать устаревший as-of и отказываться от рыночно-зависимых рекомендаций ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53), [ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

#### Scenario: Bybit недоступен — инструмент отдаёт кэш
Traceability ID: scenario-sources-market-down-cached-projection
- **WHEN** биржа недоступна в момент вызова рыночного инструмента
- **THEN** инструмент возвращает «недоступно» и последнюю кэшированную проекцию с её as-of

#### Scenario: Ответ без рыночно-зависимых рекомендаций
Traceability ID: scenario-sources-stale-asof-no-market-advice
- **WHEN** доступные рыночные данные устарели
- **THEN** ИИ-помощник помечает их as-of и строит ответ на журнале и корпусе
- **AND** рыночно-зависимые рекомендации не выдаются
