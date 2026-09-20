namespace TransactionJournal.Data;

/// <summary>
/// Пользовательские данные сделки по стабильному ключу execId: привязка
/// к конструкции и комментарий. Отсутствие привязки означает, что сделка
/// находится во «Входящих»; запись переживает перепривязки и пересчёты.
// Traceability: openspec:domain/constructions#requirement-trade-single-binding
/// Traceability: change:add-core-domain/design#d1
/// </summary>
public sealed class TradeUserdata
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>
	/// Биржевой идентификатор исполнения — уникальный ключ сделки.
	/// Одна строка на execId гарантирует единственную принадлежность сделки.
	/// </summary>
	public required string ExecId { get; set; }

	/// <summary>Конструкция, к которой привязана сделка; null — сделка во «Входящих».</summary>
	public Construction? Construction { get; set; }

	/// <summary>Внешний ключ привязки; null, пока сделка находится во «Входящих».</summary>
	public long? ConstructionId { get; set; }

	/// <summary>Свободный комментарий пользователя к сделке.</summary>
	public string? Comment { get; set; }
}
