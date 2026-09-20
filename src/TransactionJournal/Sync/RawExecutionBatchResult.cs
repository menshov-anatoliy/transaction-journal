namespace TransactionJournal.Sync;

/// <summary>Итог пакетной записи сырых записей исполнения.</summary>
public sealed class RawExecutionBatchResult
{
	/// <summary>Идентификаторы фактически вставленных записей в порядке пачки.</summary>
	public required IReadOnlyList<string> InsertedExecIds { get; init; }

	/// <summary>Сколько записей пачки пропущено как уже известные журналу по execId.</summary>
	public required int SkippedKnownCount { get; init; }

	/// <summary>Сколько записей вставлено этим вызовом.</summary>
	public int InsertedCount => InsertedExecIds.Count;
}
