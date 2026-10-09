namespace TransactionJournal.Hints.Corpus;

/// <summary>
/// Карточка правила корпуса — декларативные YAML-данные, один файл — одна
/// карточка; id равен имени файла. Поля соответствуют схеме корпуса: характер
/// — закрытый справочник таксономии, чёткость crisp/fuzzy, скоуп, статус
/// active/retired, пороги, ключ машинного триггера, шаблон подсказки,
/// атрибуция источников, необязательные объявления конфликтов и retiring.
/// Traceability: openspec:hints/rules-corpus#requirement-corpus-snapshot-per-pass
/// </summary>
public sealed record RuleCard
{
	/// <summary>Идентификатор правила — равен имени файла без расширения.</summary>
	public required string Id { get; init; }

	/// <summary>Название правила.</summary>
	public required string Title { get; init; }

	/// <summary>
	/// Человекочитаемое условие триггера из карточки; информационное поле без
	/// влияния на исполнение движком — служит сырьём для краткого содержания
	/// индекса корпуса в снимке контекста чата.
	/// Traceability: openspec:chats/context#scenario-chat-context-card-index-only
	/// </summary>
	public string? TriggerDescription { get; init; }

	/// <summary>
	/// Человекочитаемое описание действия из карточки; информационное поле без
	/// влияния на исполнение движком — служит сырьём для краткого содержания
	/// индекса корпуса в снимке контекста чата.
	/// Traceability: openspec:chats/context#scenario-chat-context-card-index-only
	/// </summary>
	public string? ActionDescription { get; init; }

	/// <summary>Характер действия правила — один из десяти закрытого справочника.</summary>
	public required string Character { get; init; }

	/// <summary>Техника исполнения (например synthetic-close); null — признак не задан.</summary>
	public string? Technique { get; init; }

	/// <summary>Чёткость правила: crisp — императив прямого действия, fuzzy — к решению человека.</summary>
	public required RuleClarity Clarity { get; init; }

	/// <summary>Область правила: открытые конструкции или структура портфеля.</summary>
	public required RuleScope Scope { get; init; }

	/// <summary>Статус карточки в каноне: active исполняется, retired — только гасит живые записи.</summary>
	public required RuleCardStatus Status { get; init; }

	/// <summary>Пороги правила; значения движок берёт отсюда, а не из кода.</summary>
	public required IReadOnlyList<RuleThreshold> Thresholds { get; init; }

	/// <summary>
	/// Ключ реализации триггера в коде движка; null или неизвестный ключ —
	/// правило не порождает подсказок и попадает в чек-лист сводки.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-unimplemented-trigger-checklist
	/// </summary>
	public string? TriggerImplementation { get; init; }

	/// <summary>Шаблон текста подсказки, заполняемый фактами триггера; null у чек-листных правил.</summary>
	public string? HintTemplate { get; init; }

	/// <summary>Атрибуция происхождения: тег источника, путь файла, цитаты-доказательства.</summary>
	public required IReadOnlyList<RuleSource> Sources { get; init; }

	/// <summary>
	/// Необязательное объявление конфликтных пар: id правил, не могущих быть
	/// активными одновременно с карточкой; трактуется симметрично.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-declared-conflicts-validated
	/// </summary>
	public required IReadOnlyList<string> ConflictsWith { get; init; }

	/// <summary>Причина снятия с канона; заполнена у retired-карточек.</summary>
	public string? RetiredReason { get; init; }

	/// <summary>Замещающая карточка, если есть; необязательное поле retired-карточек.</summary>
	public string? SupersededBy { get; init; }
}

/// <summary>Чёткость правила корпуса.</summary>
public enum RuleClarity
{
	/// <summary>Чёткое правило — императив прямого действия.</summary>
	Crisp,

	/// <summary>Размытое правило — предмет оценки человека, пометка «[решение]».</summary>
	Fuzzy,
}

/// <summary>Область действия правила корпуса.</summary>
public enum RuleScope
{
	/// <summary>Правило открытых конструкций.</summary>
	OpenConstructions,

	/// <summary>Правило структуры личного портфеля.</summary>
	Portfolio,
}

/// <summary>Статус карточки в каноне корпуса.</summary>
public enum RuleCardStatus
{
	/// <summary>Активное правило — исполняется движком.</summary>
	Active,

	/// <summary>Выведенное из канона правило — не исполняется, но гасит живые записи подсказок.</summary>
	Retired,
}

/// <summary>Порог правила: именованная величина с единицей; значение — строка, так как бывает диапазоном (например «7-10»).</summary>
public sealed record RuleThreshold
{
	/// <summary>Имя порога, на которое ссылается триггер.</summary>
	public required string Name { get; init; }

	/// <summary>Значение порога в сыром виде; числовые триггеры парсят его сами.</summary>
	public required string Value { get; init; }

	/// <summary>Единица измерения (percent, days, trades).</summary>
	public required string Unit { get; init; }
}

/// <summary>Источник правила: тег, путь файла базы знаний от корня вольта Obsidian и цитаты-доказательства.</summary>
public sealed record RuleSource
{
	/// <summary>Тег источника (ПИ, ЛИЧ, ОК, ИК-БТ).</summary>
	public required string Tag { get; init; }

	/// <summary>Путь файла базы знаний от корня вольта Obsidian.</summary>
	public required string File { get; init; }

	/// <summary>Цитаты-доказательства источника.</summary>
	public required IReadOnlyList<string> Quotes { get; init; }
}
