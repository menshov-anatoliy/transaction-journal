namespace TransactionJournal.Data;

/// <summary>
/// Ручной статус конструкции. «Открыта» — значение по умолчанию для новых
/// конструкций; «архив» скрывает конструкцию из активных списков, сохраняя
/// её и историю в аналитике. Автоматической смены статуса нет.
// Traceability: change:add-core-domain/design#d4
/// </summary>
public enum ConstructionStatus
{
	/// <summary>Конструкция активна и видна в активных списках.</summary>
	Open,

	/// <summary>Конструкция закрыта пользователем, но не скрыта из списков.</summary>
	Closed,

	/// <summary>Конструкция скрыта из активных списков и остаётся только в аналитике.</summary>
	Archived,
}

/// <summary>
/// Именованное логическое объединение сделок вокруг одной торговой цели:
/// выделенный капитал, ручной статус и комментарий. Единственный мутируемый
/// агрегат домена — позиции и результаты выводятся из сделок при чтении.
// Traceability: openspec:domain/constructions#requirement-construction-management
/// Traceability: change:add-core-domain/design#d1
/// </summary>
public sealed class Construction
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>Имя конструкции, свободно переименовывается без влияния на прочие данные.</summary>
	public required string Name { get; set; }

	/// <summary>Ручной статус конструкции; по умолчанию новые конструкции открываются со статусом «открыта».</summary>
	public ConstructionStatus Status { get; set; }

	/// <summary>
	/// Выделенный капитал в USDT — текущее значение без истории изменений;
	/// база для расчёта процентов результата конструкции.
	/// </summary>
	public decimal AllocatedCapitalUsdt { get; set; }

	/// <summary>Свободный комментарий пользователя к конструкции.</summary>
	public string? Comment { get; set; }
}
