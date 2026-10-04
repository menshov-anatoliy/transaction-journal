namespace TransactionJournal.Hints.Engine;

using System.Text.RegularExpressions;
using TransactionJournal.Hints.Corpus;

/// <summary>
/// Рендер текста подсказки: шаблон hintTemplate карточки заполняется фактами
/// триггера, чёткость правила задаёт формулировку — чёткое правило звучит
/// императивом прямого действия, размытое получает префикс «[решение]» и
/// оставляет действие решению человека. Чёткость влияет на формулировку, но не
/// на факт срабатывания.
/// Traceability: openspec:hints/engine-pass#requirement-engine-clarity-shapes-wording
/// </summary>
public static partial class HintTextRenderer
{
	[GeneratedRegex(@"\{([A-Za-z0-9_]+)\}")]
	private static partial Regex PlaceholderRegex();

	/// <summary>Префикс формулировки размытого правила — предмет оценки человека.</summary>
	public const string FuzzyPrefix = "[решение] ";

	/// <summary>Рендерит текст подсказки карточкой и фактами триггера.</summary>
	/// <param name="card">Карточка правила с шаблоном и чёткостью.</param>
	/// <param name="facts">Факты триггера — значения подстановок шаблона.</param>
	/// <exception cref="InvalidOperationException">У сработавшего триггера нет шаблона или не хватает факта.</exception>
	public static string Render(RuleCard card, IReadOnlyDictionary<string, string> facts)
	{
		if (string.IsNullOrWhiteSpace(card.HintTemplate))
		{
			throw new InvalidOperationException($"Карточка '{card.Id}' сработавшего триггера без hintTemplate.");
		}

		var text = PlaceholderRegex().Replace(card.HintTemplate, match =>
		{
			var key = match.Groups[1].Value;
			return facts.TryGetValue(key, out var value)
				? value
				: throw new InvalidOperationException($"Триггер карточки '{card.Id}' не передал факт '{key}' для шаблона.");
		});

		// Размытое правило не предписывает действие: префикс помечает предмет
		// оценки человека; чёткое звучит императивом как сформулировано в шаблоне.
		return card.Clarity == RuleClarity.Fuzzy ? FuzzyPrefix + text : text;
	}

	/// <summary>Строковое значение чёткости для самоописательной записи подсказки.</summary>
	/// <param name="clarity">Чёткость правила.</param>
	public static string ClarityText(RuleClarity clarity)
		=> clarity == RuleClarity.Crisp ? "crisp" : "fuzzy";
}
