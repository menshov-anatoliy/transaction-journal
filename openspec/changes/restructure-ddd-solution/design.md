# Design

## Context

Сегодня весь код — один проект `src/TransactionJournal` со слоями-папками: `Domain`, `Data` (EF), `Sync`, `Materialization`, `Analytics`, `Bybit`, `Components`, `Ops`; тесты — отдельный проект `tests/TransactionJournal.Tests` с теми же папками-слоями. Целевая структура и порядок шагов утверждены на карте: [#29](https://github.com/menshov-anatoliy/transaction-journal/issues/29) (состав проектов и раскладка), [#31](https://github.com/menshov-anatoliy/transaction-journal/issues/31) (порядок фаз, отклонение интерлива и подсказок на монолите); ADR-0008 фиксирует её как архитектурное решение, ADR-0003 ограничивает снизу: имя exe, `App_Data` и режим `--mcp` неприкосновенны.

## Goals / Non-Goals

**Goals:**

- Пять проектов решения с границами, проверяемыми компилятором; зависимости внутрь.
- Механичная миграция: серия шагов Domain → Infrastructure → Application → Web, после каждого — сборка и зелёные тесты без правки ассертов.
- Namespace по проектам с сохранением подпространств; тесты — один проект с зеркальными папками.

**Non-Goals:**

- Любое изменение поведения, контрактов SQLite-схемы, UI или `--mcp`: находки оформляются отдельными change.
- Проект `TransactionJournal.Hints` и код агента подсказок ([ADR-0007](../../../docs/adr/0007-hints-agent-architecture.md)) — следующие change, строятся на готовой структуре.
- MCP-сервер из ADR-0003 и read-only контракт R3 — их порты лишь размещаются в Application по мере переноса соответствующего кода; сами функции не разрабатываются.
- Refactoring тел методов: переносятся как есть.

## Decisions

### D1. Раскладка по принципу «логика / оркестрация / адаптеры / UI»

Перечень переносов фиксирован решением [#29](https://github.com/menshov-anatoliy/transaction-journal/issues/29):

| Откуда (папка сегодня) | Куда (проект) | Что именно |
|---|---|---|
| `Domain` + чистые движки `Materialization`/`Sync` | Domain | POCO-сущности, ConstructionAssembler, ConstructionNameBuilder, PositionFifoEngine, OptionSymbolParser, LinearSymbolParser, store-порты (IJournalRawSnapshotStore, IInstrumentReferenceStore, probe-интерфейсы) |
| `Data` + `Bybit` + `Ops` | Infrastructure | JournalDbContext, fluent-конфигурации, миграции, Bybit-клиент, бэкапы |
| `Materialization`/`Sync`/`Analytics` (оркестрация), сервисы и read-модели страниц | Application | ConstructionService, TradeBindingService, CommentService, PnLAdjustmentService, ManualCloseMarkService, Inbox/PositionReadModel, шлюзы-порты IBybitHistoryGateway, IFreshInstrumentMarkSource, read-only контракт чтения |
| `Components`, `Program.cs`, `Properties`, `wwwroot` | Web (TransactionJournal) | Blazor-компоненты, composition root; имя exe и `App_Data` без изменений |

Альтернатива «переносить по фичам, а не по слоям» отклонена: фича разрезается между слоями, шаг не собирался бы после каждого переноса.

### D2. Порядок шагов — по направлению зависимостей

Domain → Infrastructure → Application → Web. Domain собирается без зависимостей; Infrastructure ложится на готовые порты Domain; Application — на оба; Web последним получает composition root над готовыми слоями. Каждый шаг заканчивается закоммиченным зелёным состоянием; шаг можно прервать и продолжить без полусобранных веток. Отклонённые альтернативы — интерлив с разработкой Hints (два незакрытых фронта) и один большой PR (нет контрольных точек) — зафиксированы в [#31](https://github.com/menshov-anatoliy/transaction-journal/issues/31).

### D3. Порты объявляются там, где живут их потребители

Store-порты домена (снапшот журнала, справочник инструментов) — в Domain; шлюзы внешних систем (IBybitHistoryGateway, IFreshInstrumentMarkSource) и read-only контракт чтения для `--mcp`/R3 — в Application. Infrastructure реализует всё и регистрируется в composition root Web. Так ADR-0003 подтверждается без изменений: `--mcp` — раннее ветвление того же exe, контракт — порты Application.

### D4. Namespace = проект, подпространства сохраняются

Root namespace равен имени проекта; внутренние подпространства не схлопываются: `TransactionJournal.Application.Sync`, `TransactionJournal.Infrastructure.Bybit` и т. п. Это сохраняет читаемость происхождения кода и делает диффы переноса механическими (замена префикса namespace и `using`).

### D5. Тесты остаются одним проектом

`tests/TransactionJournal.Tests` не дробится: персональный инструмент, один runner, папки зеркалят проекты (`Domain/`, `Application/`, `Infrastructure/`, `Web/`). Ассерты не правятся; при переносе теста меняются только namespace. Дробление тестов по проектам отклонено как рост конфигурации без выгоды для одного пользователя.

### D6. Решение и целевые фреймворки

Все проекты — `net9.0`; Web остаётся единственным исполняемым (`OutputType Exe`), остальные — библиотеки. Solution-файл обновляется на первом шаге и дополняется на каждом следующем.

## Risks / Trade-offs

- [Широта переноса: один шаг затрагивает десятки файлов] → Механичность: `git mv` + замена namespace + сборка + полный тестовый прогон; никакой правки логики внутри шага.
- [Скрытые связки слоёв всплывут при переносе (например, доменный тип с EF-зависимостью)] → Разрыв связи — единственная допустимая правка вне механики: тип уезжает в слой своей зависимости либо зависимость инвертируется портом; правка фиксируется в tasks шага отдельным пунктом с обоснованием.
- [Слом внешних скриптов/CI, ссылающихся на старые пути проекта] → Проверка путей сборки в конце каждого шага; имя exe не меняется, поэтому артефакт развёртывания один и тот же.
- [Дрейг между шагами (solve долгий, база уходит вперёд)] → Шаги короткие и последовательные; между шагами в main не принимаются change, трогающие переносимые папки.

## Migration Plan

Четыре последовательных шага (каждый — отдельный коммит/PR, зелёный на выходе):

1. **Domain**: создать проекты и solution-структуру; перенести POCO-сущности, движки, парсеры, store-порты; тесты — в зеркальную папку `Domain`.
2. **Infrastructure**: перенести EF (DbContext, конфигурации, миграции), Bybit-клиент, Ops; реализовать store-порты Domain.
3. **Application**: перенести оркестрацию Materialization/Sync/Analytics, use-case-сервисы, read-модели, шлюзы-порты, read-only контракт.
4. **Web**: перенести Components, `Program.cs`, composition root; финальная сверка: имя exe, `App_Data`, `--mcp`, полный тестовый прогон.

Откат — возврат к точке последнего зелёного шага; структура совместима построечно (каждый шаг не ломает предыдущее состояние solution).
