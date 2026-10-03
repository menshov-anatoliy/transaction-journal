# Proposal

## Why

Слои журнала сегодня — папки одного проекта `src/TransactionJournal`: граница между доменом, оркестрацией, инфраструктурой и UI держится только дисциплиной ревью, компилятор её не проверяет. Перед построением агента подсказок ([ADR-0007](../../../docs/adr/0007-hints-agent-architecture.md)) нужна целевая структура, на которой движок строится сразу без перекладки (карта [#19](https://github.com/menshov-anatoliy/transaction-journal/issues/19), решения [#29](https://github.com/menshov-anatoliy/transaction-journal/issues/29) и [#31](https://github.com/menshov-anatoliy/transaction-journal/issues/31)).

## What Changes

- Решение перестраивается в пять проектов с DDD-слоями ([ADR-0008](../../../docs/adr/0008-ddd-solution-structure.md)): `TransactionJournal.Domain`, `TransactionJournal.Application`, `TransactionJournal.Infrastructure`, `TransactionJournal` (Web) и — позже, отдельным change — `TransactionJournal.Hints`.
- Зависимости направлены внутрь: Web → Application → Domain; Infrastructure реализует порты Application и Domain; Hints зависит только от Domain.
- Namespace приводятся к проектам (root namespace = проект, подпространства сохраняются, например `TransactionJournal.Application.Sync`); тесты остаются одним проектом `tests/TransactionJournal.Tests` с папками, зеркалящими проекты.
- Миграция идёт поэтапно, серией шагов Domain → Infrastructure → Application → Web; после каждого шага решение собирается, тесты зелёные без правки ассертов.
- Функционального изменения нет: имя exe, App_Data, режим `--mcp` и всё поведение приложения сохраняются ([ADR-0003](../../../docs/adr/0003-stack-single-exe-dotnet-sqlite.md)); находки поведения по ходу миграции фиксируются отдельными change.

## Capabilities

### New Capabilities

- `architecture/solution-structure`: инварианты целевой структуры решения — состав проектов, раскладка слоёв, направление зависимостей, namespace, зеркальные тесты и неизменность запуска (`--mcp`, имя exe). Проживает после миграции как контракт для всех последующих change.

### Modified Capabilities

_None_

## Impact

- `src/TransactionJournal.sln` (или эквивалент): четыре проекта вместо одного, переносы файлов почти всего дерева `src/TransactionJournal/**`.
- Переносу подлежат: `Domain` (+ чистые движки и парсеры из `Materialization`/`Sync`), EF-код `Data` → Infrastructure, `Bybit` → Infrastructure, `Ops` → Infrastructure, `Materialization`/`Sync`/`Analytics` как оркестрация → Application, сервисы и read-модели `Components/Pages` → Application, `Components` и `Program.cs` → Web.
- `tests/TransactionJournal.Tests`: перенос файлов в зеркальные папки, обновление namespace в `using`.
- Инфраструктура сборки: пути в скриптах/CI, если они ссылаются на старые пути проекта.
- Внешние контракты (SQLite-схема, `--mcp`, UI) не изменяются.
