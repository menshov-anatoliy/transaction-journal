namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Существующая конструкция, передаваемая сборке как начальное состояние
/// (seed): стабильный ключ БД, имя с признаком ручной фиксации, статус,
/// период жизни, ноги с текущими знаковыми остатками и фьючерсный остаток.
/// Инкрементная сборка читает существующие конструкции как контекст остатков: они участвуют
/// в классификации закрывающих сделок и выборе целей усреднения, а изменяются
/// только в производных атрибутах — имени (пока не зафиксировано вручную)
/// и статусе «открыта»/«закрыта». Полный пересбор вызывает сборку с пустым
/// seed'ом и получает прежнее поведение.
// Traceability: change:refine-construction-assembly/design#d1
/// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#requirement-incremental-inbox-assembly
/// Traceability: change:close-construction-on-all-positions/design#d3
/// </summary>
public sealed record AssemblySeedConstruction
{
	/// <summary>Стабильный ключ конструкции в базе данных.</summary>
	public required long Id { get; init; }

	/// <summary>Текущее имя конструкции в базе.</summary>
	public required string Name { get; init; }

	/// <summary>Признак вручную зафиксированного имени: автогенерация имени для конструкции отключена.</summary>
	public bool NameIsManual { get; init; }

	/// <summary>Текущий статус конструкции в базе; «архив» сборкой не меняется.</summary>
	public ConstructionStatus Status { get; init; }

	/// <summary>Начало периода жизни конструкции, мс Unix-эпохи.</summary>
	public required long OpenedAtMs { get; init; }

	/// <summary>Конец периода жизни, мс Unix-эпохи; null — конструкция жива.</summary>
	public long? ClosedAtMs { get; init; }

	/// <summary>
	/// Знаковый фьючерсный остаток конструкции (по одному на актив — конструкция
	/// одноактивна): агрегат привязанных linear-сделок и закрывающих записей
	/// читающего слоя позиций, включая ручные пометки между прогонами. Участвует
	/// в статусе и периоде жизни конструкции наравне с опционными ногами.
	/// </summary>
	// Seed несёт фьючерсный остаток: инкремент пересчитывает статус существующей
	// конструкции по всем её позициям — ненулевой остаток держит конструкцию
	// открытой после гибели опционного прикрытия.
	// Traceability: change:close-construction-on-all-positions/design#d3
	// Traceability: change:close-construction-on-all-positions/specs/domain/construction-assembly/spec#requirement-status-follows-all-positions
	public decimal FuturesQuantity { get; init; }

	/// <summary>
	/// Время последнего события позиций конструкции — привязанного исполнения
	/// либо применённой закрывающей записи read-модели, мс Unix-эпохи; null —
	/// события не выведены. База непрерывности момента закрытия: закрытие
	/// не может наступать раньше последнего события позиций.
	/// </summary>
	// Traceability: change:close-construction-on-all-positions/design#d3
	public long? LastPositionEventAtMs { get; init; }

	/// <summary>Ноги конструкции с текущими знаковыми остатками.</summary>
	public required IReadOnlyList<AssemblySeedLeg> Legs { get; init; }
}

/// <summary>
/// Нога существующей конструкции в seed'е: символ опциона и агрегированный
/// знаковый остаток (сделки плюс закрывающие записи). Свойства ноги — доска,
/// страйк, тип — разбираются из символа тем же парсером, что и в группировке
/// исполнений.
/// </summary>
public sealed record AssemblySeedLeg
{
	/// <summary>Символ опциона, например ETH-25SEP26-2100-C-USDT.</summary>
	public required string Symbol { get; init; }

	/// <summary>Текущий знаковый остаток ноги.</summary>
	public required decimal Quantity { get; init; }
}
