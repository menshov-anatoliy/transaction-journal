namespace TransactionJournal.Data;

/// <summary>
/// Комментарий позиции по ключу «конструкция × инструмент». Позиция —
/// производная и собственной строки не имеет, поэтому комментарий хранится
/// по стабильному ключу и переживает закрытие, переоткрытие и пересчёты
/// остатка.
// Traceability: openspec:domain/constructions#requirement-entity-comments
/// Traceability: change:add-core-domain/design#d2
/// </summary>
public sealed class PositionComment
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>Конструкция, внутри которой оставлен комментарий позиции.</summary>
	public Construction Construction { get; set; } = null!;

	/// <summary>Внешний ключ конструкции.</summary>
	public long ConstructionId { get; set; }

	/// <summary>Инструмент позиции, к которому оставлен комментарий.</summary>
	public required string Symbol { get; set; }

	/// <summary>Текст комментария позиции.</summary>
	public required string Text { get; set; }
}
