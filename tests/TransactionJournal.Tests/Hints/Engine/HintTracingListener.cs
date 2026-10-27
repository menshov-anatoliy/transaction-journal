namespace TransactionJournal.Tests.Hints.Engine;

using System.Diagnostics;

/// <summary>
/// Фейковый приёмник спанов для тестов: подписывается на источник по имени и
/// собирает стартовавшие активности в порядке старта — заменяет sink трассы
/// в проверках формы дерева спанов.
/// </summary>
internal sealed class HintTracingListener : IDisposable
{
	private readonly ActivityListener _listener;

	/// <summary>Создаёт приёмник для источника с заданным именем.</summary>
	/// <param name="sourceName">Имя ActivitySource — например TransactionJournal.Hints.</param>
	public HintTracingListener(string sourceName)
	{
		_listener = new ActivityListener
		{
			ShouldListenTo = source => source.Name == sourceName,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
			ActivityStarted = activity => Started.Add(activity),
		};
		ActivitySource.AddActivityListener(_listener);
	}

	/// <summary>Стартовавшие активности в порядке старта.</summary>
	public List<Activity> Started { get; } = [];

	public void Dispose() => _listener.Dispose();
}
