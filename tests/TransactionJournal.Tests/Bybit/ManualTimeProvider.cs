namespace TransactionJournal.Tests.Bybit;

/// <summary>
/// Фиктивный поставщик времени для детерминированных проверок устойчивости:
/// единственный источник хода времени — ожидания. Одноразовый таймер срабатывает
/// мгновенно, продвигая виртуальные часы на длину ожидания, поэтому и троттлинг
/// интервала, и паузы повторов Polly выполняются без реального простоя, а все
/// длительности ожиданий запоминаются в порядке возникновения.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
	private readonly object _sync = new();
	private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	/// <summary>Длительности всех ожиданий (созданных одноразовых таймеров) в порядке возникновения.</summary>
	public List<TimeSpan> Delays { get; } = [];

	public override DateTimeOffset GetUtcNow()
	{
		lock (_sync)
		{
			return _utcNow;
		}
	}

	/// <summary>Продвигает виртуальные часы вперёд без ожидания.</summary>
	public void Advance(TimeSpan interval)
	{
		lock (_sync)
		{
			_utcNow += interval;
		}
	}

	public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
	{
		if (dueTime > TimeSpan.Zero)
		{
			Delays.Add(dueTime);
			Advance(dueTime);
		}

		// Часы уже продвинуты на длину ожидания — таймер срабатывает немедленно;
		// периодические таймеры в проверках устойчивости не используются.
		callback(state);
		return InertTimer.Instance;
	}

	/// <summary>Пустой таймер: всё уже сработало в момент создания.</summary>
	private sealed class InertTimer : ITimer
	{
		public static readonly InertTimer Instance = new();

		public bool Change(TimeSpan dueTime, TimeSpan period) => false;

		public void Dispose()
		{
		}

		public ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}
}
