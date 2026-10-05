namespace TransactionJournal.Consultations;

using TransactionJournal.Consultations.Ports;

/// <summary>
/// Накопитель рыночного следа одного ответа ассистента: инструменты исполняются
/// внутри агентного цикла, запись ведётся в момент вызова тула — когда as-of
/// отданных данных известен точно. По завершении стрима след фиксируется в
/// сообщении ассистента; без инструментальных вызовов следа нет.
// Traceability: openspec:consultations/history#requirement-history-message-composition
/// </summary>
public sealed class ConsultationMarketTraceRecorder
{
	private readonly List<ConsultationToolInvocation> _invocations = [];

	/// <summary>Записывает один вызов инструмента: имя, компактные аргументы и as-of отданных данных.</summary>
	/// <param name="toolName">Имя инструмента реестра read-only функций.</param>
	/// <param name="arguments">Аргументы вызова в компактном виде.</param>
	/// <param name="dataAsOf">As-of данных тула; null — рыночных данных у вызова нет вовсе.</param>
	public void Record(string toolName, string arguments, DateTimeOffset? dataAsOf)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
		ArgumentException.ThrowIfNullOrWhiteSpace(arguments);
		_invocations.Add(new ConsultationToolInvocation
		{
			ToolName = toolName,
			Arguments = arguments,
			DataAsOf = dataAsOf,
		});
	}

	/// <summary>Строит рыночный след сообщения; null — инструментальных вызовов не было.</summary>
	public ConsultationMarketTrace? Build() => _invocations.Count == 0
		? null
		: new ConsultationMarketTrace { Invocations = [.. _invocations] };
}
