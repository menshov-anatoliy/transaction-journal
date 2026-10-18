namespace TransactionJournal.Domain;

/// <summary>
/// Отказ валидации ручной сделки: дата в будущем, количество или цена
/// не положительны, комиссия отрицательна либо сторона не распознана.
/// Сообщение человекочитаемо и годится для показа пользователю без
/// дополнительной обработки.
/// </summary>
public sealed class ManualTradeValidationException : Exception
{
	/// <summary>Создаёт отказ валидации с человекочитаемым описанием причины.</summary>
	/// <param name="message">Человекочитаемое описание нарушенного правила.</param>
	public ManualTradeValidationException(string message)
		: base(message)
	{
	}
}
