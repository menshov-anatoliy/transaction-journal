namespace TransactionJournal.Tests.Sync.Reconciliation;

/// <summary>
/// Источник строки выгрузки: UTA-файл торговых изменений или fund-файл операций фонда.
/// </summary>
public enum StatementFileKind
{
	/// <summary>UTA-файл AssetChangeDetails: TRADE, SETTLEMENT, DELIVERY, TRANSFER_IN/OUT.</summary>
	Uta,

	/// <summary>Fund-файл AssetChangeDetails: Earn, Deposit, Withdraw, Transfer in/out.</summary>
	Fund,
}

/// <summary>
/// Категория строки выгрузки для сверки: сверяемая с исполнениями, сверяемая
/// с delivery-записями, вне области синхронизации или неизвестный сверке тип.
/// </summary>
public enum StatementRowCategory
{
	/// <summary>Сделка: сверяется с сырыми записями исполнения журнала.</summary>
	Trade,

	/// <summary>Экспирация: сверяется с сырыми delivery-записями журнала.</summary>
	Delivery,

	/// <summary>Вне области синхронизации: журнал такие строки не загружает.</summary>
	OutOfScope,

	/// <summary>Неизвестный тип строки: сверка его не знает и обязана показать явно.</summary>
	Unknown,
}

/// <summary>
/// Одна строка CSV-выгрузки Bybit AssetChangeDetails после разбора.
/// Маркер «--» выгрузки трактуется как отсутствие значения и представляется null.
/// </summary>
public sealed class StatementRow
{
	/// <summary>Какой файл выгрузки породил строку.</summary>
	public required StatementFileKind FileKind { get; init; }

	/// <summary>Имя файла-части выгрузки для локализации строки в отчёте.</summary>
	public required string SourceFile { get; init; }

	/// <summary>Номер строки в файле для локализации.</summary>
	public required int LineNumber { get; init; }

	/// <summary>Тип строки как в выгрузке: TRADE, SETTLEMENT, Earn и так далее.</summary>
	public required string Type { get; init; }

	/// <summary>Расчётная валюта строки: UTA-колонка Currency или fund-колонка Coin.</summary>
	public required string Currency { get; init; }

	/// <summary>Инструмент UTA-файла (Contract); в fund-файле инструмента нет.</summary>
	public string? Contract { get; init; }

	/// <summary>Сторона UTA-файла (Direction: BUY/SELL); в fund-файле стороны нет.</summary>
	public string? Direction { get; init; }

	/// <summary>Количество; null, если в выгрузке стоит «--».</summary>
	public decimal? Quantity { get; init; }

	/// <summary>Цена исполнения (Filled Price); null, если в выгрузке стоит «--».</summary>
	public decimal? FilledPrice { get; init; }

	/// <summary>Комиссия (Fee Paid); null, если в выгрузке стоит «--».</summary>
	public decimal? FeePaid { get; init; }

	/// <summary>Время строки в UTC.</summary>
	public required DateTime TimeUtc { get; init; }
}
