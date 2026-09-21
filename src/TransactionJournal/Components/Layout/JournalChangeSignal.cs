namespace TransactionJournal.Components.Layout;

/// <summary>
/// Сигнал изменений журнала внутри одного circuit: экраны оповещают его после
/// выполнения мутаций домена, а каркас «Терминала» перечитывает панель — итог
/// журнала, бейдж «Входящих» и заголовок транзитной вкладки — без навигации.
/// Сервис привязан к circuit, поэтому подписка каркаса и оповещения экранов
/// всегда относятся к одному пользовательскому сеансу.
/// </summary>
// Действия экрана меняют данные, которые показывает и каркас: индикация статуса
// транзитной вкладки обновляется по сигналу сразу после действия, а не при
// ближайшей навигации.
// Traceability: openspec:ui/screens#scenario-detail-status-change-indicated
public sealed class JournalChangeSignal
{
	private readonly List<Func<Task>> _handlers = [];
	private readonly object _gate = new();

	/// <summary>
	/// Подписывает асинхронный обработчик на сигнал изменений; освобождение
	/// возвращённого объекта снимает подписку.
	/// </summary>
	/// <param name="handler">Асинхронный обработчик сигнала.</param>
	/// <exception cref="ArgumentNullException">Обработчик не задан.</exception>
	public IDisposable Subscribe(Func<Task> handler)
	{
		ArgumentNullException.ThrowIfNull(handler);

		// Список обработчиков защищён локом: каркас подписывается и освобождается
		// в своём контуре, экраны оповещают из обработчиков событий.
		lock (_gate)
		{
			_handlers.Add(handler);
		}

		return new Subscription(() =>
		{
			lock (_gate)
			{
				_handlers.Remove(handler);
			}
		});
	}

	/// <summary>
	/// Оповещает подписчиков об изменении журнала: обработчики выполняются
	/// последовательно в порядке подписки.
	/// </summary>
	public async Task RaiseAsync()
	{
		Func<Task>[] handlers;
		lock (_gate)
		{
			handlers = [.. _handlers];
		}

		foreach (var handler in handlers)
		{
			await handler().ConfigureAwait(true);
		}
	}

	/// <summary>Снимает подписку при освобождении; repeat-вызовы безопасны.</summary>
	private sealed class Subscription(Action unsubscribe) : IDisposable
	{
		public void Dispose() => unsubscribe();
	}
}
