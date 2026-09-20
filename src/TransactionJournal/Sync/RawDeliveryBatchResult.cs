namespace TransactionJournal.Sync;

/// <summary>Итог пакетной записи сырых delivery-записей.</summary>
public sealed class RawDeliveryBatchResult
{
	/// <summary>Ключи symbol + deliveryTime фактически вставленных записей в порядке пачки.</summary>
	public required IReadOnlyList<DeliveryRecordKey> InsertedKeys { get; init; }

	/// <summary>Сколько записей пачки пропущено как уже известные журналу по symbol + deliveryTime.</summary>
	public required int SkippedKnownCount { get; init; }

	/// <summary>Сколько записей вставлено этим вызовом.</summary>
	public int InsertedCount => InsertedKeys.Count;
}
