namespace TransactionJournal.Sync;

/// <summary>
/// Идемпотентный ключ delivery-записи: пара symbol + deliveryTime, по которой
/// журнал распознаёт уже известную запись. Повторная загрузка записи с тем же ключом
/// не создаёт дублей ни в хранилище сырья, ни в доменных представлениях.
/// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
/// </summary>
public sealed record DeliveryRecordKey(string Symbol, long DeliveryTimeMs);
