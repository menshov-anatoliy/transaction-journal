# Tasks

## 1. Каркас проекта Hints и порты

- [ ] 1.1 Создать проект `src/TransactionJournal.Hints` (net9.0) со ссылкой только на `TransactionJournal.Domain`; включить в solution; объявить порты `IJournalSnapshotReader`, `IMarkSource`, `IHintStore`, `IClock` и каркас прохода (`HintAgentPass`). Проверить: `dotnet build` решения; Hints не ссылается на Application/Infrastructure/Web (`requirement-dependencies-point-inward`).
- [ ] 1.2 Реализовать в Infrastructure адаптеры портов: чтение журнал-снапшота поверх EF, источник марок поверх Bybit-клиента, `IHintStore` с миграцией таблиц подсказок в существующей SQLite; зарегистрировать в composition root Web. Проверить: `dotnet test` зелёный, миграция применяется к копии рабочей базы.

## 2. Снимок корпуса и валидация

- [ ] 2.1 Загрузчик корпуса: чтение каталога (настраиваемый путь, дефолт `rules/`), парсинг YAML, проверка схемы всех карточек за одно чтение, агрегированная `CorpusInvalidException` (включая отсутствующий/пустой каталог), неизменяемый снимок, `retired`-карточки для гашения. Проверить: `requirement-corpus-validity-precondition` — юнит-тесты битой карточки, пустого и отсутствующего каталога.
- [ ] 2.2 Разделение неизвестного и битого: неизвестный ключ `trigger.implementation` и `implementation: null` — карточка валидна, попадает в чек-лист и лог «непокрытых кодом». Проверить: `requirement-corpus-unimplemented-trigger-checklist` — тесты обоих случаев, проход продолжается.

## 3. Машинные триггеры

- [ ] 3.1 Реестр триггеров и каркас чистой функции `(снапшот, марки, карточка) → факты | нет`; пороги только из `thresholds` карточки; вычисления журнала поверх движков Domain (FIFO, сборка, Risk/Profit цели Construction). Проверить: юнит-тесты каркаса на фикстуре одного правила.
- [ ] 3.2 Реализовать 11 ключей v1: `risk-limit-period`, `uncovered-sale-margin`, `profit-target-reached`, `edge-sale-cap`, `roll-time-window`, `roll-threshold`, `atm-decay-window`, `min-straddle-size`, `flat-win-streak`, `unfreeze-profit-ratio`, `synthetic-close-itm`. Проверить: `requirement-engine-v1-trigger-set` — фикстурные e2e на каждый ключ парами «сработало / не сработало» ([#31](https://github.com/menshov-anatoliy/transaction-journal/issues/31)).
- [ ] 3.3 Рендер подсказок: `hintTemplate` + факты; чёткость — императив / префикс «[решение]»; фиксация фактов, тегов источников и as-of при генерации. Проверить: `requirement-engine-clarity-shapes-wording`, `requirement-engine-self-describing-record` — тесты формулировок и неизменности записи после правки карточки.
- [ ] 3.4 Пропуск прохода при недоступности марок с диагностикой; определение субъекта «конструкция»/«журнал» по правилу. Проверить: `requirement-engine-market-unavailable-skips-pass`, `requirement-hint-subject-v1-closed-set`.

## 4. Дедуп и жизненный цикл

- [ ] 4.1 Ключ окна «правило × субъект (+ период для периодных)»: подавление новой подсказки любой записью `new/applied/dismissed`; гашение в `expired` при уходе условия; новое окно при смене периода и при возвращении условия. Проверить: `requirement-hint-dedup-rule-subject-window` — e2e сценариев дедупа (непрерывное условие, dismissed в окне, смена периода, возврат условия).
- [ ] 4.2 Переходы `applied/dismissed` только от человека (UI-команда), `expired` только от агента; терминальность без reopen; `firstSeenAt` при первом показе. Проверить: `requirement-hint-lifecycle-transitions` — тесты переходов и запретов.
- [ ] 4.3 Пограничные гашения: закрытие субъекта на первом проходе; вывод правила (retired/удаление) при загрузке снимка. Проверить: `requirement-hint-expiry-on-subject-close-and-retirement` — e2e обоих сценариев.

## 5. UI подсказок

- [ ] 5.1 Read-модель Hints (индикатор, панель, журнал) поверх `IHintStore`; справочник групп v1 («Риск-режим», «Управление конструкцией», «Фьючерсная нога»). Проверить: `requirement-ui-hint-section-groups` — тесты группировки и скрытия пустых групп.
- [ ] 5.2 Индикатор живых подсказок в строке конструкции; панель «Подсказки» в деталях (живые сверху, история свёрнута, карточка полного состава, кнопки «Применено»/«Отклонено»); та же панель субъекта «журнал» на обзорном экране. Проверить: `requirement-ui-construction-hint-badge`, `requirement-ui-hint-panel-in-construction`, `requirement-ui-portfolio-hint-panel` — экранные тесты.
- [ ] 5.3 Общий read-only журнал подсказок с фильтрами статуса, характера и группы. Проверить: `requirement-ui-hint-log` — тесты фильтров.
- [ ] 5.4 Кнопка ручного запуска прохода: фоновое выполнение, обновление панелей, агрегированная ошибка корпуса в UI. Проверить: `requirement-ui-manual-pass-button` — тесты запуска и ошибки корпуса.

## 6. Сквозная приёмка движка

- [ ] 6.1 Прогнать полный набор фикстурных e2e: все 11 триггеров парами, дедуп-окно, fail-fast корпуса, пограничные гашения, панель показывает факты триггера ([#31](https://github.com/menshov-anatoliy/transaction-journal/issues/31)). Проверить: `dotnet test` зелёный; `openspec validate add-hints-engine --strict`.
- [ ] 6.2 Сверить раскладку с инвариантами `architecture/solution-structure`: Hints → только Domain, адаптеры в Infrastructure, регистрация в Web. Проверить: ревью ссылок проектов.
