namespace TransactionJournal.Data;

/// <summary>
/// Состояние синхронизации по торговой категории: водяные знаки успешных синков
/// и достигнутая граница backfill. Отсутствие водяного знака означает, что для
/// категории ещё не было успешного синка — очередной запуск выполнит backfill.
// Traceability: openspec:sync/bybit-history#requirement-manual-sync-modes
/// </summary>
public sealed class SyncState
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>Торговая категория состояния: linear или option; уникальна.</summary>
	public required string Category { get; set; }

	/// <summary>
	/// Водяной знак последнего успешного прохода исполнений: самое позднее время
	/// execTime (мс), покрытое успешным синком. Инкрементальная догрузка стартует
	/// от этой отметки минус перекрытие назад.
	/// </summary>
	public long? ExecWatermarkMs { get; set; }

	/// <summary>Водяной знак последнего успешного delivery-прохода (deliveryTime, мс).</summary>
	public long? DeliveryWatermarkMs { get; set; }

	/// <summary>
	/// Самая ранняя дата записи, достигнутая backfill-ом категории. Полные проходы
	/// не запрашивают окна старше этой границы.
	// Traceability: openspec:sync/bybit-history#scenario-backfill-depth-boundary
	/// </summary>
	public long? BackfillBoundaryMs { get; set; }

	/// <summary>Момент последнего успешного завершения синка категории.</summary>
	public DateTimeOffset? LastSuccessAt { get; set; }
}
