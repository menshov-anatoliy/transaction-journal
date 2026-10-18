namespace TransactionJournal.Application.ManualTrades;

/// <summary>
/// Отказ разрешения символа ручной сделки: биржа недоступна, ответила ошибкой или
/// не знает символ. Сообщение человекочитаемо и годится для показа пользователю
/// без дополнительной обработки; создание сделки с этим символом отменяется.
/// Traceability: change:manual-journal-management/design#d3
/// </summary>
public sealed class ManualSymbolResolutionException : Exception
{
	/// <summary>Создаёт отказ разрешения символа с человекочитаемым описанием.</summary>
	/// <param name="symbol">Символ, который не удалось разрешить.</param>
	/// <param name="message">Человекочитаемое описание причины отказа.</param>
	/// <param name="innerException">Исходная ошибка сети или биржи; null — отказа биржи не было.</param>
	public ManualSymbolResolutionException(string symbol, string message, Exception? innerException = null)
		: base(message, innerException)
	{
		Symbol = symbol;
	}

	/// <summary>Символ, который не удалось разрешить через справочник.</summary>
	public string Symbol { get; }
}
