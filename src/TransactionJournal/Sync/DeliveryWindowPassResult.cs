using TransactionJournal.Bybit;

namespace TransactionJournal.Sync;

/// <summary>Итог прохода окна delivery-истории.</summary>
public sealed class DeliveryWindowPassResult
{
	/// <summary>
	/// Все delivery-записи окна в порядке выдачи биржи, включая известные журналу.
	/// Движок синхронизации отличает по ним пустое окно (биржа исчерпала данные)
	/// от окна только с известными записями.
	/// </summary>
	public required IReadOnlyList<BybitDeliveryRecord> AllDeliveries { get; init; }

	/// <summary>Новые (неизвестные журналу) delivery-записи окна в порядке выдачи биржи: новые раньше старых.</summary>
	public required IReadOnlyList<BybitDeliveryRecord> NewDeliveries { get; init; }

	/// <summary>Сколько страниц запросил проход.</summary>
	public required int PagesFetched { get; init; }
}
