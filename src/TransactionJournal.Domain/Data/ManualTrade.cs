namespace TransactionJournal.Domain.Data;

/// <summary>
/// Ручная сделка — сырьё журнала, внесённое пользователем без синхронизации
/// с биржей. Хранится отдельной таблицей сырья и материализуется в общий
/// журнал наравне с <see cref="RawExecution"/>, поэтому переживает полный
/// пересбор по построению: пересбор стирает конструкции, а не сырьё.
/// </summary>
// Сделка — полноправная запись журнала с признаком источника «ручной ввод»:
// попадает во «Входящие» либо сразу в указанную конструкцию.
// Traceability: openspec:domain/manual-trades#requirement-manual-trade-entry
// Отдельная таблица сырья вместо расширения RawExecutions: обязательности
// полей биржевой и ручной записи различны, смешение ломает идемпотентность
// синхронизации.
// Traceability: change:manual-journal-management/design#d1
public sealed class ManualTrade
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>Время исполнения сделки, заданное пользователем.</summary>
	public required DateTimeOffset ExecutedAt { get; set; }

	/// <summary>Инструмент сделки (например, BTC-27DEC24-2800-C или BTCUSDT).</summary>
	public required string Symbol { get; set; }

	/// <summary>Сторона сделки биржевым значением: Buy или Sell.</summary>
	public required string Side { get; set; }

	/// <summary>Количество сделки: положительное число; направление задаёт сторона.</summary>
	public required decimal Quantity { get; set; }

	/// <summary>Цена сделки, заданная пользователем.</summary>
	public required decimal Price { get; set; }

	/// <summary>Комиссия сделки в USDT: неотрицательная, ноль означает отсутствие комиссии.</summary>
	public decimal FeeUsdt { get; set; }

	/// <summary>Внешний ключ конструкции; null — сделка находится во «Входящих».</summary>
	public long? ConstructionId { get; set; }

	/// <summary>Конструкция, к которой привязана сделка; null у сделок «Входящих».</summary>
	public Construction? Construction { get; set; }

	/// <summary>Момент внесения записи.</summary>
	public required DateTimeOffset CreatedAt { get; set; }

	/// <summary>Момент последней правки записи; обновляется сервисом при изменении.</summary>
	public required DateTimeOffset UpdatedAt { get; set; }
}
