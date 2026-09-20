namespace TransactionJournal.Data;

/// <summary>
/// Необработанная запись исполнения сделки из Bybit V5 API.
/// Биржевая запись хранится целиком (JSON) вместе с идентификатором источника
/// и временем загрузки, чтобы доменные представления можно было пересобрать
/// без повторного обращения к API.
// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
/// </summary>
public sealed class RawExecution
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>
	/// Биржевой идентификатор исполнения. Уникален и служит идемпотентным ключом вставки:
	/// повторная загрузка известной записи не создаёт дублей.
	/// </summary>
	public required string ExecId { get; set; }

	/// <summary>Торговая категория записи: linear или option.</summary>
	public required string Category { get; set; }

	/// <summary>Инструмент записи (например, BTC-27DEC24-2800-C или BTCUSDT).</summary>
	public required string Symbol { get; set; }

	/// <summary>Время исполнения в мс Unix-эпохи; продвигается из сырья для проходов окнами.</summary>
	public long ExecTimeMs { get; set; }

	/// <summary>Сырой JSON записи исполнения в неизменном виде, как отдала биржа.</summary>
	public required string PayloadJson { get; set; }

	/// <summary>Момент загрузки записи синхронизацией.</summary>
	public required DateTimeOffset FetchedAt { get; set; }
}
