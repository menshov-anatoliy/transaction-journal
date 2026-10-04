namespace TransactionJournal.Consultations;

/// <summary>
/// Инструкции агента консультаций: поведение ассистента задаётся редактируемым
/// файлом <c>consultation-prompt.md</c> — отдельный настраиваемый путь, по
/// умолчанию файл лежит рядом с каталогом <c>rules/</c>; валидный файл
/// переопределяет встроенный минимальный дефолт, отсутствие или нечитаемость
/// файла чат не ломает — работает встроенный дефолт. Файл перечитывается на
/// каждое чтение: правки владельца действуют со следующего сообщения без
/// перезапуска приложения.
/// Traceability: openspec:consultations/context#requirement-context-agent-instructions-file
/// </summary>
public sealed class ConsultationInstructions
{
	/// <summary>Имя файла инструкций по умолчанию, размещаемого рядом с rules/.</summary>
	public const string DefaultFileName = "consultation-prompt.md";

	/// <summary>
	/// Встроенный минимальный дефолт инструкций: обязательные принципы поведения
	/// ассистента — планы «если/то» по свершившимся фактам без направленных
	/// прогнозов цены, цитирование id карточек корпуса, явная маркировка
	/// «вне корпуса правил», read-only чат с планом управления markdown-текстом
	/// и пост-мортем «работа над ошибками» для закрытых конструкций.
	/// Traceability: openspec:consultations/context#requirement-context-scenario-conduct
	/// Traceability: openspec:consultations/context#requirement-context-postmortem-mode
	/// </summary>
	public const string DefaultInstructions =
		"""
		Ты — ассистент-консультант журнала сделок по одной торговой конструкции.
		Владелец задаёт вопросы в диалоге; детерминированный снимок контекста
		(конструкция, портфельные агрегаты, лимиты, индекс корпуса правил)
		передан тебе вместе с сообщением. Отвечай на русском языке, по существу
		вопроса и строго по фактам снимка.

		Обязательные принципы:

		1. Планы действий формулируй сценариями «если/то» по свершившимся фактам
		журнала с явными порогами. Направленные прогнозы цены запрещены: на вопрос
		о будущем движении цены переформулируй ответ в сценарий «если/то» и не
		утверждай, куда пойдёт цена.
		2. Вариант, покрываемый правилом корпуса, сопровождай id соответствующей
		карточки (например, «(карточка ac-01)»). Перед цитированием прочитай
		полный текст карточки инструментом read_rule_card по её id.
		3. Вариант, который не покрыт ни одной карточкой корпуса, явно помечай
		формулировкой «вне корпуса правил».
		4. План управления выдавай markdown-текстом прямо в ответе. Чат работает
		только на чтение: ничего не изменяй и не сохраняй в журнале.
		5. Для закрытой конструкции веди разбор в режиме пост-мортема
		(«работа над ошибками») по финальному состоянию журнала: что сработало,
		где была ошибка исполнения, что изменить в правилах на будущее.

		Рыночные данные доступны только инструментами на момент вопроса:
		get_market_snapshot по базовой монете, get_option_board по базовой монете,
		read_rule_card по id карточки. Если рыночные данные недоступны или их
		as-of заметно устарел, помечай это явно и не давай рыночно-зависимых
		рекомендаций — отвечай по журналу и корпусу правил.
		""";

	private readonly string _path;

	/// <summary>Создаёт источник инструкций агента по пути файла.</summary>
	/// <param name="path">Путь файла инструкций (дефолт consultation-prompt.md рядом с rules/, переопределяется настройкой).</param>
	public ConsultationInstructions(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		_path = path;
	}

	/// <summary>Путь файла инструкций, читаемого источником.</summary>
	public string FilePath => _path;

	/// <summary>
	/// Возвращает текст инструкций для агента: валидный непустой файл
	/// переопределяет встроенный дефолт; отсутствующий, пустой или нечитаемый
	/// файл оставляет дефолт — чат продолжает работать.
	/// Traceability: openspec:consultations/context#scenario-context-instructions-override
	/// Traceability: openspec:consultations/context#scenario-context-instructions-missing-ok
	/// </summary>
	/// <returns>Текст инструкций агента.</returns>
	public string Read()
	{
		try
		{
			var text = File.ReadAllText(_path);
			return string.IsNullOrWhiteSpace(text) ? DefaultInstructions : text;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or System.Security.SecurityException)
		{
			// Любая нечитаемость файла — штатный откат на встроенный дефолт:
			// инструкции не являются предусловием работы чата.
			return DefaultInstructions;
		}
	}
}
