# Proposal

## Why

Журнал умеет принимать историю (change `add-bybit-sync`, тикет [#8](https://github.com/menshov-anatoliy/transaction-journal/issues/8)) и владеет доменом конструкций, позиций и корректировок (change `add-core-domain`, тикет [#9](https://github.com/menshov-anatoliy/transaction-journal/issues/9)), но не отвечает на главный вопрос — каков результат конструкции. Семантика результата зафиксирована решениями карты ([#1](https://github.com/menshov-anatoliy/transaction-journal/issues/1)) и гриллингом edge-cases ([#5](https://github.com/menshov-anatoliy/transaction-journal/issues/5)): собственный расчёт FIFO с комиссиями, нереализованная часть по маркам на момент запроса, проценты от выделенного капитала; контракт MCP ([#7](https://github.com/menshov-anatoliy/transaction-journal/issues/7)) уже обещает `get_analytics` с `unrealized_pnl` и `marks_as_of`. Этот change переносит её в формальную спеку.

## What Changes

- Реализованный PnL: собственный расчёт FIFO по сделкам и закрывающим записям (delivery/OTM/ручная пометка, ADR-0002), комиссии из записей исполнения, USDC по паритету 1:1 (ADR-0001), funding вне результата, биржевой `deliveryRpl` — только предупреждающая сверка.
- Нереализованный PnL: оценка открытых остатков по маркам публичных тикеров на момент запроса, отметка времени марок, при сбое — null только у нереализованной части.
- Провайдер марок: публичный эндпоинт тикеров без аутентификации, кэш последней известной марки со штампом времени; источник дефолта цены ручной пометки закрытия (контракт с `add-core-domain` D3).
- Метрики позиции: чистый остаток, реализованный PnL, накопленные комиссии, нереализованный PnL, даты открытия/закрытия и длительность.
- Метрики конструкции: итог как сумма позиций и внешних корректировок PnL, проценты от текущего выделенного капитала, даты и длительность; длительность открытой — до текущего момента.
- Всё вычисляется при чтении: хранимых результатов нет, промежуточный кэш прозрачен.

## Capabilities

### New Capabilities

- `analytics/performance`: аналитика результата — расчёт PnL, провайдер марок, метрики позиции и конструкции, проценты и временные характеристики.

### Modified Capabilities

<!-- Изменяемых capability нет: спеки проекта пусты; границы с sync и domain проходят по «Входящим», ключу execId и PositionReadModel. -->

## Impact

- Новый читающий слой поверх `PositionReadModel` домена и материализованных записей sync: движок FIFO, вычислитель метрик, провайдер марок.
- HTTP-клиент публичных тикеров Bybit без SDK (решение research-тикета [#2](https://github.com/menshov-anatoliy/transaction-journal/issues/2)); кэш марок — единственное новое хранилище.
- Потребители: UI-экраны списка и деталей конструкции (тикет [#12](https://github.com/menshov-anatoliy/transaction-journal/issues/12)) и MCP-инструмент `get_analytics` (тикет [#11](https://github.com/menshov-anatoliy/transaction-journal/issues/11)).
- Опирается на CONTEXT.md и ADR-0001/ADR-0002; изменений глоссария не требует.
