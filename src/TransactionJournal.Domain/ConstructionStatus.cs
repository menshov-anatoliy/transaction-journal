namespace TransactionJournal.Domain;

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
