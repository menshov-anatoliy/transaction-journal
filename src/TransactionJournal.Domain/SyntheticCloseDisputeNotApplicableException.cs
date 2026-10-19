namespace TransactionJournal.Domain;

/// <summary>
/// Оспаривание неприменимо к связке с ручными сделками: спор правилами журнала
/// предусмотрен только для распознанных связок, ручная удаляется свободно —
/// запись бесполезного спора не создаётся.
// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
/// </summary>
public sealed class SyntheticCloseDisputeNotApplicableException : Exception
{
	/// <summary>Идентификатор линейной сделки связки.</summary>
	public string LinearExecId { get; }

	/// <summary>Идентификатор встречной опционной сделки связки.</summary>
	public string CounterOptionExecId { get; }

	/// <summary>Создаёт исключение для пары сделок ручной связки.</summary>
	/// <param name="linearExecId">Идентификатор линейной сделки.</param>
	/// <param name="counterOptionExecId">Идентификатор встречной опционной сделки.</param>
	public SyntheticCloseDisputeNotApplicableException(string linearExecId, string counterOptionExecId)
		: base(
			"Оспаривание применимо только к распознанным связкам: связка "
			+ $"{linearExecId} + {counterOptionExecId} состоит из ручных сделок и удаляется свободно.")
	{
		LinearExecId = linearExecId;
		CounterOptionExecId = counterOptionExecId;
	}
}
