namespace TransactionJournal.Materialization;

/// <summary>
/// Ошибка материализации закрывающих записей экспирации: повреждённый JSON delivery-записи,
/// отсутствующие расчётная цена или страйк, расхождение идентификаторов строки и полезной
/// нагрузки, конфликт записей с одним ключом либо расхождение времени доставки со справочником.
/// Сырое хранилище обязано быть достаточным для переразбора, поэтому остановка с внятной
/// ошибкой честнее молчаливого пропуска повреждённой записи.
// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
/// </summary>
public sealed class ExpiryMaterializationException : Exception
{
	/// <summary>Создаёт ошибку материализации с контекстом записи доставки.</summary>
	/// <param name="sourceKey">Ключ источника «symbol|deliveryTimeMs», на котором материализация остановилась.</param>
	/// <param name="message">Человекочитаемое описание ошибки.</param>
	/// <param name="innerException">Исходная ошибка разбора, если была.</param>
	public ExpiryMaterializationException(string sourceKey, string message, Exception? innerException = null)
		: base(message, innerException)
	{
		SourceKey = sourceKey;
	}

	/// <summary>Ключ источника «symbol|deliveryTimeMs», на котором материализация остановилась.</summary>
	public string SourceKey { get; }
}
