namespace TransactionJournal.Domain;

/// <summary>
/// Вид унифицированной закрывающей записи в потоке позиций: биржевые записи
/// экспираций из синхронизации либо ручная пометка пользователя. Ручная пометка —
/// fallback-средство для инструментов без биржевых записей закрытия.
// Traceability: openspec:domain/constructions#requirement-position-close-lifecycle
/// </summary>
public enum PositionClosingKind
{
	/// <summary>Закрытие delivery ITM-опциона из биржевой записи синхронизации.</summary>
	Delivery,

	/// <summary>Автоматическое закрытие OTM-экспирации по нулевой цене из синхронизации.</summary>
	OtmExpiry,

	/// <summary>Ручная пометка закрытия пользователя — пользовательская закрывающая запись.</summary>
	ManualMark,
}

/// <summary>
/// Закрывающая запись единого потока позиций: delivery/OTM-записи экспираций
/// из синхронизации и ручные пометки пользователя, влитые в одну хронологию.
/// Запись производна и пересчитывается при каждом чтении; у ручной пометки
/// количество равно остатку на момент применения по хронологии.
// Traceability: openspec:domain/constructions#requirement-position-close-lifecycle
// Traceability: change:add-core-domain/design#d3
/// </summary>
public sealed record PositionClosingEntry
{
	/// <summary>Конструкция, внутри которой запись закрывает позицию.</summary>
	public required long ConstructionId { get; init; }

	/// <summary>Инструмент закрываемой позиции.</summary>
	public required string Symbol { get; init; }

	/// <summary>Вид записи: delivery, OTM-экспирация или ручная пометка.</summary>
	public required PositionClosingKind Kind { get; init; }

	/// <summary>Момент закрытия: время биржевой записи либо время ручной пометки.</summary>
	public required DateTimeOffset ClosedAt { get; init; }

	/// <summary>
	/// Знаковое количество, обнуляющее остаток на момент применения:
	/// противоположно знаку закрываемого остатка.
	/// </summary>
	public required decimal Quantity { get; init; }

	/// <summary>
	/// Эффективная цена закрытия: внутренняя стоимость delivery, ноль OTM-экспирации,
	/// цена пользователя у ручной пометки либо последняя известная марка, когда цена
	/// не задана. null — марка инструмента пока неизвестна.
	/// </summary>
	public required decimal? Price { get; init; }

	/// <summary>Ключ источника: «symbol|deliveryTimeMs» биржевой записи либо «manual:{id}» ручной пометки.</summary>
	public required string SourceKey { get; init; }
}
