namespace TransactionJournal.Sync;

/// <summary>
/// Одно окно истории исполнения для прохода: категория, границы времени и необязательный
/// фильтр базового актива. Длительность окна ограничена семью днями — предел эндпоинта
/// execution/list, поэтому перебор истории назад выполняется цепочкой таких окон.
/// Traceability: change:add-bybit-sync/design#d4
/// </summary>
public sealed record ExecutionWindow
{
	/// <summary>Торговая категория окна: linear или option.</summary>
	public required string Category { get; init; }

	/// <summary>
	/// Базовый актив фильтра окна; null — окно проходит категорию целиком. Опционная доска
	/// без явного baseCoin отдаёт записи только одного актива, поэтому не-BTC активы
	/// читаются отдельными окнами с заданным фильтром.
	/// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
	/// </summary>
	public string? BaseCoin { get; init; }

	/// <summary>Начало окна, мс Unix-эпохи.</summary>
	public long StartMs { get; init; }

	/// <summary>Конец окна, мс Unix-эпохи; обязан быть позже начала.</summary>
	public long EndMs { get; init; }
}
