namespace TransactionJournal.Domain;

/// <summary>
/// Отказ валидации ручной синтетической связки: сделки из разных конструкций,
/// страйк или экспирация встречного опциона не совпадают с закрываемой ногой,
/// количество не согласуется с одной из сделок либо сделка не найдена
/// в конструкции. Сообщение человекочитаемо и годится для показа пользователю
/// без дополнительной обработки.
/// </summary>
// Окно времени между сделками ручной связки правилом не проверяется:
// отказ возможен только по перечисленным причинам.
// Traceability: openspec:domain/synthetic-close#scenario-manual-link-rejected-on-mismatch
public sealed class SyntheticCloseManualLinkValidationException : Exception
{
	/// <summary>Создаёт отказ валидации с человекочитаемым описанием причины.</summary>
	/// <param name="message">Человекочитаемое описание нарушенного правила.</param>
	public SyntheticCloseManualLinkValidationException(string message)
		: base(message)
	{
	}
}
