# Tasks

## 1. Планировщик проходов

- [ ] 1.1 `SummaryScheduler : BackgroundService` в Hints: слоты из настройки (3 времени начала/середины/конца дня), окно активности, сон до слота/открытия окна, пропуск слотов при простое без навёрстывания; регистрация в Web. Проверить: `requirement-delivery-cadence-window` — тесты слота внутри окна, тишины вне окна, невыполненного слота.
- [ ] 1.2 Ручной запуск прохода из Telegram `/summary` вне зависимости от окна; общий конвейер «проход → сводка → доставка» для планового и ручного запуска. Проверить: `scenario-delivery-manual-outside-window`.

## 2. Telegram-шлюз

- [ ] 2.1 `TelegramGateway` поверх Bot API long-polling (отдельный task с cancellationToken, корректный shutdown); порт `ISummaryChannel`; настройка токена бота и chat id. Проверить: интеграционный тест на тестовом токене/эхо, shutdown не зависает.
- [ ] 2.2 Команды: `/summary` — полный формат всегда; `/status` — шаблонный ответ (синхронизация, марки, число живых подсказок, режим последнего изложения, окно, валидность корпуса); все прочие команды игнорируются. Проверить: `requirement-delivery-telegram-read-only`, `requirement-delivery-status-command`, `scenario-delivery-corpus-error-in-summary`.
- [ ] 2.3 Пропуск прохода при недоступности марок с диагностикой, без сборки и доставки. Проверить: `requirement-delivery-market-unavailable-skip`.

## 3. Сборщик сводки

- [ ] 3.1 `SummaryComposer`: скелет 5 секций, as-of двумя строками в локальном поясе exe, портфельная секция, секции конструкций по группам v1 (порядок, скрытие пустых), конструкции без живых подсказок не упоминаются. Проверить: `requirement-digest-five-section-skeleton`, `requirement-digest-section-groups`.
- [ ] 3.2 Наполнение: полный набор живых `new` подсказок, строки «значок характера + формулировка по чёткости», «+M новых» от `lastDeliveredAt`; рыночный блок baseCoin (underlyingPrice, фандинг) в конце. Проверить: `requirement-digest-full-live-set-with-new-marker`, `requirement-digest-hint-line-format`.
- [ ] 3.3 Чек-лист: полный перечень (значок + короткое имя) в начале дня и `/summary`; строка «Чек-лист: N» в середине/конце; пустой автопроход — короткое сообщение с as-of. Проверить: `requirement-digest-checklist-placement`, `requirement-digest-empty-pass-short`.
- [ ] 3.4 Длинные сводки: разбивка по границам секций (заголовок с as-of в первом сообщении), свёртка секции конструкций при экстремальном объёме; эфемерность — тексты не хранятся, маркеры «включая N пропущенных проходов» и «+M новых» из `lastDeliveredAt` и счётчика слотов. Проверить: `requirement-digest-split-by-sections`, `requirement-digest-ephemeral-buffered`.

## 4. LLM-изложение

- [ ] 4.1 `NarrationService` через `IChatClient`: вход — инструкции агента + тела секций, выход — JSON по схеме с обязательными элементами; валидация, один retry, деградация до шаблона тех же секций. Проверить: `requirement-narration-json-schema-guarantees`, `requirement-narration-llm-bodies-only`.
- [ ] 4.2 Заголовок с as-of и футер — всегда шаблонные; недоступность LLM не влияет на фиксацию подсказок; состояние последнего изложения для `/status`. Проверить: `requirement-narration-unavailable-degrades-text`, `scenario-narration-header-footer-templated`.
- [ ] 4.3 Конфигурация провайдера и модели (дефолт z.ai GLM-5.3-Flash, OpenAI-совместимый base URL) без правки кода; смета бюджета < $1/мес на ~90 проходов. Проверить: `requirement-narration-provider-configurable`.
- [ ] 4.4 Инструкции агента: файл `agent-prompt.md` (настраиваемый путь, дефолт рядом с `rules/`), встроенный дефолт с обязательным правилом «никаких прогнозов», отсутствие/битость файла не ломают проход. Проверить: `requirement-narration-agent-instructions-file`.

## 5. Настройки и сквозная приёмка

- [ ] 5.1 Настройки канала в UI «Настройки» и конфигурации: токен бота, chat id, окно активности, времена трёх проходов, провайдер LLM/ключ/URL, пути корпуса и инструкций; без токена доставка отключена. Проверить: настройки читаются планировщиком и шлюзом; доступы — по тикету [#35](https://github.com/menshov-anatoliy/transaction-journal/issues/35).
- [ ] 5.2 Сквозной прогон на живом журнале владельца: 3 прохода в окне, `/summary` и `/status` отвечают, as-of присутствует, недоступность LLM деградирует до шаблона без потери подсказок; приёмка — сверка сводки с ручным разбором портфеля ([#31](https://github.com/menshov-anatoliy/transaction-journal/issues/31)). Проверить: протокол приёмки владельцем; `openspec validate add-summary-channels --strict`.
