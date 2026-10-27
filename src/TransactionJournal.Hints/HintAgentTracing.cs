namespace TransactionJournal.Hints;

using System.Diagnostics;

/// <summary>
/// Единый источник операционных спанов прохода подсказок: корневой hint_pass
/// вокруг RunAsync и дочерние спаны фаз прохода. Проход детерминирован и модель
/// не вызывает — на спанах источника нет атрибутов gen_ai.*, собственные
/// атрибуты используют только префикс tj.*.
// Traceability: openspec:ops/agent-tracing#requirement-trace-otel-two-surfaces
/// </summary>
internal static class HintAgentTracing
{
	/// <summary>
	/// Источник спанов прохода; имя — часть контракта трассировки, sink
	/// подписывается на него в composition root наряду с источником чата.
	// Traceability: openspec:ops/agent-tracing#requirement-trace-otel-two-surfaces
	/// </summary>
	public static readonly ActivitySource Source = new("TransactionJournal.Hints");

	/// <summary>Имя корневого спана прохода подсказок.</summary>
	public const string PassSpanName = "hint_pass";

	/// <summary>Имя дочернего спана фазы «снимок корпуса».</summary>
	public const string PhaseCorpusSnapshot = "hint_pass.corpus_snapshot";

	/// <summary>Имя дочернего спана фазы «гашение выведенных правил».</summary>
	public const string PhaseExpireWithdrawnRules = "hint_pass.expire_withdrawn_rules";

	/// <summary>Имя дочернего спана фазы «снимок журнала».</summary>
	public const string PhaseJournalSnapshot = "hint_pass.journal_snapshot";

	/// <summary>Имя дочернего спана фазы «гашение закрытых субъектов».</summary>
	public const string PhaseExpireClosedSubjects = "hint_pass.expire_closed_subjects";

	/// <summary>Имя дочернего спана фазы «рыночные котировки» — сетевая задержка источника видна отдельным спаном.</summary>
	public const string PhaseQuotes = "hint_pass.quotes";

	/// <summary>Имя дочернего спана фазы «оценка триггеров».</summary>
	public const string PhaseEvaluateTriggers = "hint_pass.evaluate_triggers";

	/// <summary>Атрибут исхода прохода — стабильная строка значения HintPassOutcome.</summary>
	public const string OutcomeTag = "tj.hints.outcome";

	/// <summary>Счётчик созданных записей подсказок за проход.</summary>
	public const string CreatedTag = "tj.hints.created";

	/// <summary>Счётчик погашенных в expired записей за проход.</summary>
	public const string ExpiredTag = "tj.hints.expired";

	/// <summary>Открывает дочерний спан фазы прохода; родитель берётся из текущей активности.</summary>
	/// <param name="phaseName">Имя спана фазы — константа фаз этого класса.</param>
	/// <returns>Активность фазы; null — трассировка не слушается.</returns>
	public static Activity? StartPhase(string phaseName) =>
		Source.StartActivity(phaseName, ActivityKind.Internal);

	/// <summary>Стабильная строка исхода прохода для атрибута tj.hints.outcome.</summary>
	/// <param name="outcome">Исход прохода.</param>
	public static string SerializeOutcome(HintPassOutcome outcome) => outcome switch
	{
		HintPassOutcome.Completed => "completed",
		HintPassOutcome.SkippedMarketUnavailable => "skipped-market-unavailable",
		HintPassOutcome.CorpusInvalid => "corpus-invalid",
		_ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
	};
}
