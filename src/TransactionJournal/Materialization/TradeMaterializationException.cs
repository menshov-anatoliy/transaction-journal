namespace TransactionJournal.Materialization;

/// <summary>
/// Ошибка материализации сделки из сырой записи исполнения: повреждённый JSON,
/// отсутствующие цена или количество, неизвестная сторона исполнения, расхождение
/// идентификаторов строки и полезной нагрузки либо конфликт записей с одним execId.
/// Сырое хранилище обязано быть достаточным для переразбора, поэтому остановка
/// с внятной ошибкой честнее молчаливого пропуска повреждённой записи.
// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
/// </summary>
public sealed class TradeMaterializationException : Exception
{
	/// <summary>Создаёт ошибку материализации с контекстом записи исполнения.</summary>
	/// <param name="execId">Идентификатор исполнения сырой записи, на которой материализация остановилась.</param>
	/// <param name="message">Человекочитаемое описание ошибки.</param>
	/// <param name="innerException">Исходная ошибка разбора, если была.</param>
	public TradeMaterializationException(string execId, string message, Exception? innerException = null)
		: base(message, innerException)
	{
		ExecId = execId;
	}

	/// <summary>Идентификатор исполнения сырой записи, на которой материализация остановилась.</summary>
	public string ExecId { get; }
}
