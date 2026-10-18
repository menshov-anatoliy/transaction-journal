# Tasks

## 1. Вычислитель диагностики

- [x] 1.1 `ConstructionRiskDiagnosticsCalculator` в `Application/Analytics`: вход — позиции с ненулевым остатком; парсинг символов (`OptionSymbolParser`/`LinearSymbolParser`), группировка по базовому активу (паритет USDT/USDC), наклоны на хвостах, приоритет покрытия фьючерсов, записи «инструмент + число контрактов + причина». Проверить: `requirement-risk-diagnostics-coverage-by-underlying`, `requirement-risk-diagnostics-slope-attribution`.
- [x] 1.2 Юнит-тесты матрицы сценариев: голый шорт/лонг-фьючерс, закрытые 1:1, частичное покрытие «1 из 2», короткие опционы сверх покрытия, короткий пут без покрытия, шорт-фьючерс закрывает шорт-пут, covered call, голый длинный опцион без предупреждения. Проверить: `scenario-uncovered-short-perp-unbounded`, `scenario-uncovered-long-perp-bounded`, `scenario-covered-legs-no-warning`, `scenario-partial-coverage-counts-contracts`, `scenario-short-options-beyond-futures`, `scenario-uncovered-short-put`, `scenario-short-futures-covers-short-put`, `scenario-covered-call-warns-futures-only`, `scenario-naked-long-option-no-warning`.
- [x] 1.3 Тексты записей на русском с причиной и числом контрактов; обработка неразборчивого символа отдельным предупреждением без маскировки остальных. Проверить: `requirement-risk-diagnostics-messages`, `requirement-risk-diagnostics-unparseable-symbol`.

## 2. Метрики и контракты API

- [ ] 2.1 Вызов вычислителя из `ConstructionMetricsCalculator`; счётчик предупреждений в `ConstructionMetrics` и `ConstructionListItem` (список и превью), коллекция записей в `ConstructionDetailData`; без записей в БД. Проверить: `requirement-risk-diagnostics-on-open-residuals` (пустой набор без остатков, пересчёт при чтении).
- [ ] 2.2 API-тесты: счётчик в ответе списка и превью, записи в ответе карточки, неизменность существующих полей (включая `realRiskStatus`). Проверить: `scenario-diagnostics-closed-construction-empty`, `scenario-diagnostics-recomputed-on-read`.

## 3. Макет design.pen — до реализации frontend

- [ ] 3.1 Через MCP Pen внести в design.pen бейдж-счётчик «⚠ N» в строку экрана «Конструкции» (паттерн бейджа живых подсказок, тон `--risk`), включая состояние без предупреждений (бейдж скрыт). Проверить: макет содержит бейдж в обоих состояниях строки.
- [ ] 3.2 Через MCP Pen внести в design.pen полный перечень предупреждений в краткую карточку контекстной панели и секцию «Предупреждения» над таблицами карточки конструкции (паттерн секции закрывающих записей), включая скрытие секции при пустом наборе. Проверить: макет содержит обе точки отображения в обоих состояниях.

## 4. Интерфейс — перенос из макета

- [ ] 4.1 Бейдж «⚠ N» в строке таблицы конструкций по обновлённому макету (образец — бейдж живых подсказок); скрыт при нуле; доступное имя (`title`/`aria-label`) с числом предупреждений. Проверить: `scenario-list-row-warning-badge`, `scenario-list-row-no-warning-badge`.
- [ ] 4.2 Полный перечень предупреждений в краткой карточке контекстной панели по макету. Проверить: `scenario-preview-lists-all-warnings`.
- [ ] 4.3 Секция «Предупреждения» в основной карточке над таблицами записей по макету, скрытая при пустом наборе; бейдж «∞» индикатора не затрагивается. Проверить: `scenario-detail-warnings-section-above-tables`, `scenario-detail-warnings-section-hidden`, `scenario-unbounded-badge-coexists-with-warnings`.

## 5. Сверка и приёмка

- [ ] 5.1 Скриншот-сверка экранов «Конструкции» и карточки с обновлённым макетом design.pen через MCP Pen: токенно-компонентное соответствие (состав/структура компонентов, токены, типографика, отступы, радиусы).
- [ ] 5.2 Прогон `openspec validate add-construction-risk-diagnostics --strict`; сквозная проверка на живом журнале: бейдж, краткая и основная карточки показывают один и тот же набор предупреждений.
