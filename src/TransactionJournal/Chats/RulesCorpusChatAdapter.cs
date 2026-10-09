namespace TransactionJournal.Chats;

using System.Text;
using TransactionJournal.Chats.Ports;
using TransactionJournal.Hints.Corpus;

/// <summary>
/// Адаптер корпуса правил для чата в composition root поверх
/// загрузчика корпуса: индекс отдаёт все карточки (исполняемые, без машинного
/// триггера и retired) компактно — id, название, краткое содержание из первых
/// фраз описаний карточки, статус; полный текст карточки рендерится только по
/// id. Проект Chats на Hints не ссылается — связывание сред живёт
/// здесь, как и остальные связывания composition root.
/// Traceability: openspec:chats/context#scenario-chat-context-card-index-only
/// Traceability: openspec:architecture/solution-structure#scenario-environments-not-linked
/// </summary>
public sealed class RulesCorpusChatAdapter : IRuleCorpusReader
{
	/// <summary>Предельная длина одной части краткого содержания карточки.</summary>
	private const int MaxSummaryPartLength = 160;

	/// <summary>Символы-терминаторы первой фразы краткого содержания.</summary>
	private static readonly char[] SentenceTerminators = ['.', '!', '?'];

	private readonly RulesCorpusLoader _loader;

	/// <summary>Создаёт адаптер поверх загрузчика корпуса правил.</summary>
	/// <param name="loader">Загрузчик корпуса: снимок пересобирается на каждое чтение.</param>
	public RulesCorpusChatAdapter(RulesCorpusLoader loader)
	{
		_loader = loader;
	}

	/// <inheritdoc />
	public Task<IReadOnlyList<RuleCardSummary>> ListIndexAsync(CancellationToken cancellationToken = default)
	{
		// Снимок корпуса пересобирается на каждое чтение: правки карточек
		// действуют со следующего прохода, кэширование внутри прохода не нужно.
		// Traceability: openspec:hints/rules-corpus#requirement-corpus-snapshot-per-pass
		var snapshot = _loader.Load();
		var cards = snapshot.ExecutableCards
			.Concat(snapshot.UnimplementedCards)
			.Concat(snapshot.RetiredCards)
			.OrderBy(card => card.Id, StringComparer.Ordinal)
			.Select(card => new RuleCardSummary
			{
				Id = card.Id,
				Title = card.Title,
				Summary = ComposeSummary(card),
				Status = card.Status == RuleCardStatus.Active ? "active" : "retired",
			})
			.ToArray();
		return Task.FromResult<IReadOnlyList<RuleCardSummary>>(cards);
	}

	/// <inheritdoc />
	public Task<RuleCardContent?> ReadCardAsync(string cardId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
		var snapshot = _loader.Load();
		var card = snapshot.ExecutableCards
			.Concat(snapshot.UnimplementedCards)
			.Concat(snapshot.RetiredCards)
			.FirstOrDefault(candidate => string.Equals(candidate.Id, cardId, StringComparison.Ordinal));
		if (card is null)
		{
			return Task.FromResult<RuleCardContent?>(null);
		}

		var content = new RuleCardContent { Id = card.Id, Text = RenderCard(card) };
		return Task.FromResult<RuleCardContent?>(content);
	}

	/// <inheritdoc />
	public Task<IReadOnlyList<RuleCardThreshold>> ReadCardThresholdsAsync(string cardId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
		var snapshot = _loader.Load();
		var card = snapshot.ExecutableCards
			.Concat(snapshot.UnimplementedCards)
			.Concat(snapshot.RetiredCards)
			.FirstOrDefault(candidate => string.Equals(candidate.Id, cardId, StringComparison.Ordinal));
		if (card is null)
		{
			return Task.FromResult<IReadOnlyList<RuleCardThreshold>>([]);
		}

		// Пороги отданы парами имя-величина-единица без рендеринга полного
		// текста: снимку нужен только числовой канон карточки.
		// Traceability: openspec:chats/context#scenario-chat-context-portfolio-snapshot-without-construction
		var thresholds = card.Thresholds
			.Select(threshold => new RuleCardThreshold { Name = threshold.Name, Value = threshold.Value, Unit = threshold.Unit })
			.ToArray();
		return Task.FromResult<IReadOnlyList<RuleCardThreshold>>(thresholds);
	}

	/// <summary>
	/// Краткое содержание — первые фразы описаний триггера и действия: «условие
	/// → действие»; при отсутствии описаний откат к шаблону подсказки или названию.
	/// Traceability: openspec:chats/context#scenario-chat-context-card-index-only
	/// </summary>
	private static string ComposeSummary(RuleCard card)
	{
		var trigger = FirstSentence(card.TriggerDescription);
		var action = FirstSentence(card.ActionDescription);
		var summary = trigger is null
			? action
			: action is null
				? trigger
				: $"{trigger} → {action}";
		return summary ?? card.HintTemplate ?? card.Title;
	}

	/// <summary>Первая фраза текста: пробелы схлопнуты, обрез до лимита с многоточием; пустой текст — null.</summary>
	private static string? FirstSentence(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		var flat = NormalizeWhitespace(text);
		var cut = flat.IndexOfAny(SentenceTerminators);
		var sentence = cut < 0 ? flat : flat[..(cut + 1)];
		return sentence.Length <= MaxSummaryPartLength
			? sentence
			: sentence[..MaxSummaryPartLength].TrimEnd() + "…";
	}

	/// <summary>Переносы строк и повторы пробелов схлопываются в один пробел.</summary>
	private static string NormalizeWhitespace(string text) =>
		string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

	/// <summary>
	/// Полный текст карточки: декларативные поля, описания триггера и действия,
	/// пороги, шаблон подсказки, конфликтные объявления и атрибуция источников.
	/// Traceability: openspec:chats/context#scenario-chat-context-card-index-only
	/// </summary>
	private static string RenderCard(RuleCard card)
	{
		var sb = new StringBuilder();
		sb.AppendLine($"# {card.Id} — {card.Title}");
		sb.AppendLine();
		sb.AppendLine($"- Характер действия: {card.Character}");
		if (card.Technique is { } technique)
		{
			sb.AppendLine($"- Техника исполнения: {technique}");
		}

		sb.AppendLine(card.Clarity == RuleClarity.Crisp
			? "- Чёткость: crisp — императив прямого действия"
			: "- Чёткость: fuzzy — предмет решения человека");
		sb.AppendLine(card.Scope == RuleScope.OpenConstructions
			? "- Область: открытые конструкции"
			: "- Область: структура портфеля");
		sb.AppendLine(card.Status == RuleCardStatus.Active
			? "- Статус: active"
			: "- Статус: retired (выведено из канона, не исполняется)");
		if (card.Status == RuleCardStatus.Retired)
		{
			sb.AppendLine($"- Причина снятия с канона: {card.RetiredReason}");
			if (card.SupersededBy is { } supersededBy)
			{
				sb.AppendLine($"- Замещена карточкой: {supersededBy}");
			}
		}

		if (card.TriggerDescription is { } triggerDescription)
		{
			sb.AppendLine();
			sb.AppendLine($"Триггер: {NormalizeWhitespace(triggerDescription)}");
		}

		if (card.ActionDescription is { } actionDescription)
		{
			sb.AppendLine();
			sb.AppendLine($"Действие: {NormalizeWhitespace(actionDescription)}");
		}

		if (card.Thresholds.Count > 0)
		{
			sb.AppendLine();
			sb.AppendLine("Пороги:");
			foreach (var threshold in card.Thresholds)
			{
				sb.AppendLine($"- {threshold.Name} = {threshold.Value} {threshold.Unit}");
			}
		}

		if (card.HintTemplate is { } hintTemplate)
		{
			sb.AppendLine();
			sb.AppendLine($"Шаблон подсказки: {NormalizeWhitespace(hintTemplate)}");
		}

		if (card.ConflictsWith.Count > 0)
		{
			sb.AppendLine();
			sb.AppendLine($"Объявленные конфликты: {string.Join(", ", card.ConflictsWith)}");
		}

		if (card.Sources.Count > 0)
		{
			sb.AppendLine();
			sb.AppendLine("Источники:");
			foreach (var source in card.Sources)
			{
				sb.AppendLine($"- {source.Tag}, {source.File}:");
				foreach (var quote in source.Quotes)
				{
					sb.AppendLine($"  - {quote}");
				}
			}
		}

		return sb.ToString();
	}
}
