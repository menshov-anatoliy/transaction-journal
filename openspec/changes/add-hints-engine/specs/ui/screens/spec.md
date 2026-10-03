# Spec Delta

## ADDED Requirements

### Requirement: Индикатор живых подсказок у конструкции в списке

Список конструкций SHALL показывать у каждой конструкции индикатор — число живых подсказок (статусы `new`, включая непросмотренные) этой конструкции; у конструкций без живых подсказок индикатор отсутствует ([#30](https://github.com/menshov-anatoliy/transaction-journal/issues/30)).

Traceability ID: requirement-ui-construction-hint-badge

#### Scenario: Конструкция с живыми подсказками несёт индикатор
Traceability ID: scenario-ui-badge-shows-live-count
- **WHEN** у конструкции есть живые подсказки
- **THEN** в строке списка конструкций показывается их число

#### Scenario: Без живых подсказок индикатора нет
Traceability ID: scenario-ui-badge-hidden-when-none
- **WHEN** у конструкции нет живых подсказок
- **THEN** индикатор не отображается

### Requirement: Панель «Подсказки» в деталях конструкции

Экран деталей конструкции SHALL содержать панель «Подсказки»: живые подсказки сверху, терминальная история свёрнута, сортировка по времени генерации. Карточка подсказки SHALL показывать полный состав: текст, характер, чёткость, rule id, теги источников с цитатами, факты триггера, as-of и статус. Кнопки «Применено» и «Отклонено» на живой подсказке SHALL быть единственными мутациями подсказок в UI ([#30](https://github.com/menshov-anatoliy/transaction-journal/issues/30)).

Traceability ID: requirement-ui-hint-panel-in-construction

#### Scenario: Живые подсказки наверху панели
Traceability ID: scenario-ui-panel-live-first
- **WHEN** панель «Подсказки» открыта у конструкции с живыми и терминальными записями
- **THEN** живые показаны сверху, терминальная история свёрнута
- **AND** сортировка внутри — по времени генерации

#### Scenario: Карточка показывает полный состав подсказки
Traceability ID: scenario-ui-hint-card-full-composition
- **WHEN** подсказка раскрыта в панели
- **THEN** видны текст, характер, чёткость, rule id, теги источников с цитатами, факты триггера, as-of и статус

#### Scenario: Кнопки «Применено»/«Отклонено» завершают жизненный цикл
Traceability ID: scenario-ui-apply-dismiss-buttons
- **WHEN** владелец нажимает «Применено» или «Отклонено» на живой подсказке
- **THEN** запись переходит в соответствующий терминальный статус
- **AND** других мутаций подсказок UI не предоставляет

### Requirement: Портфельные подсказки — панель на обзорном экране

Обзорный экран журнала SHALL показывать ту же панель «Подсказки» для подсказок субъекта «журнал» с теми же правилами состава и кнопками ([#30](https://github.com/menshov-anatoliy/transaction-journal/issues/30)).

Traceability ID: requirement-ui-portfolio-hint-panel

#### Scenario: Подсказки журнала видны на обзорном экране
Traceability ID: scenario-ui-portfolio-panel-shows-journal-hints
- **WHEN** есть живые подсказки субъекта «журнал»
- **THEN** обзорный экран показывает панель «Подсказки» с ними

### Requirement: Общий журнал подсказок

UI SHALL предоставлять общий read-only журнал подсказок по всем субъектам с фильтрами по статусу и характеру действия; записи показываются карточками полного состава ([#30](https://github.com/menshov-anatoliy/transaction-journal/issues/30)).

Traceability ID: requirement-ui-hint-log

#### Scenario: Журнал фильтруется по статусу и характеру
Traceability ID: scenario-ui-hint-log-filters
- **WHEN** в журнале подсказок выбраны фильтры статуса и характера
- **THEN** список показывает только совпадающие записи по всем субъектам

### Requirement: Группы характеров — общий слой отображения подсказок

Панель «Подсказки» и общий журнал подсказок SHALL группировать записи по закрытому справочнику групп v1: «Риск-режим» (портфельные лимиты и режим риска), «Управление конструкцией» (выход, защита прибыли, цель по прибыли, снижение риска, роллирование, перестройка/разборка, прочее), «Фьючерсная нога» (управление фьючерсной ногой); пустые группы SHALL не показываться. Тот же справочник SHALL переиспользоваться секциями сводки следующего change ([#34](https://github.com/menshov-anatoliy/transaction-journal/issues/34), [#24](https://github.com/menshov-anatoliy/transaction-journal/issues/24)).

Traceability ID: requirement-ui-hint-section-groups

#### Scenario: Панель группирует живые подсказки по группам
Traceability ID: scenario-ui-panel-groups-hints
- **WHEN** у субъекта есть живые подсказки разных характеров
- **THEN** панель показывает их по группам справочника v1
- **AND** группы без подсказок не отображаются

#### Scenario: Журнал подсказок фильтруется по группе
Traceability ID: scenario-ui-hint-log-group-filter
- **WHEN** в журнале подсказок выбрана группа
- **THEN** список ограничивается характерами этой группы

### Requirement: Ручной запуск прохода кнопкой в UI

UI SHALL предоставлять кнопку ручного запуска прохода агента; по завершении — обновление индикаторов и панелей; при ошибке корпуса SHALL показываться агрегированная ошибка со списком проблем ([ADR-0007](../../../../../../docs/adr/0007-hints-agent-architecture.md)).

Traceability ID: requirement-ui-manual-pass-button

#### Scenario: Кнопка запускает проход немедленно
Traceability ID: scenario-ui-manual-pass-runs-engine
- **WHEN** владелец нажимает кнопку запуска прохода
- **THEN** движок выполняет проход по текущим журналу, маркам и снимку корпуса
- **AND** панели и индикаторы обновляются по его результатам

#### Scenario: Ошибка корпуса показывается в UI запуска
Traceability ID: scenario-ui-manual-pass-corpus-error
- **WHEN** корпус невалиден при ручном запуске
- **THEN** UI показывает агрегированную ошибку со списком всех проблем карточек
