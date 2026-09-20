namespace TransactionJournal.Data;

/// <summary>
/// Ручная пометка закрытия позиции — пользовательская закрывающая запись
/// для инструментов без биржевых записей закрытия. Применяется в производном
/// слое с количеством, равным остатку на момент применения по хронологии;
/// правка и удаление свободны — пересчёт возвращает позицию в открытое
/// состояние.
// Traceability: openspec:domain/constructions#requirement-position-close-lifecycle
/// Traceability: change:add-core-domain/design#d3
/// </summary>
public sealed class ManualCloseMark
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>Конструкция, внутри которой закрывается позиция.</summary>
	public Construction Construction { get; set; } = null!;

	/// <summary>Внешний ключ конструкции.</summary>
	public long ConstructionId { get; set; }

	/// <summary>Инструмент закрываемой позиции.</summary>
	public required string Symbol { get; set; }

	/// <summary>
	/// Цена закрытия по выбору пользователя; null — не задана, производный слой
	/// подставляет последнюю известную марку инструмента при чтении.
	/// </summary>
	public decimal? Price { get; set; }

	/// <summary>Время пометки: задаёт место записи в хронологии закрывающих записей.</summary>
	public required DateTimeOffset MarkedAt { get; set; }
}
