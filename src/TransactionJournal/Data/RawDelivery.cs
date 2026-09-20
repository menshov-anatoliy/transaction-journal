namespace TransactionJournal.Data;

/// <summary>
/// Необработанная delivery-запись экспирации из Bybit V5 API (GET /v5/asset/delivery-record).
/// Хранится целиком в JSON и служит единственным источником закрывающих записей экспираций.
// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
/// </summary>
public sealed class RawDelivery
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>Инструмент delivery-записи (символ опциона или датированного фьючерса).</summary>
	public required string Symbol { get; set; }

	/// <summary>
	/// Время delivery в мс Unix-эпохи. Вместе с символом образует идемпотентный ключ вставки:
	/// повторная загрузка известной записи не создаёт дублей.
	/// </summary>
	public long DeliveryTimeMs { get; set; }

	/// <summary>Торговая категория записи: linear или option.</summary>
	public required string Category { get; set; }

	/// <summary>Сырой JSON delivery-записи в неизменном виде, как отдала биржа.</summary>
	public required string PayloadJson { get; set; }

	/// <summary>Момент загрузки записи синхронизацией.</summary>
	public required DateTimeOffset FetchedAt { get; set; }
}
