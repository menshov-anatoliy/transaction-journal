using TransactionJournal.Data;

namespace TransactionJournal.Sync;

/// <summary>
/// Снимок всех сырых записей хранилища журнала для полного переразбора доменных
/// представлений: справочник инструментов, записи исполнения и delivery-записи.
/// Снимок достаточен для материализации без сетевых запросов к бирже.
// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
/// </summary>
public sealed record JournalRawSnapshot
{
	/// <summary>Сырые спецификации инструментов справочника.</summary>
	public required IReadOnlyList<RawInstrument> Instruments { get; init; }

	/// <summary>Сырые записи исполнения сделок.</summary>
	public required IReadOnlyList<RawExecution> Executions { get; init; }

	/// <summary>Сырые delivery-записи экспираций.</summary>
	public required IReadOnlyList<RawDelivery> Deliveries { get; init; }
}
