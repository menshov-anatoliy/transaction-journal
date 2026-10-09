# Parity checklist 7.1 — карта переноса №1–29

Паритет-аудит фиксирует итог переноса по карте §12: каждая функция помечена как `covered`, `intentionally dropped` или `gap`.
Traceability: doc:.wf-research/ui-concept/concept.md#12-карта-переноса-по-инвентаризации-129

## Матрица статусов

| № | Статус | Где реализовано (файл/маршрут) | Комментарий |
|---|---|---|---|
| 1 | covered | `frontend/src/pages/constructions-page.tsx` (`/`) | Шапка итога + таблица конструкций. |
| 2 | covered | `frontend/src/lib/format/degradation.ts`, `frontend/src/pages/constructions-page.tsx` | Сквозной паттерн деградации котировок. |
| 3 | covered | `frontend/src/components/constructions/card/metric-strip.tsx`, `frontend/src/components/constructions/construction-preview.tsx` | kstrip в карточке + краткая метрика в превью. |
| 4 | covered | `frontend/src/components/constructions/card/{positions-table,trades-table,closing-entries-table,adjustments-table}.tsx` | Все 4 таблицы карточки. |
| 5 | covered | `frontend/src/pages/construction-card-page.tsx`, `frontend/src/components/constructions/card/use-card-commands.ts` | Действия конструкции в шапке карточки. |
| 6 | covered | `frontend/src/components/markdown/{markdown-viewer,markdown-edit-dialog}.tsx`, `frontend/src/pages/construction-card-page.tsx` | MD-рендер + split-редактор Ctrl+Enter. |
| 7 | covered | `frontend/src/components/constructions/card/{positions-table,closing-entries-table}.tsx` | Ручные пометки закрытия, правка и удаление. |
| 8 | covered | `frontend/src/components/constructions/card/trades-table.tsx`, `frontend/src/components/constructions/card/use-card-commands.ts` | Возврат сделки во «Входящие» и перенос. |
| 9 | covered | `frontend/src/components/constructions/card/adjustments-table.tsx` | Корректировки PnL карточки. |
| 10 | covered | `frontend/src/pages/inbox-page.tsx` (`/inbox`) | Фильтры, выбор, привязка, создание конструкции. |
| 11 | covered | `frontend/src/pages/sync-settings-page.tsx` (`/sync-settings`), `frontend/src/pages/constructions-page.tsx` | Ручная синхронизация в разделе и кнопка в шапке «Конструкций». |
| 12 | covered | `frontend/src/pages/inbox-page.tsx`, `frontend/src/pages/sync-settings-page.tsx` | Сборка из «Входящих» + полный пересбор в опасной зоне. |
| 13 | covered | `frontend/src/pages/constructions-page.tsx`, `frontend/src/pages/construction-card-page.tsx` | Панели подсказок журнала и конструкции. |
| 14 | covered | `frontend/src/pages/constructions-page.tsx` | Ручной запуск прохода подсказок в правой области «Конструкций». |
| 15 | covered | `frontend/src/pages/hints-page.tsx` (`/hints`) | Журнал всех подсказок read-only. |
| 16 | covered | `frontend/src/pages/agent-page.tsx`, `frontend/src/components/constructions/card/construction-chats-panel.tsx` | Чаты агента в разделе и в правой области карточки. |
| 17 | covered | `frontend/src/pages/constructions-page.tsx`, `frontend/src/components/layout/AppLayout.tsx`, `frontend/src/lib/format/display-time.ts` | Итог/сигнал/локальное время + бейдж «Входящих». |
| 18 | covered | `frontend/src/components/constructions/constructions-table.tsx` | Бейдж живых подсказок в строке таблицы. |
| 19 | covered | `frontend/src/components/finresult/fin-result-indicator.tsx`, `frontend/src/components/constructions/card/metric-strip.tsx`, `frontend/src/components/constructions/construction-preview.tsx` | Compact/средний/полный индикатор финрезультата. |
| 20 | covered | `frontend/src/pages/sync-settings-page.tsx` | Журнал синхронизаций и переключатель бэкапа. |
| 21 | covered | `frontend/src/pages/sync-settings-page.tsx` | Сброс состояния категории в опасной зоне. |
| 22 | covered | `frontend/src/pages/sync-settings-page.tsx` | Блок подключения Bybit с маской ключа. |
| 23 | intentionally dropped | Решение #54, карта §12 | Транзитная вкладка отпадает: карточка-маршрут + открытие в новом окне. |
| 24 | covered | `frontend/src/pages/sync-settings-page.tsx` | Предупреждения сверки и заметки запусков. |
| 25 | covered | `frontend/src/lib/format/{plural,quantity}.ts` | Русские формы числительных и форматы величин. |
| 26 | intentionally dropped | Решение #54, карта §12 | `/sync`-дубль отпадает, всё консолидировано в `/sync-settings`. |
| 27 | covered | `frontend/src/pages/error-page.tsx`, `frontend/src/router/index.tsx` (`/Error`) | Русифицированная страница ошибки SPA. |
| 28 | covered | `frontend/src/pages/construction-card-page.tsx`, `frontend/src/components/constructions/card/use-card-commands.ts` | Возврат конструкции из архива через действие шапки. |
| 29 | covered | `frontend/src/pages/sync-settings-page.tsx` | Текстовая подсказка про `appsettings` при незастроенном ключе. |

## Итог по gap

- Найден и закрыт один `gap`: №27 (`/Error`).
- В исходном аудите незакрытых UI-`gap` после исправления №27 не зафиксировано. Это не подтверждает готовность настоящего ИИ-конвейера: ограничение №16 уточнено ниже.

## Ограничение №16 — 2026-10-09

Статус `covered` для чатов означает наличие UI и текущего HTTP-контракта, а не полноценную генерацию ИИ-ответов. `AgentEndpoints` пока возвращает детерминированное резюме. По решению владельца задача 5.2 остаётся открытой до подключения конвейера change `add-agent-chat`; пост-мортем закрытой конструкции тем же конвейером также не считается принятым.

## Отдельные решения карты

- №23 и №26 зафиксированы как `intentionally dropped` по решению #54.
- Экранное редактирование/просмотр настроек LLM **не выводится в UI** (решение раунда 1 карты переноса).
