# Research: Bybit V5 API — сделки, delivery, марки, авторизация из C#

Статус: research-заметка по тикету [#2](https://github.com/menshov-anatoliy/transaction-journal/issues/2) (карта усилия — #1).
Дата: 2026-09-19. Все факты сверены с официальной документацией Bybit V5 API и официальными репозиториями bybit-exchange на момент исследования; у каждого факта — ссылка на первоисточник.

Базовые URL (mainnet): `https://api.bybit.com` и `https://api.bytick.com`; testnet: `https://api-testnet.bybit.com`.
Источник: [Integration Guidance](https://bybit-exchange.github.io/docs/v5/guide).

---

## 1. История исполнения сделок Unified-аккаунта (execution list)

Эндпоинт: **`GET /v5/execution/list`** («Get Trade History», раздел Trade).
Источник: [docs/v5/order/execution](https://bybit-exchange.github.io/docs/v5/order/execution).

Поведение и пагинация:

- Записи отсортированы по `execTime` **по убыванию**; при равном `execTime` docs рекомендуют досортировывать по `execId + orderId + leavesQty`.
- Пагинация курсорная: параметр `cursor`, в ответе `nextPageCursor` — передать его следующим запросом.
- `limit`: `[1..100]`, по умолчанию `50`.
- Окно времени (`startTime`/`endTime`, ms):
  - не переданы → последние **7 дней**;
  - только `startTime` → `[startTime, startTime+7d]`;
  - только `endTime` → `[endTime-7d, endTime]`;
  - оба → `endTime - startTime <= 7 дней`.
  То есть бэкфилл делается «пролистыванием» 7-дневными окнами от текущего момента назад.
- Фильтры: `category` (обязателен: `linear`, `inverse`, `spot`, `option`), `symbol`, `orderId`, `orderLinkId`, `baseCoin` (для option по умолчанию `BTC`), `settleCoin`, `execType`.
- Приоритет фильтров: `orderId > orderLinkId > symbol > baseCoin`; если передан `orderId`/`orderLinkId`, остальные игнорируются.
- Для realtime docs рекомендует websocket-стрим `execution`, а не поллинг этого эндпоинта.

Состав полей ответа (каждая запись `list[]`): `symbol`, `orderId`, `orderLinkId`, `side` (`Buy`/`Sell`), `orderPrice`, `orderQty`, `leavesQty`, `createType`, `orderType` (`Market`/`Limit`), `stopOrderType`, `execFee`, `execFeeV2` (только `FutureSpread`), `execId`, `execPrice`, `execQty`, `execType`, `execValue`, `execTime` (ms), `feeCurrency`, `isMaker` (bool), `feeRate`, `markPrice` (марка на момент исполнения), `blockTradeId`, `closedSize` (закрытая часть позиции этим исполнением), `seq` (cross sequence; уникальность — `seq + symbol`), `extraFees`; опционные поля: `tradeIv`, `markIv`, `indexPrice`, `underlyingPrice`.
Источник: [docs/v5/order/execution — Response Parameters](https://bybit-exchange.github.io/docs/v5/order/execution).

Различия linear/option:

- У опционов есть `tradeIv`/`markIv`/`indexPrice`/`underlyingPrice`; у linear их нет (у linear `indexPrice` не возвращается).
- `category=option` без `symbol` запрашивается по `baseCoin` (дефолт `BTC`).
- Валюта комиссии — из `feeCurrency` (см. §5).

Rate limit: `/v5/execution/list` — **50 req/s**.
Источник: [docs/v5/rate-limit — Trade](https://bybit-exchange.github.io/docs/v5/rate-limit).

Важно: `closedPnl` в этом эндпоинте **не возвращается** (в списке полей его нет). Реализованный PnL по закрытиям берётся из `/v5/position/closed-pnl` (linear/inverse) или `/v5/asset/delivery-record` (delivery, см. §2).

Смежный эндпоинт: **`GET /v5/position/closed-pnl`** («Get Closed PnL», `category=linear|inverse`, опции не поддерживаются): те же правила 7-дневного окна и cursor; `limit [1..100]`, дефолт 50; поля: `symbol`, `orderId`, `side`, `qty`, `orderPrice`, `orderType`, `execType` (`Trade`, `BustTrade`, `SessionSettlePnL`, `Settle`, `MovePosition`, `ForwardSplitSettle`, `ReverseSplitSettle`), `closedSize`, `cumEntryValue`, `avgEntryPrice`, `cumExitValue`, `avgExitPrice`, `closedPnl`, `fillCount`, `leverage`, `openFee`, `closeFee`, `createdTime`, `updatedTime`.
Источник: [docs/v5/position/close-pnl](https://bybit-exchange.github.io/docs/v5/position/close-pnl).
Нюанс из FAQ: `avgEntryPrice`/`avgExitPrice` в closed-pnl — не фактические цены исполнения, а цены с учётом суммарных затрат (включая комиссии).
Источник: [FAQ — Closed PnL](https://bybit-exchange.github.io/docs/faq).

## 2. Delivery/экспирация опционов

### 2.1. Основной источник — Get Delivery Record

Эндпоинт: **`GET /v5/asset/delivery-record`** («Get Delivery Record», раздел Asset; в навигации помечен «(2 years)»).
Источник: [docs/v5/asset/delivery](https://bybit-exchange.github.io/docs/v5/asset/delivery).

- Покрывает inverse futures, USDC futures, USDT futures и **опционы**; сортировка по `deliveryTime` по убыванию.
- Параметры: `category` (обязателен: `inverse`, `linear`, `option`), `symbol`, `expDate` (формат `25MAR22`, по умолчанию — все), `startTime`/`endTime` (окно **30 дней**; без параметров — последние 30 дней; `endTime - startTime <= 30 дней`), `limit [1..50]` (дефолт 20), `cursor`.
- Поля ответа: `deliveryTime` (ms, number), `symbol`, `side` (`Buy`/`Sell`), `position` (исполненный размер), `entryPrice` (средняя входная), `deliveryPrice` (расчётная цена экспирации), `strike` (страйк), `fee` (комиссия за delivery), `deliveryRpl` (реализованный PnL доставки).
- Пример ответа в docs: `{"symbol":"BTC-29DEC22-16000-P","side":"Buy","deliveryTime":1672300800860,"strike":"16000","fee":"0.00000000","position":"0.01","deliveryPrice":"16541.86369547","deliveryRpl":"3.5"}`.
- Rate limit: `/v5/asset/delivery-record` — **50 req/s**. Источник: [rate-limit — Asset](https://bybit-exchange.github.io/docs/v5/rate-limit).

Отличить delivery от обычной сделки просто: delivery живёт в отдельном эндпоинте `/v5/asset/delivery-record` (в execution list его нет) и содержит `deliveryPrice`/`strike`/`deliveryRpl` вместо `execPrice`/`execFee`.

### 2.2. Транзакционный лог Unified-аккаунта

Эндпоинт: **`GET /v5/account/transaction-log`** («Get Transaction Log»), `accountType=UNIFIED`.
Источник: [docs/v5/account/transaction-log](https://bybit-exchange.github.io/docs/v5/account/transaction-log).

- Глубина: **«supports up to 2 years worth of data»** — задокументировано прямо на странице.
- Окно: без `startTime`/`endTime` — последние 24 часа; при обоих параметрах `endTime - startTime <= 7 дней`; `limit [1..50]`, дефолт 20; `cursor`.
- Фильтр `type` принимает значения enum `type(uta-translog)`, среди них:
  - `SETTLEMENT` — funding-сеттлмент USDT-перпа + сессионный 8-часовой сеттлмент USDC;
  - `DELIVERY` — «USDC Futures, Option delivery, Event Contract settlement» — то есть **delivery опционов виден в transaction log с type=DELIVERY**;
  - также `TRADE`, `LIQUIDATION`, `ADL`, `FUNDING`-родственные записи и т.д.
  Источник: [enum#typeuta-translog](https://bybit-exchange.github.io/docs/v5/enum).
- Поля записи: `id`, `symbol`, `category`, `side`, `transactionTime`, `type`, `transSubType`, `qty`, `size` (остаток позиции со знаком), `currency`, `tradePrice`, `funding`, `fee`, `cashFlow`, `change` (= `cashFlow + funding - fee`), `cashBalance`, `feeRate`, `bonusChange`, `tradeId`, `orderId`, `orderLinkId`, `extraFees`, `displayType`, `nextPageCursor`.
- Rate limit: `/v5/account/transaction-log` (UNIFIED) — **25 req/s**. Источник: [rate-limit — Account](https://bybit-exchange.github.io/docs/v5/rate-limit).

### 2.3. Delivery в execution list (не путать)

Enum `execType` эндпоинта execution list содержит `Delivery` («USDT futures delivery; Position closed due to delisting») и `Settle` («Inverse futures settlement»), а `createType` содержит `CreateBySettle` («USDC Futures delivery… записывается как trade, но не как order»).
Источник: [enum#exectype](https://bybit-exchange.github.io/docs/v5/enum), [enum#createtype](https://bybit-exchange.github.io/docs/v5/enum).
То есть в execution list «Delivery» относится к делистингу/поставке фьючерсов, а экспирация опционов отражается в `/v5/asset/delivery-record` и transaction log (`type=DELIVERY`).

### 2.4. Публичные расчётные цены

`GET /v5/market/delivery-price` (без аутентификации): по опционам возвращает символы в статусе `DELIVERING` (окно UTC 08:00–12:00), если не указан `symbol`; `settleCoin` по умолчанию `USDC`; поля `symbol`, `deliveryPrice`, `deliveryTime`; `limit [1..200]`, дефолт 50.
Источник: [docs/v5/market/delivery-price](https://bybit-exchange.github.io/docs/v5/market/delivery-price).

## 3. Публичные марки (tickers) без аутентификации

Эндпоинт: **`GET /v5/market/tickers`**.
Источник: [docs/v5/market/tickers](https://bybit-exchange.github.io/docs/v5/market/tickers).

- Параметры: `category` (обязателен), `symbol`, `baseCoin` (только option), `expDate` (только option, формат `25DEC22`). Для `category=option` обязательно передать `symbol` или `baseCoin`.
- Для linear/inverse (фьючерсы/перпы) поля включают: `lastPrice`, `markPrice`, `indexPrice`, `prevPrice24h`, `price24hPcnt`, `bid1Price`/`bid1Size`, `ask1Price`/`ask1Size`, `turnover24h`, `volume24h`, `openInterest`, `fundingRate`, `nextFundingTime`, `basisRate`, `basis`, `predictedDeliveryPrice` (за 30 минут до delivery), `deliveryFeeRate`, `deliveryTime` (только expiry-фьючерсы), `preOpenPrice` и др.
- Для option поля включают: `bid1Price`/`bid1Size`/`bid1Iv`, `ask1Price`/`ask1Size`/`ask1Iv`, `lastPrice`, `markPrice`, `indexPrice`, `markIv`, `underlyingPrice`, греки `delta`, `gamma`, `vega`, `theta`, `openInterest`, `turnover24h`, `volume24h`, `predictedDeliveryPrice`, `change24h`.
- `GET /v5/market/instruments-info` (публичный) — спецификация инструментов, `limit [1..1000]`, дефолт 500, cursor; для option: `optionsType` (`Call`/`Put`), `baseCoin`, `quoteCoin`, `settleCoin`, `launchTime`, `deliveryTime`, `deliveryFeeRate`, фильтры цены/лота, `displayName`; для linear: `contractType`, `status`, `baseCoin`, `quoteCoin`, `settleCoin`, `launchTime`, `deliveryTime` (время delivery expiry-фьючерса/делистинга перпа), `deliveryFeeRate`, `fundingInterval`, фильтры. Источник: [docs/v5/market/instrument](https://bybit-exchange.github.io/docs/v5/market/instrument).
- Server time (для синхронизации часов при подписи): `GET /v5/market/time`. Источник: [docs/v5/market/time](https://bybit-exchange.github.io/docs/v5/market/time).

Market-эндпоинты **отсутствуют в таблице per-UID API rate limits** — на них действует только общий IP-лимит (см. §8). Источник: [rate-limit](https://bybit-exchange.github.io/docs/v5/rate-limit).

## 4. Форматы символов

Опционы: `{BASE}-{DDMMMYY}-{STRIKE}-{C|P}`, например `BTC-27DEC24-2800-C`, `BTC-30DEC22-18000-C`, `ETH-3JAN23-1250-P`, `ETH-26DEC22-1400-C`.
Источники: примеры в [tickers](https://bybit-exchange.github.io/docs/v5/market/tickers), [instrument](https://bybit-exchange.github.io/docs/v5/market/instrument), [delivery-price](https://bybit-exchange.github.io/docs/v5/market/delivery-price); формат даты `25DEC22`/`25MAR22` подтверждён параметрами `expDate` в [tickers](https://bybit-exchange.github.io/docs/v5/market/tickers) и [delivery-record](https://bybit-exchange.github.io/docs/v5/asset/delivery).

- Дата: день 1–2 цифры (без ведущего нуля: `3JAN23`), месяц — 3 заглавные английские буквы, год — 2 цифры. Дата экспирации в UTC; delivery-окно опционов 08:00–12:00 UTC (см. `DELIVERING` в [delivery-price](https://bybit-exchange.github.io/docs/v5/market/delivery-price)).
- Парсинг: `symbol.Split('-')` → 4 части: базовый актив, дата (`DateTime.TryParseExact(ddMMMYY, "dMMMyy", InvariantCulture)`), страйк (`decimal`), тип (`C`/`P`). Надёжнее не верить строке на слово, а сверяться с `/v5/market/instruments-info?category=option` (`optionsType`, `baseCoin`, `deliveryTime`) — там же фильтры тика/лота.
- Страйк — целое/десятичное без форматирования (в примерах `2800`, `1250`, `16000`).

Фьючерсы linear:

- USDT-перпетуал: `BTCUSDT` (base+quote без разделителей); USDC-перпетуал: `BTCPERP`; датированные USDC-фьючерсы: `BTC-26SEP25` (с дефисом, как в примерах delivery у [delivery-price](https://bybit-exchange.github.io/docs/v5/market/delivery-price)); исторические USDT-квартальные имели вид `BTCUSDT-31MAR23`.
- `contractType`/`status`/`deliveryTime` из [instruments-info](https://bybit-exchange.github.io/docs/v5/market/instrument) — канонический способ отличить перп от датированного фьючерса и узнать экспирацию; у перпа `deliveryTime` означает время делистинга.

## 5. Комиссии

- В записи исполнения: `execFee` (сумма), `feeRate` (ставка), `feeCurrency` (валюта), `isMaker`. Источник: [execution list](https://bybit-exchange.github.io/docs/v5/order/execution).
- Знак: в transaction log прямо задокументировано, что `fee` «positive = расход, negative = rebate», а `funding` «positive = получение» и что funding **противоположен по знаку `execFee` из Get Trade History** → `execFee > 0` значит комиссия уплачена, `execFee < 0` — rebate получен. Источник: [transaction-log](https://bybit-exchange.github.io/docs/v5/account/transaction-log).
- Валюта: определяется полем `feeCurrency` записи исполнения; для USDT-linear это USDT; опционы на Bybit котируются/расчитываются в USDC (`settleCoin` по умолчанию USDC у [delivery-price](https://bybit-exchange.github.io/docs/v5/market/delivery-price), `settleCoin` в option-полях [instruments-info](https://bybit-exchange.github.io/docs/v5/market/instrument)).
- В closed-pnl комиссии разложены в `openFee`/`closeFee`. Источник: [close-pnl](https://bybit-exchange.github.io/docs/v5/position/close-pnl).
- В delivery-записи — поле `fee` (в примере `"0.00000000"`), плюс ставки `deliveryFeeRate` доступны в tickers и instruments-info. Источники: [delivery](https://bybit-exchange.github.io/docs/v5/asset/delivery), [tickers](https://bybit-exchange.github.io/docs/v5/market/tickers).
- Текущие персональные ставки можно получать приватным `GET /v5/account/fee-rate` (5 req/s). Источник: [rate-limit — Account](https://bybit-exchange.github.io/docs/v5/rate-limit).

## 6. Аутентификация HMAC из C# (без SDK)

Источник: [Integration Guidance — Authentication](https://bybit-exchange.github.io/docs/v5/guide).

Заголовки:

- `X-BAPI-API-KEY` — API-ключ;
- `X-BAPI-TIMESTAMP` — UTC-время в миллисекундах;
- `X-BAPI-SIGN` — подпись;
- `X-BAPI-RECV-WINDOW` — окно валидности (ms), по умолчанию `5000`;
- (в официальном C#-примере дополнительно передаётся `X-BAPI-SIGN-TYPE: 2`).

Правила подписи:

- Строка для подписи: GET → `timestamp + api_key + recv_window + queryString`; POST → `timestamp + api_key + recv_window + jsonBodyString` (конкатенация без разделителей).
- Алгоритм: HMAC-SHA256 от api_secret, результат — **hex в нижнем регистре**.
- Окно валидности: `server_time - recv_window <= timestamp < server_time + 1000`; локальные часы держать NTP-синхронизированными, серверное время проверять через `GET /v5/market/time`.
- Ключи бывают системные (HMAC) и self-generated (RSA) — для нашего случая достаточно системного HMAC-ключа.

Официальный C#-пример: [bybit-exchange/api-usage-examples → V5_demo/api_demo/Encryption_HMAC.cs](https://github.com/bybit-exchange/api-usage-examples/blob/main/V5_demo/api_demo/Encryption_HMAC.cs) — `HttpClient` + `HMACSHA256` + `BitConverter.ToString(...).Replace("-","").ToLower()`; там же примеры на других языках.

Нюанс для C#: queryString для подписи должен побайтово совпадать с отправляемым (порядок параметров и кодировка URL); проще всего собрать строку запроса вручную и использовать её и в URL, и в подписи.

Ограничение: IP из США и материкового Китая получают 403. Источник: [guide](https://bybit-exchange.github.io/docs/v5/guide).

## 7. Глубина истории

- `/v5/execution/list`: окно одного запроса ≤ 7 дней; **общая глубина хранения в документации не указана** (см. Открытые вопросы). Явного лимита «N дней назад» docs не декларируют.
- `/v5/position/closed-pnl`: те же правила 7-дневного окна; общая глубина не задокументирована. Источник: [close-pnl](https://bybit-exchange.github.io/docs/v5/position/close-pnl).
- `/v5/account/transaction-log`: **2 года** (задокументировано). Источник: [transaction-log](https://bybit-exchange.github.io/docs/v5/account/transaction-log).
- `/v5/asset/delivery-record`: в навигации docs помечен как «Get Delivery Record (2 years)». Источник: [навигация раздела Asset](https://bybit-exchange.github.io/docs/v5/asset/fund-history).
- Для данных «до апгрейда аккаунта до Unified» существует семейство `/v5/pre-upgrade/*` эндпоинтов (`/v5/pre-upgrade/execution/list`, `/v5/pre-upgrade/position/closed-pnl`, `/v5/pre-upgrade/account/transaction-log`, `/v5/pre-upgrade/asset/delivery-record`). Источник: официальные SDK — [pybit/pre_upgrade.py](https://github.com/bybit-exchange/pybit/blob/master/pybit/pre_upgrade.py).

## 8. Rate limits по нужным эндпоинтам

Таблица per-UID per-second лимитов и общий IP-лимит: [docs/v5/rate-limit](https://bybit-exchange.github.io/docs/v5/rate-limit).

| Эндпоинт | Лимит |
|---|---|
| `GET /v5/execution/list` | 50 req/s |
| `GET /v5/position/closed-pnl` | 50 req/s |
| `GET /v5/account/transaction-log` (UNIFIED) | 25 req/s |
| `GET /v5/asset/delivery-record` | 50 req/s |
| `GET /v5/market/tickers`, `/v5/market/instruments-info`, `/v5/market/delivery-price`, `/v5/market/time` | отсутствуют в per-UID таблице — действует только IP-лимит |

- IP-лимит: **600 запросов за 5-секундное окно на IP** на весь HTTP-трафик к API; превышение → 403 «access too frequent» и блокировка ~10 минут.
- Превышение per-UID лимита → `retCode 10006 "Too many visits!"`.
- Остаток лимита виден в заголовках ответа: `X-Bapi-Limit-Status`, `X-Bapi-Limit`, `X-Bapi-Limit-Reset-Timestamp`.

Для журнала (ручной sync + редкий опрос марок) лимиты избыточны: полный бэкфилл года торговли — сотни запросов при лимите 50/s.

## Открытые вопросы/риски

1. **Глубина execution list**: документация не фиксирует, сколько лет назад отдаются данные (только «7 дней на запрос»). Практику покажет первый бэкфилл; при необходимости — сверка с transaction-log (2 года) и закрытием в поддержку Bybit.
2. **`closedPnl` в execution list отсутствует** — в тикете он ожидался в составе полей. Реализованный PnL придётся собирать из `/v5/position/closed-pnl` (linear) и `/v5/asset/delivery-record` (delivery), либо считать самостоятельно из сделок (в журнале и так требуется свой «Реализованный результат», см. CONTEXT.md).
3. **Опции в closed-pnl не поддерживаются** (`category=linear|inverse` only) — PnL экспираций опционов только через delivery-record (`deliveryRpl`).
4. **Дробные/нестандартные страйки и базовые активы**: формат даты `dMMMyy` допускает однозначный день; при появлении новых базовых активов полагаться на instruments-info, а не на парсинг строки.
5. **Знак `execFee`** для rebate-сценариев (negative = rebate) задокументирован косвенно (через описание funding в transaction-log) — при первой синхронизации проверить на реальных maker-сделках.
6. **Разделение категории опционов по `settleCoin`** (USDC): если появится несколько расчётных монет — фильтровать `settleCoin` в запросах.
7. Docs периодически меняют URL страниц (например, execution list переехал из `/position/execution-list` в `/order/execution`) — в спеке ссылаться на маршруты API, а не на URL страниц документации.
8. Доступность API из РФ/региона и выбор домена (`api.bybit.com` и зеркала) — проверить на месте; docs упоминают региональные базовые URL.

## Выводы для спеки синхронизации

1. **Источник сделок**: `GET /v5/execution/list` с `category=linear` и `category=option` (два прохода). Идемпотентный ключ записи — `execId` (+ `orderId`, `seq` для отладки). Окно запроса ≤ 7 дней, пагинация `nextPageCursor`, `limit=100`. Инкрементальный sync — от последнего `execTime` минус перекрытие (сортировка desc — листать до уже известных `execId`).
2. **Delivery-закрытия**: отдельно тянуть `GET /v5/asset/delivery-record?category=option` (окно 30 дней, limit 50) для экспираций опционов и `category=linear` для датированных фьючерсов; дедуп по `symbol + deliveryTime`. Transaction log (`type=DELIVERY`) — контроль полноты и валютные движения, не основной источник.
3. **Реализованный PnL**: не брать closedPnl Bybit как единственную истину — журнал считает свой «Реализованный результат» из сделок и комиссий (глоссарий CONTEXT.md); closed-pnl/deliveryRpl использовать для сверки.
4. **Марки для нереализованного результата**: публичный `GET /v5/market/tickers` (option: `markPrice` + `underlyingPrice`; linear: `markPrice`) без API-ключа; кэшировать на стороне журнала.
5. **Инструменты**: справочник инструментов строить из `/v5/market/instruments-info` (option: `optionsType`, `baseCoin`, `deliveryTime`, фильтры тика/лота; linear: `contractType`, `deliveryTime`), символ опциона парсить `{BASE}-{dMMMyy}-{strike}-{C|P}` с культурой InvariantCulture.
6. **Авторизация**: собственный тонкий HttpClient-клиент с HMAC-SHA256 (hex lower) по официальному C#-примеру; timestamp из `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`, `recv_window` 5000, NTP-синхронизация; ключ только с правами на чтение.
7. **Rate limits**: вставить в клиент уважение `X-Bapi-Limit-Status` и бэкофф на `retCode 10006` и 403; для наших объёмов запас огромный.
8. **Комиссии**: хранить сумму (`execFee`/`fee`), валюту (`feeCurrency`/`currency`) и признак maker (`isMaker`) на каждой сделке; знак: положительный = уплачено.
