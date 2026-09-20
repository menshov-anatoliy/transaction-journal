namespace TransactionJournal.Materialization;

/// <summary>
/// Ошибка сверки символа опциона со справочником инструментов: символ не разобран,
/// неизвестен справочнику или расходится с канонической спецификацией биржи.
// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
/// </summary>
public sealed class InstrumentResolveException : Exception
{
	/// <summary>Создаёт ошибку сверки с причиной и описанием.</summary>
	/// <param name="reason">Причина неудачи сверки.</param>
	/// <param name="symbol">Символ, который не удалось сверить.</param>
	/// <param name="message">Человекочитаемое описание ошибки.</param>
	public InstrumentResolveException(InstrumentResolveFailureReason reason, string symbol, string message)
		: base(message)
	{
		Reason = reason;
		Symbol = symbol;
	}

	/// <summary>Причина неудачи сверки.</summary>
	public InstrumentResolveFailureReason Reason { get; }

	/// <summary>Символ, который не удалось сверить со справочником.</summary>
	public string Symbol { get; }
}
