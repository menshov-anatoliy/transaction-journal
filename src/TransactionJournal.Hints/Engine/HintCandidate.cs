namespace TransactionJournal.Hints.Engine;

using TransactionJournal.Hints.Corpus;
using TransactionJournal.Hints.Ports;

/// <summary>
/// Кандидат подсказки — сработавший триггер на конкретном субъекте до рендера
/// и записи: карточка-источник, субъект, факты шаблона и ключ периода окна.
/// Кандидаты проходят дедуп-окно и превращаются в записи подсказок проходом.
/// Traceability: openspec:hints/engine-pass#requirement-engine-deterministic-selection
/// </summary>
public sealed record HintCandidate
{
	/// <summary>Карточка правила, чей триггер сработал.</summary>
	public required RuleCard Card { get; init; }

	/// <summary>Субъект подсказки: журнал либо открытая конструкция.</summary>
	public required HintSubject Subject { get; init; }

	/// <summary>Факты триггера — значения подстановок шаблона карточки.</summary>
	public required IReadOnlyDictionary<string, string> Facts { get; init; }

	/// <summary>Ключ периода окна дедупа; null — правило непериодное.</summary>
	public string? WindowPeriodKey { get; init; }
}
