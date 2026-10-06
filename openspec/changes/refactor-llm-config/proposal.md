# Proposal

## Why

Секция `Llm` задумывалась как общая конфигурация LLM-провайдера: `config/local-secrets` закрепляет её за ключом LLM-провайдера, шаблон `appsettings.Local.json.example` описывает её как «LLM-провайдер сводки». Фактически же контракт модели чата консультаций живёт в отдельной секции `Consultations:ChatModel`, а `Llm` деградировал до запасного источника ключа с мёртвыми ключами `Provider`/`BaseUrl`/`Model` (код их не читает). Скоро появится второй LLM-потребитель — изложение сводок подсказок (change `add-summary-channels`, spec `hints/llm-narration`), и без общей схемы он заведёт третий набор параметров провайдера.

## What Changes

- **BREAKING** Секция `Consultations:ChatModel` удаляется. Общие параметры провайдера переезжают в секцию `Llm`: `Provider` (дефолт `zai`), `BaseUrl` (дефолт `https://api.z.ai/api/paas/v4`), `ApiKey` — единственный ключ доступа, запасные источники (`Consultations:ChatModel:ApiKey` ?? `Llm:ApiKey`) упраздняются.
- Рабочие модели задаются подсекциями общей секции: `Llm:Chat:Model` — модель чата консультанта (дефолт `glm-5.3`), `Llm:Hint:Model` — модель изложения сводок подсказок (дефолт `glm-5.3-flash`). Подсекции задают только модель; эндпоинт и ключ общие.
- Чат консультанта переходит с coding-эндпоинта z.ai (`/api/coding/paas/v4/`) на общий `paas/v4` — осознанное решение владельца, тариф запросов меняется.
- `BaseUrl` нормализуется кодом: trailing slash дописывается при отсутствии, обе формы значения валидны.
- Шаблон `appsettings.Local.json.example` переписывается под новую схему; заодно чинится существующий рассинхрон — шаблон указывал `Llm:ZaiApiKey`, который код никогда не читал.
- Локальный `appsettings.Local.json` владельца переносится на новую схему (файл вне репозитория).
- Потребитель `Llm:Hint` появляется позже в change `add-summary-channels` (задача 4.3): этот change задаёт контракт и резолв, изложение только потребляет.

## Capabilities

### New Capabilities

- `config/llm-provider`: общая конфигурация LLM-провайдера — секция `Llm` с параметрами провайдера (эндпоинт, ключ, правило стороннего провайдера) и подсекции рабочих моделей `Chat`/`Hint` для потребителей (консультации, изложение сводок).

### Modified Capabilities

- `consultations/tools`: требование «Модель чата задаётся отдельной конфигурацией» переходит с собственной секции `Consultations:ChatModel` на подсекцию `Llm:Chat` общей секции провайдера; keyed-клиент, дефолт z.ai GLM-5.3, смена конфигурацией без кода и бюджет сохраняются.

## Impact

- `src/TransactionJournal.Consultations/ConsultationChatModelOptions.cs` — резолв из общих параметров `Llm` + `Llm:Chat:Model`; обновление traceability-комментариев.
- `src/TransactionJournal/Program.cs` — резолв общих параметров провайдера и нормализация `BaseUrl` в одном месте, проводка обеих моделей, текст ошибки про незастроенный ключ.
- `src/TransactionJournal.Hints/HintChatModelOptions.cs` — новый: резолв модели изложения из `Llm` + `Llm:Hint:Model`, дефолт `glm-5.3-flash`.
- `src/TransactionJournal/appsettings.Local.json.example` — новая схема секции `Llm`; локальный `appsettings.Local.json` — то же вне репозитория.
- Тесты: `ConsultationChatModelOptionsTests` обновляется, новые тесты `HintChatModelOptions`.
- Спеки: дельта `config/llm-provider` (новая capability), дельта `consultations/tools`.
- Связи с issue нет — требование фиксируется этим change'ом; потребитель `Llm:Hint` (задача 4.3 change `add-summary-channels`) читает подсекцию без правки её контракта.
