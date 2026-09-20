using System.Threading.Tasks;

namespace TransactionJournal.Bybit;

/// <summary>
/// Пауза через TimeProvider: для системных часов — обычный Task.Delay, для подменённых
/// тестами — одноразовый таймер CreateTimer, срабатывающий по виртуальному времени.
/// </summary>
internal static class TimeProviderDelay
{
	/// <summary>Асинхронно ждёт заданный интервал по часам поставщика времени.</summary>
	internal static Task DelayAsync(this TimeProvider timeProvider, TimeSpan delay, CancellationToken cancellationToken)
	{
		if (delay <= TimeSpan.Zero)
		{
			return Task.CompletedTask;
		}

		if (timeProvider == TimeProvider.System)
		{
			return Task.Delay(delay, cancellationToken);
		}

		using var wait = new TimerWait(timeProvider, delay, cancellationToken);
		return wait.Task;
	}

	/// <summary>Одноразовое ожидание на таймере поставщика времени с поддержкой отмены.</summary>
	private sealed class TimerWait : IDisposable
	{
		private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly ITimer? _timer;
		private CancellationTokenRegistration _registration;

		public TimerWait(TimeProvider timeProvider, TimeSpan delay, CancellationToken cancellationToken)
		{
			if (cancellationToken.CanBeCanceled)
			{
				_registration = cancellationToken.Register(static state => ((TimerWait)state!).Cancel(), this);
			}

			_timer = timeProvider.CreateTimer(static state => ((TimerWait)state!).Complete(), this, delay, Timeout.InfiniteTimeSpan);
		}

		public Task Task => _completion.Task;

		private void Complete()
		{
			Dispose();
			_completion.TrySetResult();
		}

		private void Cancel()
		{
			Dispose();
			_completion.TrySetCanceled();
		}

		public void Dispose()
		{
			_timer?.Dispose();
			_registration.Dispose();
		}
	}
}
