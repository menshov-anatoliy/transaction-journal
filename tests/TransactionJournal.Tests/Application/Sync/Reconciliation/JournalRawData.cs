using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
namespace TransactionJournal.Tests.Application.Sync.Reconciliation;

/// <summary>
/// Атрибуты сопоставления сырой записи исполнения, разобранные из PayloadJson:
/// именно они участвуют в матчинге со строками выгрузки.
/// Traceability: openspec:sync/bybit-statement-reconciliation#requirement-trade-rows-match-executions
/// </summary>
/// <param name="Id">Суррогатный ключ строки RawExecutions для локализации в отчёте.</param>
/// <param name="ExecId">Биржевой идентификатор исполнения для локализации в отчёте.</param>
/// <param name="Symbol">Инструмент записи.</param>
/// <param name="Side">Сторона записи, приведённая к верхнему регистру.</param>
/// <param name="ExecType">Тип исполнения биржи (execType): Trade, Funding и прочие.</param>
/// <param name="Quantity">Исполненное количество (execQty) — размер фактического заполнения, как в строке выгрузки.</param>
/// <param name="Price">Цена исполнения (execPrice).</param>
/// <param name="Fee">Комиссия исполнения (execFee); null, если биржа не раскрыла значение.</param>
/// <param name="TimeUtc">Время исполнения в UTC.</param>
public sealed record JournalExecution(
	long Id,
	string ExecId,
	string Symbol,
	string Side,
	string ExecType,
	decimal? Quantity,
	decimal? Price,
	decimal? Fee,
	DateTime TimeUtc);

/// <summary>
/// Атрибуты сопоставления сырой delivery-записи, разобранные из PayloadJson.
/// Traceability: openspec:sync/bybit-statement-reconciliation#requirement-delivery-rows-match-deliveries
/// </summary>
/// <param name="Id">Суррогатный ключ строки RawDeliveries для локализации в отчёте.</param>
/// <param name="Symbol">Инструмент delivery-записи.</param>
/// <param name="Quantity">Размер позиции (position из PayloadJson).</param>
/// <param name="TimeUtc">Время delivery в UTC.</param>
public sealed record JournalDelivery(
	long Id,
	string Symbol,
	decimal? Quantity,
	DateTime TimeUtc);

/// <summary>
/// Снимок сырых записей журнала в границах диапазона дат строк выгрузки
/// вместе с количеством записей, исключённых по диапазону: они не сверяются,
/// но обязаны быть видны в сводке отчёта.
/// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-executions-outside-range-excluded
/// </summary>
/// <param name="Executions">Записи исполнения в диапазоне.</param>
/// <param name="Deliveries">Delivery-записи в диапазоне.</param>
/// <param name="FromMsInclusive">Нижняя граница диапазона, мс с эпохи Unix, включительно.</param>
/// <param name="ToMsInclusive">Верхняя граница диапазона, мс с эпохи Unix, включительно.</param>
/// <param name="ExecutionsOutsideRange">Количество записей исполнения вне диапазона.</param>
/// <param name="DeliveriesOutsideRange">Количество delivery-записей вне диапазона.</param>
public sealed record JournalRawData(
	IReadOnlyList<JournalExecution> Executions,
	IReadOnlyList<JournalDelivery> Deliveries,
	long FromMsInclusive,
	long ToMsInclusive,
	int ExecutionsOutsideRange,
	int DeliveriesOutsideRange);
