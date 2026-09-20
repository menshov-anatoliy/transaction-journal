namespace TransactionJournal.Data;

/// <summary>
/// Необработанная спецификация инструмента из публичного эндпоинта Bybit
/// GET /v5/market/instruments-info. Канонические свойства опциона
/// (тип, базовый актив, deliveryTime) берутся из этого справочника, а не из строки символа.
// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
/// </summary>
public sealed class RawInstrument
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>Символ инструмента; уникален и служит идемпотентным ключом вставки.</summary>
	public required string Symbol { get; set; }

	/// <summary>Торговая категория инструмента: linear или option.</summary>
	public required string Category { get; set; }

	/// <summary>Сырой JSON спецификации инструмента в неизменном виде, как отдала биржа.</summary>
	public required string PayloadJson { get; set; }

	/// <summary>Момент загрузки записи синхронизацией.</summary>
	public required DateTimeOffset FetchedAt { get; set; }
}
