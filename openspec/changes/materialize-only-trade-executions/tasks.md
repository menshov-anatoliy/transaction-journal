# Tasks

## 1. Тесты фильтра в материализаторе сделок

- [x] 1.1 В `tests\TransactionJournal.Tests\Materialization\TradeMaterializerTests` добавить тест: сырая запись с `execType: "Funding"`, заполненными количеством и ценой по марк-цене и валидной стороной, не материализуется сделкой — результат пуст, неразрешённых символов и исключений нет. Прогнать `dotnet test --filter TradeMaterializerTests` и убедиться, что новый тест падает на текущем коде. Рядом с тестом — человекочитаемый комментарий и метки `Traceability: openspec:sync/bybit-history#requirement-non-trade-executions-are-not-trades`, `Traceability: openspec:sync/bybit-history#scenario-funding-record-is-not-a-trade`
- [x] 1.2 В том же файле добавить тест: записи с неизвестным типом исполнения (например, `AdlTrade`) и с пустым `execType` не материализуются, не завершают материализацию ошибкой и не порождают предупреждений; соседняя запись `execType: "Trade"` из того же вызова материализуется как прежде. Метки `Traceability: openspec:sync/bybit-history#scenario-unknown-exec-type-is-not-a-trade` с человекочитаемым комментарием
- [x] 1.3 Добавить тест: повторная материализация одного и того же набора Trade- и Funding-записей даёт идентичный результат без дублей и ошибок. Метка `Traceability: openspec:sync/bybit-history#requirement-sync-idempotency` с человекочитаемым комментарием

## 2. Фильтр по типу исполнения в TradeMaterializer

- [x] 2.1 В `src\TransactionJournal\Materialization\TradeMaterializer.cs` перенести разбор payload в `Materialize` до построения сделки: после разбора сравнить `ExecType` с `Trade` без учёта регистра и не-Trade запись пропустить до проверок стороны, цены и количества; повреждённый payload остаётся ошибкой. В XML-комментарии метода и класса описать правило; добавить человекочитаемый комментарий о том, что фандинг и прочие не-Trade события хранятся в сырье, но сделкой не становятся, и метки `Traceability: openspec:sync/bybit-history#requirement-new-records-land-in-inbox`, `Traceability: openspec:sync/bybit-history#requirement-non-trade-executions-are-not-trades`, `Traceability: openspec:sync/bybit-history#requirement-raw-record-storage`
- [x] 2.2 Калибровать payload-хелперы существующих тестов: `ExecutionPayload` в `TradeMaterializerTests`, `ExpiryMaterializerTests`, `JournalMaterializerTests` и аналогичные хелперы доменных, analytics- и UI-тестов, строящие payload исполнений, получают `execType: "Trade"` значением по умолчанию. Прогнать `dotnet test` по проекту тестов целиком и убедиться, что после калибровки и фильтра падений нет
- [x] 2.3 В `TradeMaterializerTests` добавить тест: конфликт двух Trade-записей с одним `execId` по-прежнему роняет материализацию, а Funding-запись с тем же `execId`, что у Trade-записи, дубликата не создаёт. Метка `Traceability: openspec:sync/bybit-history#requirement-sync-idempotency` с человекочитаемым комментарием

## 3. Проекции поверх фильтра

- [ ] 3.1 В `tests\TransactionJournal.Tests\Materialization\JournalMaterializerTests` добавить тест: набор из Trade- и Funding-записей linear-инструмента даёт во «Входящих» и в остатках экспираций только вклад Trade-записей — количество инструмента сходится без фандинга. Метки `Traceability: openspec:sync/bybit-history#scenario-non-trade-records-keep-projections-clean` с человекочитаемым комментарием
- [ ] 3.2 В `tests\TransactionJournal.Tests\Domain\InboxReadModelTests` добавить тест: read-модель «Входящих» на наборе Trade- и Funding-сырых записей возвращает только Trade-сделку. Метки `Traceability: openspec:sync/bybit-history#scenario-funding-record-is-not-a-trade` с человекочитаемым комментарием

## 4. Финальная верификация

- [ ] 4.1 Собрать решение (`dotnet build`) и прогнать весь тестовый проект (`dotnet test`): зелёный прогон без пропущенных падений; проверить rg-рецептом, что каждая новая метка `Traceability: openspec:sync/bybit-history#...` разрешается в `Traceability ID:` дельты `openspec\changes\materialize-only-trade-executions\specs\sync\bybit-history\spec.md`, и что полезные существующие комментарии рядом с изменёнными блоками не удалены
