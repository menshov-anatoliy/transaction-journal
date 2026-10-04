namespace TransactionJournal.Hints.Corpus;

using YamlDotNet.RepresentationModel;

/// <summary>
/// Загрузчик корпуса правил: читает каталог целиком за одно чтение, парсит
/// YAML всех карточек, проверяет схему и объявленные конфликтные пары, собирая
/// все проблемы в один список; при наличии проблем поднимает агрегированную
/// ошибку до построения снимка — валидность корпуса есть предусловие прохода.
/// Отсутствующий или пустой каталог равен битому корпусу. Снимок неизменяем;
/// retired-карточки входят в него только для гашения живых записей.
/// Traceability: openspec:hints/rules-corpus#requirement-corpus-validity-precondition
/// Traceability: openspec:hints/rules-corpus#requirement-corpus-snapshot-per-pass
/// </summary>
public sealed class RulesCorpusLoader
{
	private static readonly IReadOnlySet<string> Characters = new HashSet<string>(StringComparer.Ordinal)
	{
		"risk-mode",
		"profit-target",
		"profit-protection",
		"risk-reduction",
		"rolling",
		"entry",
		"exit",
		"futures-leg",
		"rebuild-dismantle",
		"other",
	};

	private static readonly IReadOnlySet<string> Clarities = new HashSet<string>(StringComparer.Ordinal) { "crisp", "fuzzy" };

	private static readonly IReadOnlySet<string> Scopes = new HashSet<string>(StringComparer.Ordinal) { "open-constructions", "portfolio" };

	private static readonly IReadOnlySet<string> Statuses = new HashSet<string>(StringComparer.Ordinal) { "active", "retired" };

	private readonly string _corpusPath;

	/// <summary>Создаёт загрузчик корпуса по пути каталога карточек.</summary>
	/// <param name="corpusPath">Путь каталога корпуса (дефолт rules/ рядом с exe, переопределяется настройкой).</param>
	public RulesCorpusLoader(string corpusPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(corpusPath);
		_corpusPath = corpusPath;
	}

	/// <summary>Путь каталога корпуса, читаемого загрузчиком.</summary>
	public string CorpusPath => _corpusPath;

	/// <summary>
	/// Читает каталог корпуса в неизменяемый снимок. Все проблемы собираются
	/// за одно чтение и поднимаются одной агрегированной ошибкой.
	/// </summary>
	/// <returns>Снимок корпуса с разбором карточек по назначению.</returns>
	/// <exception cref="CorpusInvalidException">Корпус невалиден — список проблем полный.</exception>
	public RulesCorpusSnapshot Load()
	{
		if (!Directory.Exists(_corpusPath))
		{
			throw new CorpusInvalidException([$"Каталог корпуса правил отсутствует: {_corpusPath}"]);
		}

		var files = Directory.EnumerateFiles(_corpusPath, "*.yaml", SearchOption.TopDirectoryOnly)
			.Concat(Directory.EnumerateFiles(_corpusPath, "*.yml", SearchOption.TopDirectoryOnly))
			.Order(StringComparer.Ordinal)
			.ToArray();
		if (files.Length == 0)
		{
			throw new CorpusInvalidException([$"Каталог корпуса правил не содержит карточек: {_corpusPath}"]);
		}

		var problems = new List<string>();
		var cards = new List<RuleCard>();
		foreach (var file in files)
		{
			ParseCard(file, problems, cards);
		}

		ValidateDeclaredConflicts(cards, problems);

		if (problems.Count > 0)
		{
			throw new CorpusInvalidException(problems);
		}

		return BuildSnapshot(cards);
	}

	private void ParseCard(string file, List<string> problems, List<RuleCard> cards)
	{
		var fileName = Path.GetFileName(file);
		var problemsBefore = problems.Count;
		YamlNode? root;
		try
		{
			var stream = new YamlStream();
			stream.Load(new StringReader(File.ReadAllText(file)));
			root = stream.Documents.Count > 0 ? stream.Documents[0].RootNode : null;
		}
		catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or IOException)
		{
			problems.Add($"{fileName}: YAML не парсится ({ex.Message})");
			return;
		}

		if (root is not YamlMappingNode map)
		{
			problems.Add($"{fileName}: карточка должна быть YAML-отображением полей схемы");
			return;
		}

		// id равен имени файла без расширения — расхождение ломает схему карточки.
		var id = Path.GetFileNameWithoutExtension(file);
		var cardId = RequiredScalar(map, "id", fileName, problems);
		if (cardId is not null && cardId != id)
		{
			problems.Add($"{fileName}: id карточки '{cardId}' не равен имени файла '{id}'");
		}

		var title = RequiredScalar(map, "title", fileName, problems);
		var character = BoundedScalar(map, "character", Characters, fileName, problems);
		var technique = OptionalScalar(map, "technique");
		var clarity = BoundedScalar(map, "clarity", Clarities, fileName, problems);
		var scope = BoundedScalar(map, "scope", Scopes, fileName, problems);
		var status = BoundedScalar(map, "status", Statuses, fileName, problems);
		var thresholds = ParseThresholds(map, fileName, problems);
		var (implementation, triggerDescription) = ParseTrigger(map, fileName, problems);
		var (hintTemplate, actionDescription) = ParseAction(map, fileName, problems);
		var sources = ParseSources(map, fileName, problems);
		var conflictsWith = ParseConflictsWith(map, fileName, problems);

		string? retiredReason = null;
		string? supersededBy = null;
		if (status == "retired")
		{
			(retiredReason, supersededBy) = ParseRetired(map, fileName, problems);
		}

		// Карточка добавляется в снимок только без собственных проблем: счётчик
		// до парсинга отделяет битость этого файла от уже собранных проблем.
		if (problems.Count > problemsBefore)
		{
			return;
		}

		cards.Add(new RuleCard
		{
			Id = id,
			Title = title!,
			Character = character!,
			Technique = technique,
			Clarity = ParseClarity(clarity!),
			Scope = ParseScope(scope!),
			Status = ParseStatus(status!),
			Thresholds = thresholds,
			TriggerImplementation = implementation,
			TriggerDescription = triggerDescription,
			ActionDescription = actionDescription,
			HintTemplate = hintTemplate,
			Sources = sources,
			ConflictsWith = conflictsWith,
			RetiredReason = retiredReason,
			SupersededBy = supersededBy,
		});
	}

	private static string? RequiredScalar(YamlMappingNode map, string key, string fileName, List<string> problems)
	{
		var node = Child(map, key);
		var value = (node as YamlScalarNode)?.Value;
		if (string.IsNullOrWhiteSpace(value))
		{
			problems.Add($"{fileName}: обязательное поле '{key}' отсутствует или пусто");
			return null;
		}

		return value;
	}

	private static string? BoundedScalar(YamlMappingNode map, string key, IReadOnlySet<string> allowed, string fileName, List<string> problems)
	{
		var value = RequiredScalar(map, key, fileName, problems);
		if (value is not null && !allowed.Contains(value))
		{
			problems.Add($"{fileName}: поле '{key}' имеет значение '{value}' вне закрытого набора ({string.Join(", ", allowed)})");
			return null;
		}

		return value;
	}

	private static string? OptionalScalar(YamlMappingNode map, string key)
		=> (Child(map, key) as YamlScalarNode)?.Value;

	private static List<RuleThreshold> ParseThresholds(YamlMappingNode map, string fileName, List<string> problems)
	{
		var thresholds = new List<RuleThreshold>();
		var node = Child(map, "thresholds");
		if (node is not YamlSequenceNode sequence)
		{
			problems.Add($"{fileName}: поле 'thresholds' отсутствует или не является списком (пустой корпус порогов — 'thresholds: []')");
			return thresholds;
		}

		foreach (var item in sequence)
		{
			if (item is not YamlMappingNode thresholdMap)
			{
				problems.Add($"{fileName}: элемент 'thresholds' должен быть отображением name/value/unit");
				continue;
			}

			var name = RequiredScalar(thresholdMap, "name", fileName, problems);
			var valueNode = Child(thresholdMap, "value");
			var value = (valueNode as YamlScalarNode)?.Value;
			if (string.IsNullOrWhiteSpace(value))
			{
				problems.Add($"{fileName}: порог без поля 'value' (допустимо строковое значение, например \"7-10\")");
			}

			var unit = RequiredScalar(thresholdMap, "unit", fileName, problems);
			if (name is not null && value is not null && unit is not null)
			{
				thresholds.Add(new RuleThreshold { Name = name, Value = value, Unit = unit });
			}
		}

		return thresholds;
	}

	private static (string? Implementation, string? Description) ParseTrigger(YamlMappingNode map, string fileName, List<string> problems)
	{
		if (Child(map, "trigger") is not YamlMappingNode triggerMap)
		{
			problems.Add($"{fileName}: поле 'trigger' отсутствует или не является отображением");
			return (null, null);
		}

		// Неизвестный движку ключ и implementation: null схеме соответствуют —
		// это не битость: карточка уходит в чек-лист, проход продолжается.
		// Traceability: openspec:hints/rules-corpus#requirement-corpus-unimplemented-trigger-checklist
		if (triggerMap.Children.ContainsKey(new YamlScalarNode("implementation")) == false)
		{
			problems.Add($"{fileName}: в 'trigger' отсутствует поле 'implementation' (null допустим — правило без машинной реализации)");
			return (null, null);
		}

		// Человекочитаемое описание триггера — информационное поле схемы: не
		// проверяется и не влияет на исполнение, нужно краткому содержанию
		// индекса корпуса в снимке консультации.
		return (OptionalScalar(triggerMap, "implementation"), OptionalScalar(triggerMap, "description"));
	}

	private static (string? HintTemplate, string? Description) ParseAction(YamlMappingNode map, string fileName, List<string> problems)
	{
		if (Child(map, "action") is not YamlMappingNode actionMap)
		{
			problems.Add($"{fileName}: поле 'action' отсутствует или не является отображением");
			return (null, null);
		}

		if (actionMap.Children.ContainsKey(new YamlScalarNode("hintTemplate")) == false)
		{
			problems.Add($"{fileName}: в 'action' отсутствует поле 'hintTemplate' (null допустим у правил без машинного триггера)");
			return (null, null);
		}

		// Человекочитаемое описание действия — информационное поле схемы: не
		// проверяется и не влияет на исполнение, нужно краткому содержанию
		// индекса корпуса в снимке консультации.
		return (OptionalScalar(actionMap, "hintTemplate"), OptionalScalar(actionMap, "description"));
	}

	private static List<RuleSource> ParseSources(YamlMappingNode map, string fileName, List<string> problems)
	{
		var sources = new List<RuleSource>();
		if (Child(map, "sources") is not YamlSequenceNode sequence)
		{
			problems.Add($"{fileName}: поле 'sources' отсутствует или не является списком");
			return sources;
		}

		if (sequence.Children.Count == 0)
		{
			problems.Add($"{fileName}: поле 'sources' пусто — правило без атрибуции происхождения");
			return sources;
		}

		foreach (var item in sequence)
		{
			if (item is not YamlMappingNode sourceMap)
			{
				problems.Add($"{fileName}: элемент 'sources' должен быть отображением tag/file/quotes");
				continue;
			}

			var tag = RequiredScalar(sourceMap, "tag", fileName, problems);
			var file = RequiredScalar(sourceMap, "file", fileName, problems);
			var quotes = new List<string>();
			if (Child(sourceMap, "quotes") is not YamlSequenceNode quoteSequence || quoteSequence.Children.Count == 0)
			{
				problems.Add($"{fileName}: источник '{tag}' без непустого списка цитат 'quotes'");
			}
			else
			{
				foreach (var quote in quoteSequence)
				{
					var text = (quote as YamlScalarNode)?.Value;
					if (string.IsNullOrWhiteSpace(text))
					{
						problems.Add($"{fileName}: источник '{tag}' содержит пустую цитату");
						continue;
					}

					quotes.Add(text);
				}
			}

			if (tag is not null && file is not null && quotes.Count > 0)
			{
				sources.Add(new RuleSource { Tag = tag, File = file, Quotes = quotes });
			}
		}

		return sources;
	}

	private static List<string> ParseConflictsWith(YamlMappingNode map, string fileName, List<string> problems)
	{
		var conflicts = new List<string>();
		var node = Child(map, "conflicts_with");
		if (node is null)
		{
			return conflicts;
		}

		if (node is not YamlSequenceNode sequence)
		{
			problems.Add($"{fileName}: поле 'conflicts_with' должно быть списком id правил");
			return conflicts;
		}

		foreach (var item in sequence)
		{
			var id = (item as YamlScalarNode)?.Value;
			if (string.IsNullOrWhiteSpace(id))
			{
				problems.Add($"{fileName}: 'conflicts_with' содержит пустой id правила");
				continue;
			}

			conflicts.Add(id);
		}

		return conflicts;
	}

	private static (string Reason, string? SupersededBy) ParseRetired(YamlMappingNode map, string fileName, List<string> problems)
	{
		if (Child(map, "retired") is not YamlMappingNode retiredMap)
		{
			problems.Add($"{fileName}: retired-карточка без отображения 'retired' с полем 'reason'");
			return (string.Empty, null);
		}

		var reason = RequiredScalar(retiredMap, "reason", fileName, problems);
		var supersededBy = OptionalScalar(retiredMap, "superseded_by");
		return (reason ?? string.Empty, supersededBy);
	}

	/// <summary>
	/// Проверяет объявленные конфликтные пары: объявление трактуется симметрично —
	/// достаточно записи с одной из сторон; две активные карточки пары делают
	/// корпус невалидным, пара с retired-карточкой валидна и остаётся документацией
	/// известного напряжения источников.
	/// Traceability: openspec:hints/rules-corpus#requirement-corpus-declared-conflicts-validated
	/// </summary>
	private static void ValidateDeclaredConflicts(List<RuleCard> cards, List<string> problems)
	{
		var byId = cards.ToDictionary(card => card.Id, StringComparer.Ordinal);
		var reported = new HashSet<string>(StringComparer.Ordinal);
		foreach (var card in cards)
		{
			foreach (var otherId in card.ConflictsWith)
			{
				if (!byId.TryGetValue(otherId, out var other))
				{
					continue;
				}

				if (card.Status != RuleCardStatus.Active || other.Status != RuleCardStatus.Active)
				{
					continue;
				}

				var pairKey = string.CompareOrdinal(card.Id, other.Id) < 0
					? $"{card.Id}|{other.Id}"
					: $"{other.Id}|{card.Id}";
				if (!reported.Add(pairKey))
				{
					continue;
				}

				problems.Add($"Объявленная конфликтная пара нарушена: карточки '{card.Id}' и '{other.Id}' обе имеют status: active");
			}
		}
	}

	private static RulesCorpusSnapshot BuildSnapshot(List<RuleCard> cards)
	{
		var executable = new List<RuleCard>();
		var unimplemented = new List<RuleCard>();
		var retired = new List<RuleCard>();
		foreach (var card in cards)
		{
			if (card.Status == RuleCardStatus.Retired)
			{
				retired.Add(card);
			}
			else if (card.TriggerImplementation is null || !HintTriggerKeys.IsKnown(card.TriggerImplementation))
			{
				unimplemented.Add(card);
			}
			else
			{
				executable.Add(card);
			}
		}

		return new RulesCorpusSnapshot
		{
			ExecutableCards = executable,
			UnimplementedCards = unimplemented,
			RetiredCards = retired,
		};
	}

	private static RuleClarity ParseClarity(string value) => value == "crisp" ? RuleClarity.Crisp : RuleClarity.Fuzzy;

	private static RuleScope ParseScope(string value) => value == "open-constructions" ? RuleScope.OpenConstructions : RuleScope.Portfolio;

	private static RuleCardStatus ParseStatus(string value) => value == "active" ? RuleCardStatus.Active : RuleCardStatus.Retired;

	private static YamlNode? Child(YamlNode node, string key)
		=> node is YamlMappingNode map && map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? value : null;
}
