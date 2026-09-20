using TransactionJournal.Bybit;

namespace TransactionJournal.Sync;

/// <summary>Итог прохода окна истории исполнения.</summary>
public sealed class ExecutionWindowPassResult
{
	/// <summary>Новые (неизвестные журналу) записи окна в порядке выдачи биржи: новые раньше старых.</summary>
	public required IReadOnlyList<BybitExecution> NewExecutions { get; init; }

	/// <summary>Сколько страниц запросил проход, включая страницу ранней остановки.</summary>
	public required int PagesFetched { get; init; }

	/// <summary>Проход остановился раньше исчерпания: страница целиком из известных execId.</summary>
	public required bool EarlyStopped { get; init; }

	/// <summary>Биржа исчерпала страницы окна: курсор закончился без ранней остановки.</summary>
	public bool Exhausted => EarlyStopped == false;
}
