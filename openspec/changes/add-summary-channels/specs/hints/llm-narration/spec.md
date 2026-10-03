# Spec Delta

## ADDED Requirements

### Requirement: LLM излагает только тела секций

Заголовок с as-of и технический футер SHALL всегда собираться кодом — фактические обязательные элементы не проходят через модель. LLM SHALL излагать только тела секций; скелет SHALL быть один на оба режима: LLM — связное изложение, шаблон при деградации — те же секции списками ([#34](https://github.com/menshov-anatoliy/transaction-journal/issues/34), [ADR-0007](../../../../../../docs/adr/0007-hints-agent-architecture.md)).

Traceability ID: requirement-narration-llm-bodies-only

#### Scenario: Заголовок и футер не зависят от модели
Traceability ID: scenario-narration-header-footer-templated
- **WHEN** сводка собирается в любом режиме изложения
- **THEN** заголовок с as-of и футер идентичны шаблонным

### Requirement: Обязательные элементы фиксируются JSON-схемой ответа

Ответ LLM SHALL ограничиваться JSON-схемой: все подсказки и обязательные элементы присутствуют в обязательном порядке; нарушение схемы SHALL вызывать retry, затем — деградацию до шаблона ([#34](https://github.com/menshov-anatoliy/transaction-journal/issues/34), [ADR-0007](../../../../../../docs/adr/0007-hints-agent-architecture.md)).

Traceability ID: requirement-narration-json-schema-guarantees

#### Scenario: Нарушение схемы ретраится и деградирует
Traceability ID: scenario-narration-schema-retry-then-template
- **WHEN** ответ модели нарушает JSON-схему после повторной попытки
- **THEN** сводка доставляется шаблонным текстом тех же секций
- **AND** ни одна живая подсказка не теряется

### Requirement: Недоступность LLM деградирует только текст

Недоступность LLM SHALL деградировать только текст сводки (шаблон вместо изложения); фиксация подсказок движком SHALL продолжаться — функция не теряется ([#31](https://github.com/menshov-anatoliy/transaction-journal/issues/31), [ADR-0007](../../../../../../docs/adr/0007-hints-agent-architecture.md)).

Traceability ID: requirement-narration-unavailable-degrades-text

#### Scenario: LLM недоступен — подсказки живут
Traceability ID: scenario-narration-llm-down-hints-persist
- **WHEN** провайдер LLM недоступен в момент прохода
- **THEN** сводка доставляется шаблоном
- **AND** проход фиксирует подсказки как при доступном LLM

### Requirement: Провайдер и модель задаются конфигурацией

Подключение LLM SHALL идти через `Microsoft.Extensions.AI` (`IChatClient`); провайдер и модель SHALL сменяться конфигурацией без изменения кода (дефолт — z.ai, модель GLM-5.3-Flash, OpenAI-совместимый base URL). Бюджет LLM SHALL оставаться менее $1/мес на каденции 3 прохода/день ([ADR-0007](../../../../../../docs/adr/0007-hints-agent-architecture.md)).

Traceability ID: requirement-narration-provider-configurable

#### Scenario: Смена провайдера без кода
Traceability ID: scenario-narration-provider-switch-config
- **WHEN** конфигурация указывает другого провайдера или модель (например локальную Ollama)
- **THEN** изложение использует её без правки кода

### Requirement: Инструкции агента — редактируемый файл со встроенным дефолтом

Изложение SHALL руководствоваться инструкциями агента из редактируемого файла (отдельный настраиваемый путь, по умолчанию `agent-prompt.md` рядом с `rules/`), переопределяющими встроенный в код минимальный дефолт с обязательным правилом «никаких прогнозов»; отсутствие или битость файла SHALL не ломать проход ([ADR-0007](../../../../../../docs/adr/0007-hints-agent-architecture.md)).

Traceability ID: requirement-narration-agent-instructions-file

#### Scenario: Файл инструкций переопределяет дефолт
Traceability ID: scenario-narration-instructions-override
- **WHEN** рядом с корпусом лежит валидный `agent-prompt.md`
- **THEN** изложение руководствуется им вместо встроенного дефолта

#### Scenario: Отсутствие файла не ломает проход
Traceability ID: scenario-narration-instructions-missing-ok
- **WHEN** файл инструкций отсутствует или не читается
- **THEN** проход выполняется со встроенным дефолтом
