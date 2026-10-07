# Spec Delta

## Purpose

Снимок контекста чата агента и инструкции ИИ-помощника: что ассистент видит о конструкции или портфеле и корпусе правил, и по каким принципам отвечает владельцу.

## ADDED Requirements

### Requirement: Снимок контекста чата собирается детерминированно кодом

Перед сообщением владельца SHALL собираться снимок контекста в markdown с явным as-of. Для чата с привязкой к конструкции снимок SHALL содержать конструкцию с позициями и результатами, портфельные агрегаты и лимиты, компактный индекс корпуса правил (каждая карточка: id и краткое содержание). Для чата без привязки снимок SHALL содержать портфельный уровень журнала — агрегаты и лимиты — и индекс корпуса, без раздела конструкции. Снимок SHALL собираться кодом поверх read-моделей журнала без участия LLM; живые подсказки движка в контекст SHALL не входить; рыночные данные — только через инструменты в момент сообщения ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53), карта [#50](https://github.com/menshov-anatoliy/transaction-journal/issues/50), [ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md)).

Traceability ID: requirement-chat-context-deterministic-snapshot

#### Scenario: Снимок привязанного чата содержит конструкцию с as-of
Traceability ID: scenario-chat-context-construction-snapshot-with-asof
- **WHEN** владелец отправляет сообщение в чате, привязанном к конструкции
- **THEN** ассистенту передан markdown-снимок: конструкция, портфельные агрегаты, индекс корпуса
- **AND** каждый раздел несёт отметку as-of момента сборки

#### Scenario: Снимок чата без конструкции — портфельный уровень
Traceability ID: scenario-chat-context-portfolio-snapshot-without-construction
- **WHEN** владелец отправляет сообщение в чате без привязки к конструкции
- **THEN** снимок содержит портфельные агрегаты и лимиты журнала с as-of и индекс корпуса
- **AND** раздел конкретной конструкции в снимке отсутствует

#### Scenario: Живые подсказки движка не попадают в контекст
Traceability ID: scenario-chat-context-hints-excluded
- **WHEN** движок подсказок сгенерировал живые подсказки по конструкции
- **THEN** они отсутствуют в снимке контекста чата

#### Scenario: Карточка правила читается целиком по требованию
Traceability ID: scenario-chat-context-card-index-only
- **WHEN** снимок собран
- **THEN** корпус представлен компактным индексом карточек
- **AND** полный текст карточки доступен только инструментом чтения

### Requirement: Инструкции ИИ-помощника — редактируемый файл со встроенным дефолтом

Поведение ИИ-помощника SHALL задаваться инструкциями из редактируемого файла `agent-prompt.md` (отдельный настраиваемый путь, по умолчанию рядом с `rules/`), переопределяющими встроенный минимальный дефолт; отсутствие или битость файла SHALL не ломать чат — работают дефолтные инструкции ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53), [ADR-0009](../../../../../../docs/adr/0009-consultation-chat-architecture.md), паттерн [ADR-0007](../../../../../../docs/adr/0007-hints-agent-architecture.md)).

Traceability ID: requirement-chat-context-agent-instructions-file

#### Scenario: Файл инструкций переопределяет дефолт
Traceability ID: scenario-chat-context-instructions-override
- **WHEN** рядом с корпусом лежит валидный `agent-prompt.md`
- **THEN** ИИ-помощник руководствуется им вместо встроенного дефолта

#### Scenario: Отсутствие файла не ломает чат
Traceability ID: scenario-chat-context-instructions-missing-ok
- **WHEN** файл инструкций отсутствует или не читается
- **THEN** чат работает на встроенном дефолте

### Requirement: Ответы — сценарии «если/то» с опорой на корпус

Инструкции SHALL обязывать ИИ-помощника: формулировать планы действий «если/то» по свершившимся фактам с порогами — без направленных прогнозов цены; варианты в рамках корпуса сопровождать цитированием id карточек правил; варианты вне корпуса явно маркировать «вне корпуса правил»; план управления выдавать markdown-текстом в ответе, без сохранения ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53), карта [#50](https://github.com/menshov-anatoliy/transaction-journal/issues/50)).

Traceability ID: requirement-chat-context-scenario-conduct

#### Scenario: Внутрикорпусный вариант цитирует карточку
Traceability ID: scenario-chat-context-in-corpus-citation
- **WHEN** ИИ-помощник предлагает вариант, покрываемый правилом корпуса
- **THEN** вариант сопровождается id соответствующей карточки правила

#### Scenario: Внекорпусный вариант маркирован
Traceability ID: scenario-chat-context-out-of-corpus-marked
- **WHEN** ни одна карточка корпуса не покрывает предлагаемый вариант
- **THEN** вариант явно помечен «вне корпуса правил»

#### Scenario: Прогнозы цены отсутствуют
Traceability ID: scenario-chat-context-no-price-forecasts
- **WHEN** владелец спрашивает о будущем движении цены
- **THEN** ИИ-помощник переформулирует ответ в сценарии «если/то» по фактам с порогами
- **AND** направленный прогноз цены не выдаётся

### Requirement: Закрытая конструкция консультируется в пост-мортем

Для закрытой конструкции снимок SHALL собираться по финальному состоянию журнала; режим SHALL задаваться инструкциями как пост-мортем («работа над ошибками»); состав инструментов SHALL быть идентичен управленческому режиму — рыночные данные доступны ([#53](https://github.com/menshov-anatoliy/transaction-journal/issues/53), карта [#50](https://github.com/menshov-anatoliy/transaction-journal/issues/50)).

Traceability ID: requirement-chat-context-postmortem-mode

#### Scenario: Закрытая конструкция — работа над ошибками
Traceability ID: scenario-chat-context-closed-construction-postmortem
- **WHEN** владелец открывает чат, привязанный к закрытой конструкции
- **THEN** ИИ-помощник ведёт разбор финального состояния журнала в режиме пост-мортема
- **AND** рыночные инструменты доступны как для открытой
