namespace TransactionJournal.Domain.Data;

/// <summary>
/// Ручная синтетическая связка — персистентные ручные данные логического слоя
/// рядом с оспариванием: пользователь соединяет линейную сделку и встречный
/// опцион одной конструкции без 60-минутного окна распознавания. Связка
/// принадлежит конструкции и умирает вместе с ней: инкрементная сборка её
/// сохраняет, полный пересбор стирает.
/// </summary>
// Ручная связка действует в логическом слое наравне с распознанными:
// действующие связки = распознанные − оспоренные + ручные.
// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
// Персистентная сущность рядом с оспариванием, а не вывод при чтении: ручные
// данные должны переживать инкрементную сборку и стираться пересбором.
// Traceability: change:manual-journal-management/design#d5
public sealed class SyntheticCloseManualLink
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>Конструкция, внутри которой создана связка.</summary>
	public Construction Construction { get; set; } = null!;

	/// <summary>Внешний ключ конструкции — владельца ручной связки.</summary>
	public long ConstructionId { get; set; }

	/// <summary>
	/// Идентификатор линейной сделки журнала. Сделки не материализуются строками
	/// БД, поэтому ссылка строковая: у биржевой сделки это execId, у ручной —
	/// идентификатор строки сырья; единая ссылка обслуживает оба источника.
	/// </summary>
	public required string LinearTradeId { get; set; }

	/// <summary>
	/// Идентификатор встречной опционной сделки журнала — с теми же правилами
	/// идентификации, что у линейной сделки.
	/// </summary>
	public required string OptionTradeId { get; set; }

	/// <summary>Количество связки: не превышает количество ни одной из сделок пары.</summary>
	public required decimal Quantity { get; set; }
}
