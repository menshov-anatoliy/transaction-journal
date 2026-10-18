namespace TransactionJournal.Application.ManualTrades;

/// <summary>
/// Черновик ручной сделки — данные формы ручного ввода до проверки и сохранения.
/// Привязка <see cref="ConstructionId"/> применяется только при создании:
/// null означает «Входящие», заданное значение — немедленное попадание сделки
/// в указанную конструкцию; при правке привязка сделки не меняется — перенос
/// выполняют существующие команды привязки.
/// </summary>
public sealed record ManualTradeDraft
{
	/// <summary>Время исполнения сделки, заданное пользователем.</summary>
	public required DateTimeOffset ExecutedAt { get; init; }

	/// <summary>Инструмент сделки, например BTC-29DEC23-45000-C или BTCUSDT.</summary>
	public required string Symbol { get; init; }

	/// <summary>Сторона сделки биржевым значением: Buy или Sell.</summary>
	public required string Side { get; init; }

	/// <summary>Количество сделки: положительное число; направление задаёт сторона.</summary>
	public required decimal Quantity { get; init; }

	/// <summary>Цена сделки: положительное число.</summary>
	public required decimal Price { get; init; }

	/// <summary>Комиссия сделки в USDT: неотрицательная, ноль — комиссии нет.</summary>
	public decimal FeeUsdt { get; init; }

	/// <summary>Конструкция для немедленной привязки при создании; null — во «Входящие».</summary>
	public long? ConstructionId { get; init; }
}
