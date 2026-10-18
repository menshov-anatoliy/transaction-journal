# Tasks

## 1. Снимки рыночных данных

- [ ] 1.1 Сверить живым ответом `GET /v5/market/tickers` (category=option) единицы и знак `theta` против временной стоимости реального тикера; зафиксировать вывод комментарием в маппинге `BybitTicker`. Проверка: вывод записан, порядок величины сходится.
- [ ] 1.2 Добавить доменную сущность `InstrumentMarketSnapshot` (`Symbol, MarkPrice, UnderlyingPrice, Theta, ReceivedAt`) с конфигурацией и индексом `(Symbol, ReceivedAt)` в `JournalDbContext`, EF-миграцию. Проверка: `dotnet build` чист, миграция применяется к пустой базе.
- [ ] 1.3 Расширить `InstrumentMarkProvider.GetFreshMarkAsync`: снимок ответа дополняется `UnderlyingPrice`/`Theta`, каждая успешная выборка дописывает строку истории, возвращаемый снимок (`IFreshInstrumentMarkSource`) несёт nullable-поля теты и индекса; кэш `InstrumentMarks` не меняется. Проверка: тесты провайдера на запись истории, отсутствие записи при пустом ответе биржи и сохранность кэша последней марки.

## 2. Вычислитель метрик времени жизни

- [ ] 2.1 Реализовать вычислитель метрик ноги (Application/Analytics): внутренняя/временняя стоимость от `underlyingPrice` и страйка, дневной распад `theta × остаток`, остаток суток `floor((deliveryTime − now)/24ч)` UTC через `TimeProvider`; деградация прочерком без рыночных данных, сутки — без них. Проверка: юнит-тесты сценариев `scenario-leg-itm-call-split`, `scenario-leg-otm-put-zero-intrinsic`, `scenario-leg-value-scaled-by-residual`, `scenario-leg-decay-theta-times-residual`, `scenario-leg-decay-short-sign`, `scenario-leg-days-floor`, `scenario-leg-days-no-market-data` зелёные.
- [ ] 2.2 Прирост распада: ближайший снимок истории с `ReceivedAt ≤ now − 24 ч`, прирост `(theta_now − theta_снимка) × текущий остаток`; без снимка — прочерк. Проверка: юнит-тесты `scenario-leg-acceleration-24h-snapshot`, `scenario-leg-acceleration-no-24h-snapshot` зелёные.
- [ ] 2.3 Саммари конструкции и предупреждение об экспирации: минимум суток по открытым опционным ногам, нетто-распад, суммарная временная стоимость; запись предупреждения на инструмент при остатке ≤ 14 (открытая конструкция, открытая нога). Проверка: юнит-тесты `scenario-summary-nearest-expiry-min`, `scenario-summary-net-decay-sum`, `scenario-summary-no-option-legs`, `scenario-expiry-warning-threshold-14`, `scenario-expiry-warning-one-per-instrument`, `scenario-expiry-warning-open-legs-only`, `scenario-expiry-warning-independent-of-agent` зелёные.

## 3. Контракты и read-модели

- [ ] 3.1 Встроить вычислитель в `ConstructionMetricsCalculator`: саммари и предупреждение об экспирации попадают в контракты списка, превью и карточки; записи предупреждения входят в существующий `RiskWarnings` (счётчик бейджа растёт автоматически). Проверка: тесты read-моделей на состав метрик и предупреждений списка/карточки зелёные.
- [ ] 3.2 Расширить HTTP-контракты (`ConstructionsEndpoints`, `ConstructionCardEndpoints`) nullable-полями: строка списка `daysToNearestExpiry`, `netDailyDecayUsdt`; строка позиции `daysToExpiry`, `dailyDecayUsdt`, `intrinsicValueUsdt`, `timeValueUsdt`, `decayDayAgoUsdt`. Проверка: контрактные тесты сериализации с null и значениями зелёные.

## 4. UI

- [ ] 4.1 Обновить design.pen: колонки «До экспирации» и «Распад» в таблице списка и таблице позиций карточки, тултип состава, жёлтая подсветка строки и красные сутки в зоне предупреждения, саммари времени жизни в сводке карточки. Проверка: фреймы макета содержат новые элементы.
- [ ] 4.2 Frontend: колонки и деградации в `constructions-table` (сутки/распад, красные сутки ≤ 14, прочерки). Проверка: скриншот-сверка списка с design.pen.
- [ ] 4.3 Frontend: колонки таблицы позиций `positions-table` с тултипом состава (внутренняя/временная стоимость, «вчера → сегодня») и подсветкой строки ≤ 14 суток; саммари времени жизни в `metric-strip` карточки. Проверка: скриншот-сверка карточки с design.pen.

## 5. Интеграционная проверка

- [ ] 5.1 Сквозной сценарий на живых данных: синхронизация → открытие списка и карточки конструкции с опционными ногами → метрики и предупреждение об экспирации видны, завтрашнее повторное чтение даёт прирост распада. Проверка: ручная сверка величин с тикерами Bybit.
- [ ] 5.2 Прогон полного набора тестов `TransactionJournal.Tests` и фронтенд-проверок; сборка без предупреждений. Проверка: все зелёные.
