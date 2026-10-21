# Tasks

## 1. Макет design.pen — правка прежде кода (ADR-0011)

- [ ] 1.1 Через MCP Pen добавить в `design.pen` мастер-паттерн признака закрытия синтетикой на строке опционной ноги по образцу распада остатка линейной ноги (Body #2: главная строка остатка, вторичная 9.5px `text-text-muted` «закрыто синтетикой X · открыто Y»), оба состояния — есть/нет действующих связок; проверить узлы макета и токенно-компонентное соответствие

## 2. Бэкенд: величины признака в строках позиций

- [ ] 2.1 Дополнить `ConstructionPositionRow` парой `SyntheticClosedQuantity`/`SyntheticOpenQuantity` (nullable) и проверить сборку решения `dotnet build`
- [ ] 2.2 В `ConstructionDetailReadModel` в прогоне связок (неоспоренные распознанные + применимые ручные) собрать карту сумм по `ClosedLegSymbol` рядом с `quantityByLinearSymbol` и заполнить пару в строках позиций: X — сумма, Y = `Residual` − X, без связок `null`/`null`, без клэмпа; проверить `dotnet build`
- [ ] 2.3 Тесты read model (`ConstructionDetailReadModelTests`): сумма действующих связок по ноге, исключение оспоренной, отрицательное «открыто» без клэмпа, признак у закрытой позиции, отсутствие связок → `null`/`null`, неизменность инструментальных метрик строки; проверить `dotnet test --filter ConstructionDetailReadModel`
- [ ] 2.4 Прокинуть пару в DTO карточки (`syntheticClosedQuantity`/`syntheticOpenQuantity`) и дополнить тесты API карточки; проверить `dotnet test --filter ConstructionCard`

## 3. Frontend по обновлённому макету

- [ ] 3.1 Дополнить типы `ConstructionCardPosition` в `frontend/src/lib/api/construction-card.ts` парой nullable полей; проверить `npm run build`
- [ ] 3.2 В `positions-table.tsx` отрисовать на опционной ноге split `data-slot="option-leg-synthetic-split"` с тултипом из спеки UI (перенос по макету задачи 1.1) и дополнить тесты таблицы позиций; проверить `npm test -- positions-table`
- [ ] 3.3 Скриншот-сверка живой страницы (localhost) с макетом `design.pen` через MCP Pen — обе строки: опционная нога со связкой и нога без связок; зафиксировать результат сверки

## 4. Глоссарий и валидация change

- [ ] 4.1 Зафиксировать в `GLOSSARY.md` термин «Признак закрытия синтетикой» с avoid-списком («ручная пометка закрытия» как синоним) и ссылками на openspec-фрагменты; проверить, что термин и ссылки читаются в файле
- [ ] 4.2 Прогнать `openspec validate --change add-synthetic-close-leg-annotation` и убедиться в отсутствии ошибок
