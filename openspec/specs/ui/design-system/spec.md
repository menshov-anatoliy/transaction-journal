# ui/design-system Specification

## Purpose

Соответствие визуального слоя SPA дизайн-системе `design.pen`: токены, типографика, переиспользуемые примитивы и приёмка экранов скриншот-сверкой через MCP pen.

## Requirements

### Requirement: Визуальный слой строится на дизайн-токенах design.pen
Traceability ID: requirement-visual-layer-uses-design-tokens
Система SHALL строить цвета, шрифты и семантические пары визуального слоя SPA на 25 переменных design.pen (фоновая триада, границы, текстовые уровни, зелёный акцент ×4, пары risk/neg/info, зоны фининдикатора riskZone/profitZone/superZone, подпись сверхприбыли, минор-заливка нереализованного убытка). Визуальный слой SHALL NOT использовать цвета вне дизайн-системы; осознанные отклонения фиксируются в change-артефакте до появления в коде.

#### Scenario: Страница не содержит вне-токенных цветов
Traceability ID: scenario-page-uses-design-colors
- **WHEN** инспектируется тема и классы любой страницы после сверки
- **THEN** все цветовые значения разрешаются в дизайн-токены; Tailwind-палитры вне системы (amber, индиго-акцент) и цветовые хардкоды отсутствуют

#### Scenario: Фининдикатор полностью токенизирован
Traceability ID: scenario-finresult-colors-use-own-tokens
- **WHEN** инспектируется любой вид фининдикатора (полный, средний, компактный)
- **THEN** подпись сверхприбыли и минор-заливка нереализованного убытка разрешаются в собственные дизайн-токены design.pen, а не в хардкод или заимствование чужого по смыслу токена

#### Scenario: Осознанное отклонение зафиксировано
Traceability ID: scenario-design-deviation-is-recorded
- **WHEN** экрану требуется отклонение от дизайн-системы
- **THEN** решение записано в артефакте активного change, а не молча закодировано

### Requirement: Типографика соответствует дизайн-системе
Traceability ID: requirement-typography-matches-design-system
Система SHALL использовать Inter как основной шрифт интерфейса и JetBrains Mono в MD-редакторе. Типографика SHALL соблюдать дизайн-диапазон: титулы разделов 21/600, бренд 14/600, акцентное значение итога 18/600, ячейки таблиц 12/11.5, подписи метрик 11/12.

#### Scenario: Заголовки разделов
Traceability ID: scenario-section-title-typography
- **WHEN** открывается любая страница SPA
- **THEN** титул раздела рендерится Inter 21/600, а не системным стеком 24px

### Requirement: Повторяющиеся элементы рендерятся примитивами дизайн-системы
Traceability ID: requirement-reusable-design-primitives
Статусы, источники, метрики, кнопки, нав-пункты, пункты сессий и статусы tool-вызовов SHALL рендериться переиспользуемыми примитивами, визуально соответствующими reusable-компонентам design.pen (Чип/Статус, Чип/Источник, Метрика, Кнопка/*, Нав-пункт, Сессия/пункт, Tool-статус), а не инлайн-разметкой страниц.

#### Scenario: Статус конструкции
Traceability ID: scenario-construction-status-primitive
- **WHEN** таблица конструкций или детали показывают статус
- **THEN** статус рендерится примитивом Чип/Статус с дизайн-палитрой, единым образом на всех экранах

#### Scenario: Метрики карточки конструкции
Traceability ID: scenario-construction-metric-primitive
- **WHEN** открывается карточка конструкции
- **THEN** показатели рендерятся примитивом Метрика (подпись 11, значение 12), а не постраничными dl-гридами

### Requirement: Экран принимается скриншот-сверкой с design.pen
Traceability ID: requirement-screenshot-acceptance
Каждый экран или слой после сверки SHALL быть принят сравнением скриншота живой страницы с макетом design.pen через MCP pen. Пары скриншотов SHALL сохраняться как evidence сверки в репозитории.

#### Scenario: Закрытие таска сверки
Traceability ID: scenario-screenshot-evidence-is-saved
- **WHEN** пер-страничный или послойный таска сверки завершён
- **THEN** пара «макет/страница» сохранена в `.wf-research/design-audit/screenshots/` и доступна для проверки

### Requirement: design.pen — единственный источник визуальных решений
Traceability ID: requirement-design-pen-single-source
Чтение дизайн-системы SHALL выполняться через MCP pen (или его машиночитаемый экстракт), а не прямым парсингом файла. На время сверки design.pen SHALL быть неизменным; изменение дизайна после сверки SHALL влечь повторную сверку затронутых экранов.

#### Scenario: Извлечение данных дизайна
Traceability ID: scenario-design-data-from-approved-source
- **WHEN** для реализации или проверки нужны данные дизайна
- **THEN** используется MCP pen или его экстракт `.wf-research/design-audit/design-extract.json`
