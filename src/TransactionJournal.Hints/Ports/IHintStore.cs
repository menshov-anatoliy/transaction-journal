namespace TransactionJournal.Hints.Ports;

/// <summary>
/// Порт хранения подсказок: подсказка — самоописательная запись окружения,
/// хранится всей историей в SQLite; терминальные записи не удаляются. Домен
/// журнала порт не видит: идентификатор конструкции входит значением, а не
/// внешним ключом в доменные таблицы.
// Traceability: openspec:hints/hint-lifecycle#requirement-hint-self-describing-record
/// </summary>
public interface IHintStore
{
	/// <summary>Сохраняет новую подсказку и возвращает её с присвоенным идентификатором.</summary>
	/// <param name="hint">Запись подсказки с незаполненным идентификатором.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<HintRecord> AddAsync(HintRecord hint, CancellationToken cancellationToken = default);

	/// <summary>
	/// Ищет запись текущего окна «правило × субъект (+ период)» — живую запись
	/// со статусом new/applied/dismissed; expired окна не закрывает. Ответ решает
	/// дедуп: есть запись — новая подсказка того же ключа подавляется.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-dedup-rule-subject-window
	/// </summary>
	/// <param name="ruleId">Идентификатор правила корпуса.</param>
	/// <param name="subject">Субъект подсказки.</param>
	/// <param name="periodKey">Ключ периода периодного правила; null — правило непериодное.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<HintRecord?> FindWindowEntryAsync(
		string ruleId,
		HintSubject subject,
		string? periodKey,
		CancellationToken cancellationToken = default);

	/// <summary>Живые записи — все подсказки со статусом new/applied/dismissed.</summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<IReadOnlyList<HintRecord>> ListLiveAsync(CancellationToken cancellationToken = default);

	/// <summary>Все записи подсказок — живые и терминальные, для read-моделей отображения.</summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<IReadOnlyList<HintRecord>> ListAllAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Переводит живую подсказку в терминальный статус applied/dismissed/expired.
	/// applied и dismissed ставит только человек из UI, expired — только агент;
	/// переход допустим только из new, терминальные статусы reopen не имеют.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-lifecycle-transitions
	/// </summary>
	/// <param name="id">Идентификатор записи подсказки.</param>
	/// <param name="targetStatus">Терминальный статус перевода.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>true — перевод выполнен; false — записи нет или она уже терминальная.</returns>
	Task<bool> TryTransitionAsync(long id, HintStatus targetStatus, CancellationToken cancellationToken = default);

	/// <summary>
	/// Фиксирует первый показ подсказки в UI; последующие показы запись не меняют.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-lifecycle-transitions
	/// </summary>
	/// <param name="id">Идентификатор записи подсказки.</param>
	/// <param name="seenAt">Момент первого показа.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task MarkFirstSeenAsync(long id, DateTimeOffset seenAt, CancellationToken cancellationToken = default);
}

/// <summary>Статус подсказки в жизненном цикле new → applied | dismissed | expired.</summary>
public enum HintStatus
{
	/// <summary>Новая живая подсказка — ждёт реакции человека или гашения агентом.</summary>
	New,

	/// <summary>Человек применил подсказку — терминальный статус.</summary>
	Applied,

	/// <summary>Человек отклонил подсказку — терминальный статус; повтор в окне подавлен.</summary>
	Dismissed,

	/// <summary>Агент погасил подсказку: условие ушло, субъект закрыт или правило выведено — терминальный статус.</summary>
	Expired,
}

/// <summary>Вид субъекта подсказки — закрытый набор v1: открытая конструкция или журнал.</summary>
public enum HintSubjectKind
{
	/// <summary>Журнал целиком — портфельный уровень (например, лимиты риска периода).</summary>
	Journal,

	/// <summary>Конкретная открытая конструкция со ссылкой на её идентификатор.</summary>
	Construction,
}

/// <summary>Субъект подсказки: журнальные правила адресованы журналу, правила конструкции — конструкции.</summary>
public sealed record HintSubject
{
	/// <summary>Вид субъекта.</summary>
	public required HintSubjectKind Kind { get; init; }

	/// <summary>Идентификатор конструкции; заполнен только для вида «конструкция».</summary>
	public long? ConstructionId { get; init; }

	/// <summary>Субъект «журнал» — портфельный уровень.</summary>
	public static HintSubject ForJournal() => new() { Kind = HintSubjectKind.Journal };

	/// <summary>Субъект «конструкция» с идентификатором конструкции.</summary>
	public static HintSubject ForConstruction(long constructionId) => new()
	{
		Kind = HintSubjectKind.Construction,
		ConstructionId = constructionId,
	};
}

/// <summary>Тег источника подсказки — атрибуция происхождения правила корпуса.</summary>
public sealed record HintSourceTag
{
	/// <summary>Тег источника (например ПИ, ЛИЧ, ОК).</summary>
	public required string Tag { get; init; }

	/// <summary>Путь файла базы знаний от корня вольта Obsidian.</summary>
	public required string File { get; init; }

	/// <summary>Цитаты-доказательства источника.</summary>
	public required IReadOnlyList<string> Quotes { get; init; }
}

/// <summary>
/// Запись подсказки — самоописательный снимок момента генерации: значения
/// денормализуются при создании, позднейшие правки, retiring или удаление
/// карточки корпуса историю не искажают. Полей группировки и сортировки нет —
/// слои отображения выводят их из субъекта и характера при показе.
// Traceability: openspec:hints/hint-lifecycle#requirement-hint-self-describing-record
// Traceability: openspec:hints/engine-pass#requirement-engine-self-describing-record
/// </summary>
public sealed record HintRecord
{
	/// <summary>Суррогатный ключ записи; 0 у несохранённой подсказки.</summary>
	public long Id { get; init; }

	/// <summary>Идентификатор правила корпуса (id карточки).</summary>
	public required string RuleId { get; init; }

	/// <summary>Субъект подсказки — открытая конструкция или журнал.</summary>
	public required HintSubject Subject { get; init; }

	/// <summary>Характер действия правила (например risk-mode, profit-target).</summary>
	public required string Character { get; init; }

	/// <summary>Чёткость правила: crisp — императив, fuzzy — пометка решения.</summary>
	public required string Clarity { get; init; }

	/// <summary>Теги источников правила с цитатами-доказательствами.</summary>
	public required IReadOnlyList<HintSourceTag> Sources { get; init; }

	/// <summary>Отрендеренный текст подсказки.</summary>
	public required string Text { get; init; }

	/// <summary>Факты триггера — пары ключ-значение, заполнившие шаблон карточки.</summary>
	public required IReadOnlyDictionary<string, string> Facts { get; init; }

	/// <summary>Отметка as-of прохода, на котором подсказка сгенерирована.</summary>
	public required DateTimeOffset AsOf { get; init; }

	/// <summary>Статус жизненного цикла.</summary>
	public required HintStatus Status { get; init; }

	/// <summary>Момент первого показа в UI; null, пока подсказка не показывалась.</summary>
	public DateTimeOffset? FirstSeenAt { get; init; }

	/// <summary>
	/// Ключ периода периодного правила в ключе окна дедупа (например неделя,
	/// месяц, квартал лимитов риска); null — правило непериодное.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-dedup-rule-subject-window
	/// </summary>
	public string? WindowPeriodKey { get; init; }
}
