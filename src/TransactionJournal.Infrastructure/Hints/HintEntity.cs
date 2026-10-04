namespace TransactionJournal.Infrastructure.Hints;

/// <summary>
/// Строка таблицы подсказок в SQLite — хранение записи окружения агента.
/// Сущность живёт в Infrastructure: домен о подсказках не знает, а проект
/// Hints не ссылается на EF; в запись порта строка отображается адаптером
/// хранилища. Ссылка на конструкцию хранится значением, без внешнего ключа
/// в доменные таблицы: подсказка — самоописательная запись всей историей,
/// терминальные записи не удаляются.
// Traceability: openspec:hints/hint-lifecycle#requirement-hint-self-describing-record
/// </summary>
internal sealed class HintEntity
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>Идентификатор правила корпуса (id карточки).</summary>
	public required string RuleId { get; set; }

	/// <summary>Вид субъекта подсказки — «журнал» или «конструкция», хранится строкой.</summary>
	public required string SubjectKind { get; set; }

	/// <summary>Идентификатор конструкции-субъекта значением; null у субъекта «журнал».</summary>
	public long? SubjectConstructionId { get; set; }

	/// <summary>Характер действия правила.</summary>
	public required string Character { get; set; }

	/// <summary>Чёткость правила: crisp или fuzzy.</summary>
	public required string Clarity { get; set; }

	/// <summary>Теги источников с цитатами в JSON — денормализованный снимок корпуса момента генерации.</summary>
	public required string SourcesJson { get; set; }

	/// <summary>Отрендеренный текст подсказки.</summary>
	public required string Text { get; set; }

	/// <summary>Факты триггера в JSON — пары ключ-значение шаблона карточки.</summary>
	public required string FactsJson { get; set; }

	/// <summary>Отметка as-of прохода генерации.</summary>
	public required DateTimeOffset AsOf { get; set; }

	/// <summary>Статус жизненного цикла, хранится строкой.</summary>
	public required string Status { get; set; }

	/// <summary>Момент первого показа в UI; null, пока подсказка не показывалась.</summary>
	public DateTimeOffset? FirstSeenAt { get; set; }

	/// <summary>Ключ периода периодного правила в ключе окна дедупа; null — правило непериодное.</summary>
	public string? WindowPeriodKey { get; set; }
}
