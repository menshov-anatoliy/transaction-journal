using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
namespace TransactionJournal.Tests.Application.Sync.Reconciliation;

/// <summary>Ключевой атрибут торговой пары, по которому фиксируется расхождение.</summary>
public enum TradeField
{
	/// <summary>Количество.</summary>
	Quantity,

	/// <summary>Цена исполнения.</summary>
	Price,

	/// <summary>Комиссия по модулю.</summary>
	Fee,
}

/// <summary>Сопоставленная пара «строка TRADE выгрузки — запись исполнения журнала».</summary>
/// <param name="Row">Строка выгрузки.</param>
/// <param name="Execution">Запись исполнения.</param>
public sealed record TradeMatchPair(StatementRow Row, JournalExecution Execution);

/// <summary>Расхождение одного атрибута сопоставленной пары.</summary>
/// <param name="Field">Какой атрибут разошёлся.</param>
/// <param name="StatementValue">Значение строки выгрузки.</param>
/// <param name="JournalValue">Значение записи журнала.</param>
public sealed record TradeFieldMismatch(TradeField Field, string StatementValue, string JournalValue);

/// <summary>
/// Сопоставленная пара с расходящимися атрибутами: расхождение количества,
/// цены или комиссии не превращается в пару фиктивных «отсутствий».
/// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-trade-attribute-mismatch
/// </summary>
/// <param name="Row">Строка выгрузки.</param>
/// <param name="Execution">Запись исполнения.</param>
/// <param name="Fields">Перечень разошедшихся атрибутов со значениями обеих сторон.</param>
public sealed record TradeAttributeMismatch(
	StatementRow Row,
	JournalExecution Execution,
	IReadOnlyList<TradeFieldMismatch> Fields);

/// <summary>
/// Агрегированная сверка комиссий по инструменту: постричная ошибка не ставится,
/// когда поле комиссии записи неоднозначно, а суммы выводятся в отчёт.
/// Traceability: change:reconcile-bybit-statement/design#d5
/// </summary>
/// <param name="Symbol">Инструмент.</param>
/// <param name="StatementAbsFeeSum">Сумма модулей комиссий строк выгрузки сопоставленных пар.</param>
/// <param name="JournalAbsFeeSum">Сумма модулей комиссий записей журнала сопоставленных пар.</param>
/// <param name="UncomparedPairs">Количество пар, для которых построчная сверка комиссии невозможна.</param>
public sealed record FeeAggregate(
	string Symbol,
	decimal StatementAbsFeeSum,
	decimal JournalAbsFeeSum,
	int UncomparedPairs);

/// <summary>
/// Итог мультимножественного сопоставления торговых строк выгрузки с записями
/// исполнения журнала.
/// Traceability: openspec:sync/bybit-statement-reconciliation#requirement-trade-rows-match-executions
/// </summary>
/// <param name="Matched">Точные пары этапа 1 без расхождений атрибутов.</param>
/// <param name="AttributeMismatches">Пары этапа 2 с расхождением атрибутов.</param>
/// <param name="MissingInJournal">Строки выгрузки без пары — «отсутствует в журнале».</param>
/// <param name="MissingInStatement">Записи журнала без пары — «отсутствует в выгрузке».</param>
/// <param name="FeeAggregates">Агрегаты комиссий по инструментам с неоднозначным полем комиссии.</param>
public sealed record TradeReconciliationResult(
	IReadOnlyList<TradeMatchPair> Matched,
	IReadOnlyList<TradeAttributeMismatch> AttributeMismatches,
	IReadOnlyList<StatementRow> MissingInJournal,
	IReadOnlyList<JournalExecution> MissingInStatement,
	IReadOnlyList<FeeAggregate> FeeAggregates);

/// <summary>Сопоставленная пара «строка DELIVERY выгрузки — delivery-запись журнала».</summary>
/// <param name="Row">Строка выгрузки.</param>
/// <param name="Delivery">Delivery-запись.</param>
public sealed record DeliveryMatchPair(StatementRow Row, JournalDelivery Delivery);

/// <summary>
/// Сопоставленная пара delivery с расходящимся количеством; комиссии и цены
/// для DELIVERY не сверяются — в выгрузке они нулевые для этого типа.
/// Traceability: openspec:sync/bybit-statement-reconciliation#requirement-delivery-rows-match-deliveries
/// </summary>
/// <param name="Row">Строка выгрузки.</param>
/// <param name="Delivery">Delivery-запись.</param>
/// <param name="StatementValue">Количество строки выгрузки.</param>
/// <param name="JournalValue">Количество delivery-записи.</param>
public sealed record DeliveryQuantityMismatch(
	StatementRow Row,
	JournalDelivery Delivery,
	string StatementValue,
	string JournalValue);

/// <summary>
/// Итог сопоставления delivery-строк выгрузки с delivery-записями журнала.
/// Traceability: openspec:sync/bybit-statement-reconciliation#requirement-delivery-rows-match-deliveries
/// </summary>
/// <param name="Matched">Пары по инструменту в допуске времени.</param>
/// <param name="QuantityMismatches">Пары с расходящимся количеством.</param>
/// <param name="MissingInJournal">Строки выгрузки без пары — «отсутствует в журнале».</param>
/// <param name="MissingInStatement">Delivery-записи без пары — «отсутствует в выгрузке».</param>
public sealed record DeliveryReconciliationResult(
	IReadOnlyList<DeliveryMatchPair> Matched,
	IReadOnlyList<DeliveryQuantityMismatch> QuantityMismatches,
	IReadOnlyList<StatementRow> MissingInJournal,
	IReadOnlyList<JournalDelivery> MissingInStatement);
