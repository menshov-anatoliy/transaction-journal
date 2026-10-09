namespace TransactionJournal.Chats.Ports;

/// <summary>
/// Порт хранения чатов агента: единое хранилище плоских чатов — каждый чат
/// ведётся единой полной историей сообщений без вложенных диалогов. Чат
/// создаётся первым сообщением владельца вместе с параметрами — ИИ-модель,
/// опциональная привязка к конструкции, набор источников; параметры
/// фиксируются при создании и повторно не передаются, сменить контекст
/// можно только новым чатом. ИИ-помощник видит историю только собственного
/// чата. Жизненный цикл чата управляется только ручными действиями
/// владельца — завершение, продолжение, удаление; автоматического
/// завершения нет. Чат — запись окружения: домен журнала порт не видит.
// Traceability: openspec:chats/history#requirement-chat-flat-full-history
// Traceability: openspec:chats/history#requirement-chat-environment-record
// Traceability: openspec:chats/history#requirement-chat-manual-completion-and-deletion
/// </summary>
public interface IChatStore
{
	/// <summary>
	/// Сохраняет сообщение и возвращает его с присвоенными идентификаторами.
	/// Отдельной команды создания чата нет: сообщение без идентификатора чата
	/// создаёт новый чат с переданными параметрами; сообщение существующего
	/// чата параметры не принимает — привязка и набор источников неизменяемы,
	/// перепривязка означает начало нового чата. Сообщение в завершённый
	/// чат продолжает его и возвращает в активные.
	// Traceability: openspec:chats/history#scenario-chat-created-by-first-message
	// Traceability: openspec:chats/history#scenario-chat-binding-cannot-change
	// Traceability: openspec:chats/history#scenario-chat-resume-returns-to-active
	/// </summary>
	/// <param name="chatId">Идентификатор чата; null — создаётся новый чат этим сообщением.</param>
	/// <param name="start">Параметры создания чата: обязательны при новом чате, запрещены для существующего.</param>
	/// <param name="message">Черновик сообщения с незаполненным идентификатором.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<ChatMessage> AppendMessageAsync(
		long? chatId,
		ChatStartParameters? start,
		ChatMessageDraft message,
		CancellationToken cancellationToken = default);

	/// <summary>Чат по идентификатору: параметры и статус; null — чата нет.</summary>
	/// <param name="chatId">Идентификатор чата.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<ChatRecord?> FindChatAsync(long chatId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Сообщения чата в порядке следования — полная история видна только
	/// этому чату, истории соседних чатов в выборку не попадают.
	// Traceability: openspec:chats/history#scenario-chat-neighbour-isolation
	/// </summary>
	/// <param name="chatId">Идентификатор чата.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(long chatId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Смена модели существующего чата владельцем: меняется только параметр
	/// чата — последующие сообщения уходят выбранной модели, история не
	/// переписывается. Возвращает чат с обновлённой моделью.
	// Traceability: openspec:chats/sources#scenario-sources-model-switch-mid-chat
	/// </summary>
	/// <param name="chatId">Идентификатор чата.</param>
	/// <param name="model">Новый идентификатор модели в API провайдера.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<ChatRecord> ChangeChatModelAsync(long chatId, string model, CancellationToken cancellationToken = default);

	/// <summary>
	/// Ручное завершение чата владельцем: активный чат исчезает из списка
	/// активных и появляется в списке завершённых, история сохраняется.
	/// Единственный способ завершить чат — это действие; автоматики нет.
	// Traceability: openspec:chats/history#scenario-chat-completion-hides-to-completed-list
	/// </summary>
	/// <param name="chatId">Идентификатор завершаемого чата.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task CompleteChatAsync(long chatId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Ручное продолжение завершённого чата: он возвращается в список
	/// активных ещё до отправки нового сообщения.
	// Traceability: openspec:chats/history#scenario-chat-resume-returns-to-active
	/// </summary>
	/// <param name="chatId">Идентификатор продолжаемого чата.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task ResumeChatAsync(long chatId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Явное удаление чата владельцем: чат и все его сообщения исчезают
	/// целиком, корзины нет — восстановление невозможно.
	// Traceability: openspec:chats/history#scenario-chat-hard-delete
	/// </summary>
	/// <param name="chatId">Идентификатор удаляемого чата.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task DeleteChatAsync(long chatId, CancellationToken cancellationToken = default);

	/// <summary>Активные чаты в порядке создания.</summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<IReadOnlyList<ChatRecord>> ListActiveChatsAsync(CancellationToken cancellationToken = default);

	/// <summary>Завершённые владельцем чаты в порядке создания.</summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<IReadOnlyList<ChatRecord>> ListCompletedChatsAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Стирание пересбором: удаляет все чаты, привязанные к конструкции,
	/// вместе со всеми их сообщениями; чаты без привязки и чаты других
	/// конструкций не затрагиваются. Чат — запись окружения: вместе со
	/// старой записью конструкций уходит и привязанный к ней чат.
	// Traceability: openspec:chats/history#scenario-chat-rebuild-wipes-bound-chats
	/// </summary>
	/// <param name="constructionId">Идентификатор стираемой конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task DeleteForConstructionAsync(long constructionId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Открывает область стирания чатов пересбором: удаления привязанных
	/// чатов накапливаются в транзакции хранилища чатов и применяются только
	/// CommitAsync; закрытие области без фиксации откатывает их целиком.
	/// Область нужна, чтобы стирание чатов конструкции уехало в той же
	/// координированной фиксации, что и план пересбора журнала: сбой плана
	/// откатывает и журнал, и стирание, пережитых привязанных чатов не остаётся.
	// Traceability: openspec:chats/history#scenario-chat-rebuild-wipes-bound-chats
	/// </summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Область стирания; CommitAsync фиксирует удаления, dispose без фиксации — откат.</returns>
	Task<IChatRebuildWipe> BeginRebuildWipeAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Область стирания чатов пересбором: группирует удаления привязанных
/// чатов в одной транзакции хранилища чатов. Фиксация области — отдельное
/// явное действие, вызываемое строго после фиксации плана журнала; закрытие
/// области без фиксации откатывает все её удаления.
// Traceability: openspec:chats/history#scenario-chat-rebuild-wipes-bound-chats
/// </summary>
public interface IChatRebuildWipe : IAsyncDisposable
{
	/// <summary>Стирает чаты конструкции со всеми историями внутри области.</summary>
	/// <param name="constructionId">Идентификатор стираемой конструкции.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task DeleteForConstructionAsync(long constructionId, CancellationToken cancellationToken = default);

	/// <summary>Фиксирует удаления области: после фиксации стёртые чаты не восстанавливаются.</summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task CommitAsync(CancellationToken cancellationToken = default);
}

/// <summary>Статус жизненного цикла чата: активен или завершён владельцем.</summary>
public enum ChatStatus
{
	/// <summary>Активный чат.</summary>
	Active,

	/// <summary>Завершён владельцем; продолжение возвращает в активные.</summary>
	Completed,
}

/// <summary>
/// Параметры создания чата: ИИ-модель, опциональная привязка к конструкции
/// и набор источников данных. Фиксируются первым сообщением владельца и
/// дальше не меняются.
// Traceability: openspec:chats/history#scenario-chat-created-by-first-message
/// </summary>
public sealed record ChatStartParameters
{
	/// <summary>ИИ-модель чата: новые сообщения чата уходят ей.</summary>
	public required string Model { get; init; }

	/// <summary>Идентификатор конструкции привязки; null — чат без привязки, портфельный уровень журнала.</summary>
	public long? ConstructionId { get; init; }

	/// <summary>
	/// Набор источников данных чата — подмножество закрытого справочника
	/// источников; владелец выбирает его при создании, дефолт — все три
	/// категории.
	// Traceability: openspec:chats/sources#scenario-sources-subset-parameter
	// Traceability: openspec:chats/sources#scenario-sources-three-categories
	/// </summary>
	public IReadOnlyList<ChatDataSource> Sources { get; init; } = ChatDataSourceCatalog.All;
}

/// <summary>Чат агента: плоская полная история обмена с параметрами и статусом.</summary>
public sealed record ChatRecord
{
	/// <summary>Суррогатный ключ чата; 0 у несохранённого чата.</summary>
	public long Id { get; init; }

	/// <summary>ИИ-модель чата: новые сообщения чата уходят ей; владелец меняет её на лету без переписывания истории.</summary>
	public required string Model { get; init; }

	/// <summary>Идентификатор конструкции привязки; null — чат без привязки.</summary>
	public long? ConstructionId { get; init; }

	/// <summary>Набор источников данных чата — подмножество закрытого справочника источников.</summary>
	public required IReadOnlyList<ChatDataSource> Sources { get; init; }

	/// <summary>Статус жизненного цикла: активен или завершён.</summary>
	public ChatStatus Status { get; init; }

	/// <summary>Момент создания чата первым сообщением владельца.</summary>
	public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>Роль автора сообщения чата.</summary>
public enum ChatMessageRole
{
	/// <summary>Сообщение владельца.</summary>
	User,

	/// <summary>Ответ ИИ-помощника.</summary>
	Assistant,
}

/// <summary>Черновик сообщения чата с незаполненным идентификатором.</summary>
public sealed record ChatMessageDraft
{
	/// <summary>Роль автора сообщения.</summary>
	public required ChatMessageRole Role { get; init; }

	/// <summary>Текст сообщения.</summary>
	public required string Text { get; init; }

	/// <summary>Отметка as-of момента сообщения.</summary>
	public required DateTimeOffset AsOf { get; init; }

	/// <summary>След источников ответа ИИ-помощника; у сообщений владельца null.</summary>
	public ChatSourceTrace? SourceTrace { get; init; }
}

/// <summary>Сообщение чата: роль, текст, as-of; у помощника дополнительно след источников.</summary>
public sealed record ChatMessage
{
	/// <summary>Суррогатный ключ сообщения.</summary>
	public long Id { get; init; }

	/// <summary>Идентификатор чата, в котором оставлено сообщение.</summary>
	public long ChatId { get; init; }

	/// <summary>Роль автора сообщения.</summary>
	public required ChatMessageRole Role { get; init; }

	/// <summary>Текст сообщения.</summary>
	public required string Text { get; init; }

	/// <summary>Отметка as-of момента сообщения.</summary>
	public required DateTimeOffset AsOf { get; init; }

	/// <summary>След источников ответа ИИ-помощника; у сообщений владельца null.</summary>
	public ChatSourceTrace? SourceTrace { get; init; }
}

/// <summary>
/// След источников ответа ассистента — обобщение рыночного следа: какие
/// инструменты вызывались и с какими as-of их данные, какие карточки правил
/// прочитаны и каков as-of данных журнала в снимке контекста. Происхождение
/// рекомендаций проверяемо постфактум, UI рендерит из следа ссылки на правила.
// Traceability: openspec:chats/history#requirement-chat-message-composition
// Traceability: openspec:chats/history#scenario-chat-source-trace-persisted
/// </summary>
public sealed record ChatSourceTrace
{
	/// <summary>Вызовы инструментов в порядке следования.</summary>
	public required IReadOnlyList<ChatToolInvocation> Invocations { get; init; }

	/// <summary>Идентификаторы прочитанных карточек правил в порядке чтения, без повторов.</summary>
	public IReadOnlyList<string> RuleCards { get; init; } = [];

	/// <summary>As-of данных журнала в снимке контекста; null — данные журнала не зафиксированы.</summary>
	public DateTimeOffset? JournalAsOf { get; init; }
}

/// <summary>Запись вызова инструмента в следе источников ответа.</summary>
public sealed record ChatToolInvocation
{
	/// <summary>Имя инструмента (read_rule_card, get_market_snapshot, get_option_board).</summary>
	public required string ToolName { get; init; }

	/// <summary>Аргументы вызова в компактном виде.</summary>
	public required string Arguments { get; init; }

	/// <summary>
	/// As-of данных, отданных инструментом вызову; у кэшированной проекции
	/// при недоступном рынке это as-of кэша, null — данных с as-of нет вовсе.
	/// </summary>
	public DateTimeOffset? DataAsOf { get; init; }
}
