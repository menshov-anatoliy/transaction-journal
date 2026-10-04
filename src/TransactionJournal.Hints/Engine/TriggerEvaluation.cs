namespace TransactionJournal.Hints.Engine;

using TransactionJournal.Hints.Corpus;
using TransactionJournal.Hints.Ports;

/// <summary>
/// Вход чистой функции триггера: снимок журнала, партия марок, карточка правила
/// и отметка as-of прохода. Для правила конструкции вход дополнен конкретной
/// открытой конструкцией — субъект итерации определяет каркас прохода по виду
/// триггера, сама функция остаётся чистой относительно своих входов.
/// Traceability: openspec:hints/engine-pass#requirement-engine-deterministic-selection
/// </summary>
public sealed record TriggerEvaluationInput
{
	/// <summary>Снимок журнала одного прохода: конструкции, позиции, хронология сделок.</summary>
	public required JournalSnapshot Snapshot { get; init; }

	/// <summary>Партия рыночных марок прохода; недоступный источник до триггеров не доходит.</summary>
	public required MarkBatch Marks { get; init; }

	/// <summary>Карточка правила корпуса — источник порогов, шаблона и атрибуции.</summary>
	public required RuleCard Card { get; init; }

	/// <summary>Отметка as-of прохода: базы окон времени и дней до экспирации.</summary>
	public required DateTimeOffset AsOf { get; init; }

	/// <summary>
	/// Конструкция-субъект оценки; null у журнальных правил — их условие считается
	/// по всему снимку журнала.
	/// </summary>
	public ConstructionView? Construction { get; init; }
}

/// <summary>
/// Исход чистой функции триггера: сработало или нет; при срабатывании — факты,
/// заполнившие шаблон карточки, и ключ периода окна для периодных правил.
/// Traceability: openspec:hints/engine-pass#requirement-engine-deterministic-selection
/// </summary>
public sealed record TriggerOutcome
{
	private static readonly IReadOnlyDictionary<string, string> EmptyFacts = new Dictionary<string, string>();

	/// <summary>Условие правила выполнено на этом субъекте.</summary>
	public bool Fired { get; init; }

	/// <summary>Факты триггера — значения подстановок шаблона карточки.</summary>
	public IReadOnlyDictionary<string, string> Facts { get; init; } = EmptyFacts;

	/// <summary>Ключ периода окна дедупа; null — правило непериодное.</summary>
	public string? WindowPeriodKey { get; init; }

	/// <summary>Исход «условие не выполнено или непроверяемо по данным».</summary>
	public static TriggerOutcome NotFired() => new();

	/// <summary>Исход «условие выполнено» с фактами шаблона и ключом периода окна.</summary>
	public static TriggerOutcome FiredWith(IReadOnlyDictionary<string, string> facts, string? windowPeriodKey = null) => new()
	{
		Fired = true,
		Facts = facts,
		WindowPeriodKey = windowPeriodKey,
	};
}
