namespace TransactionJournal.Infrastructure.Chats;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TransactionJournal.Chats;
using TransactionJournal.Chats.Ports;

/// <summary>
/// Адаптер порта хранения чатов: единая SQLite-база всех чатов агента —
/// таблицы чатов и сообщений одним файлом, per-construction базы
/// консультаций заменены. Изоляция чатов — фильтром сообщений по чату.
/// Привязка к конструкции хранится значением и фиксируется при создании:
/// операций перепривязки хранилище не предоставляет. Каждый вызов создаёт
/// короткоживущий контекст, поэтому хранилище безопасно в длительных
/// сессиях и фоновых проходах.
// Traceability: openspec:chats/history#requirement-chat-environment-record
// Traceability: openspec:chats/history#requirement-chat-flat-full-history
/// </summary>
public sealed class ChatStore : IChatStore
{
	// Компактный JSON: источники и след читаются адаптером, а не человеком.
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = false,
	};

	private readonly string _databasePath;

	/// <summary>Создаёт хранилище над файлом единой базы чатов; каталог создаётся сразу.</summary>
	/// <param name="databasePath">Путь файла базы чатов (например App_Data/chats.db).</param>
	/// <exception cref="ArgumentException">Путь файла пуст.</exception>
	public ChatStore(string databasePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
		_databasePath = databasePath;

		// Каталог базы создаётся заранее: файл появляется с первым сообщением.
		var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
		if (string.IsNullOrEmpty(directory) == false)
		{
			Directory.CreateDirectory(directory);
		}
	}

	/// <inheritdoc cref="IChatStore.AppendMessageAsync" />
	public async Task<ChatMessage> AppendMessageAsync(
		long? chatId,
		ChatStartParameters? start,
		ChatMessageDraft message,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(message);

		using var db = CreateContext();
		ChatEntity chat;
		if (chatId is null)
		{
			// Чат создаётся самим сообщением владельца: отдельной команды
			// создания нет, момент создания — as-of первого сообщения,
			// параметры чата фиксируются здесь и дальше не меняются.
			// Traceability: openspec:chats/history#scenario-chat-created-by-first-message
			if (start is null)
			{
				throw new ArgumentException(
					"Новому чату обязательны параметры создания: модель, привязка, источники.",
					nameof(start));
			}

			chat = new ChatEntity
			{
				Model = NormalizeModel(start.Model),
				ConstructionId = start.ConstructionId,
				SourcesJson = SerializeSources(NormalizeSources(start.Sources)),
				Status = ChatStatus.Active.ToString(),
				CreatedAt = message.AsOf,
			};
		}
		else
		{
			// Параметры чата неизменяемы: попытка передать их существующему чату —
			// попытка перепривязки; сменить контекст можно только новым чатом.
			// Traceability: openspec:chats/history#scenario-chat-binding-cannot-change
			if (start is not null)
			{
				throw new ArgumentException(
					"Параметры чата фиксируются при создании: для существующего чата они запрещены.",
					nameof(start));
			}

			chat = await db.Chats
				.FirstOrDefaultAsync(candidate => candidate.Id == chatId.Value, cancellationToken)
				.ConfigureAwait(false)
				?? throw new InvalidOperationException($"Чат {chatId} не найден.");

			// Продолжение завершённого чата возвращает его в активные: сообщение
			// владельца — ручное действие продолжения; само по себе оно чат не
			// завершает, автоматического завершения нет.
			// Traceability: openspec:chats/history#scenario-chat-resume-returns-to-active
			if (chat.Status == ChatStatus.Completed.ToString())
			{
				chat.Status = ChatStatus.Active.ToString();
			}
		}

		var entity = new ChatMessageEntity
		{
			Chat = chat,
			Role = message.Role.ToString(),
			Text = message.Text,
			AsOf = message.AsOf,
			SourceTraceJson = message.SourceTrace == null
				? null
				: JsonSerializer.Serialize(message.SourceTrace, JsonOptions),
		};
		db.Messages.Add(entity);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return ToRecord(entity);
	}

	/// <inheritdoc cref="IChatStore.FindChatAsync" />
	public async Task<ChatRecord?> FindChatAsync(long chatId, CancellationToken cancellationToken = default)
	{
		EnsureChatId(chatId);
		using var db = CreateContext();
		var entity = await db.Chats
			.AsNoTracking()
			.FirstOrDefaultAsync(candidate => candidate.Id == chatId, cancellationToken)
			.ConfigureAwait(false);
		return entity == null ? null : ToRecord(entity);
	}

	/// <inheritdoc cref="IChatStore.ListMessagesAsync" />
	public async Task<IReadOnlyList<ChatMessage>> ListMessagesAsync(long chatId, CancellationToken cancellationToken = default)
	{
		EnsureChatId(chatId);
		using var db = CreateContext();

		// Изоляция историй: выбираются только сообщения своего чата — соседние
		// чаты, даже того же владельца, в выборку не попадают.
		// Traceability: openspec:chats/history#scenario-chat-neighbour-isolation
		var entities = await db.Messages
			.AsNoTracking()
			.Where(message => message.ChatId == chatId)
			.OrderBy(message => message.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return entities.Select(ToRecord).ToList();
	}

	/// <inheritdoc cref="IChatStore.ChangeChatModelAsync" />
	public async Task<ChatRecord> ChangeChatModelAsync(
		long chatId,
		string model,
		CancellationToken cancellationToken = default)
	{
		EnsureChatId(chatId);
		ArgumentException.ThrowIfNullOrWhiteSpace(model);

		using var db = CreateContext();
		var chat = await LoadChatAsync(db, chatId, cancellationToken).ConfigureAwait(false);

		// Смена на лету меняет только параметр чата: сообщения не трогаются —
		// история сохраняется как есть, а последующие вопросы уходят выбранной
		// модели, которую конвейер читает из чата при каждом обращении.
		// Traceability: openspec:chats/sources#scenario-sources-model-switch-mid-chat
		chat.Model = model.Trim();
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return ToRecord(chat);
	}

	/// <inheritdoc cref="IChatStore.CompleteChatAsync" />
	public async Task CompleteChatAsync(long chatId, CancellationToken cancellationToken = default)
	{
		EnsureChatId(chatId);
		using var db = CreateContext();
		var chat = await LoadChatAsync(db, chatId, cancellationToken).ConfigureAwait(false);

		// Завершение — только ручное действие владельца: активный чат исчезает
		// из списка активных и появляется в списке завершённых, история
		// сохраняется; автоматического завершения нет.
		// Traceability: openspec:chats/history#scenario-chat-completion-hides-to-completed-list
		chat.Status = ChatStatus.Completed.ToString();
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc cref="IChatStore.ResumeChatAsync" />
	public async Task ResumeChatAsync(long chatId, CancellationToken cancellationToken = default)
	{
		EnsureChatId(chatId);
		using var db = CreateContext();
		var chat = await LoadChatAsync(db, chatId, cancellationToken).ConfigureAwait(false);

		// Продолжение возвращает завершённый чат в активные ещё до отправки
		// нового сообщения.
		// Traceability: openspec:chats/history#scenario-chat-resume-returns-to-active
		chat.Status = ChatStatus.Active.ToString();
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc cref="IChatStore.DeleteChatAsync" />
	public async Task DeleteChatAsync(long chatId, CancellationToken cancellationToken = default)
	{
		EnsureChatId(chatId);
		using var db = CreateContext();
		var chat = await LoadChatAsync(db, chatId, cancellationToken).ConfigureAwait(false);

		// Удаление целиком без корзины: сначала все сообщения чата, затем сам
		// чат — ни записей, ни возможности восстановления не остаётся.
		// Traceability: openspec:chats/history#scenario-chat-hard-delete
		await db.Messages
			.Where(message => message.ChatId == chatId)
			.ExecuteDeleteAsync(cancellationToken)
			.ConfigureAwait(false);
		db.Chats.Remove(chat);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc cref="IChatStore.ListActiveChatsAsync" />
	public async Task<IReadOnlyList<ChatRecord>> ListActiveChatsAsync(CancellationToken cancellationToken = default) =>
		await ListByStatusAsync(ChatStatus.Active, cancellationToken).ConfigureAwait(false);

	/// <inheritdoc cref="IChatStore.ListCompletedChatsAsync" />
	public async Task<IReadOnlyList<ChatRecord>> ListCompletedChatsAsync(CancellationToken cancellationToken = default) =>
		await ListByStatusAsync(ChatStatus.Completed, cancellationToken).ConfigureAwait(false);

	/// <inheritdoc cref="IChatStore.DeleteForConstructionAsync" />
	public async Task DeleteForConstructionAsync(long constructionId, CancellationToken cancellationToken = default)
	{
		EnsureConstructionId(constructionId);

		// Разовое стирание поверх области: та же логика удаления привязанных
		// чатов, транзакция открыта и зафиксирована внутри вызова — для
		// вызывающих порт ведёт себя как прежде.
		await using var wipe = await BeginRebuildWipeAsync(cancellationToken).ConfigureAwait(false);
		await wipe.DeleteForConstructionAsync(constructionId, cancellationToken).ConfigureAwait(false);
		await wipe.CommitAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <inheritdoc cref="IChatStore.BeginRebuildWipeAsync" />
	// Стирание чатов пересбором делит судьбу с планом журнала: область держит
	// транзакцию хранилища чатов, фиксируемую строго после фиксации плана, —
	// сбой плана откатывает и журнал, и стирание.
	// Traceability: openspec:chats/history#scenario-chat-rebuild-wipes-bound-chats
	public async Task<IChatRebuildWipe> BeginRebuildWipeAsync(CancellationToken cancellationToken = default)
	{
		var db = CreateContext();
		var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
		return new RebuildWipe(db, transaction);
	}

	#region Область стирания пересбором

	/// <summary>
	/// Область стирания над одним короткоживущим контекстом: удаления идут
	/// в одной транзакции, фиксируются явным CommitAsync; закрытие области
	/// без фиксации откатывает их целиком.
	/// </summary>
	private sealed class RebuildWipe : IChatRebuildWipe
	{
		private readonly ChatDbContext _db;
		private readonly IDbContextTransaction _transaction;
		private bool _committed;

		public RebuildWipe(ChatDbContext db, IDbContextTransaction transaction)
		{
			_db = db;
			_transaction = transaction;
		}

		/// <summary>Стирание привязанных чатов: сообщения и чаты конструкции
		/// удаляются, остальные чаты не затрагиваются.</summary>
		public async Task DeleteForConstructionAsync(long constructionId, CancellationToken cancellationToken = default)
		{
			// Чат — запись окружения: пересбор стирает чаты, привязанные к
			// конструкции, вместе с её старой записью; портфельные чаты без
			// привязки и чаты других конструкций пересбор переживают.
			// Traceability: openspec:chats/history#scenario-chat-unbound-chat-survives-rebuild
			var boundChatIds = await _db.Chats
				.Where(chat => chat.ConstructionId == constructionId)
				.Select(chat => chat.Id)
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false);
			if (boundChatIds.Count == 0)
			{
				return;
			}

			// Стирание как у удаления владельцем — целиком без корзины: сначала
			// все сообщения привязанных чатов, затем сами чаты. Чаты без привязки
			// и чаты других конструкций в выборку не попадают и переживают.
			// Traceability: openspec:chats/history#scenario-chat-unbound-chat-survives-rebuild
			await _db.Messages
				.Where(message => boundChatIds.Contains(message.ChatId))
				.ExecuteDeleteAsync(cancellationToken)
				.ConfigureAwait(false);
			await _db.Chats
				.Where(chat => chat.ConstructionId == constructionId)
				.ExecuteDeleteAsync(cancellationToken)
				.ConfigureAwait(false);
		}

		/// <summary>Фиксация области: после неё стёртые чаты не восстанавливаются.</summary>
		public async Task CommitAsync(CancellationToken cancellationToken = default)
		{
			await _transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
			_committed = true;
		}

		/// <summary>Закрытие области: без фиксации транзакция откатывается.</summary>
		public async ValueTask DisposeAsync()
		{
			await using var transaction = _transaction;
			if (_committed == false)
			{
				await transaction.RollbackAsync().ConfigureAwait(false);
			}

			await _db.DisposeAsync().ConfigureAwait(false);
		}
	}

	#endregion

	#region Служебные выборки хранилища

	/// <summary>Загружает чат на изменение; отсутствующий чат — ошибка состояния.</summary>
	/// <param name="db">Контекст базы чатов.</param>
	/// <param name="chatId">Идентификатор чата.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	private static async Task<ChatEntity> LoadChatAsync(
		ChatDbContext db,
		long chatId,
		CancellationToken cancellationToken) =>
		await db.Chats
			.FirstOrDefaultAsync(candidate => candidate.Id == chatId, cancellationToken)
			.ConfigureAwait(false)
			?? throw new InvalidOperationException($"Чат {chatId} не найден.");

	/// <summary>Чаты одного статуса в порядке создания — списки активных и завершённых.</summary>
	/// <param name="status">Отбираемый статус жизненного цикла.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	private async Task<IReadOnlyList<ChatRecord>> ListByStatusAsync(
		ChatStatus status,
		CancellationToken cancellationToken)
	{
		using var db = CreateContext();
		var entities = await db.Chats
			.AsNoTracking()
			.Where(candidate => candidate.Status == status.ToString())
			.OrderBy(candidate => candidate.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return entities.Select(ToRecord).ToList();
	}

	#endregion

	#region Разворот схемы и отображение записей порта

	private ChatDbContext CreateContext()
	{
		var options = new DbContextOptionsBuilder<ChatDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

		// База чатов создаётся по требованию: единая запись окружения не
		// привязана к жизненному циклу конструкций, миграции не предусмотрены.
		var db = new ChatDbContext(options);
		db.Database.EnsureCreated();
		return db;
	}

	private static void EnsureChatId(long chatId)
	{
		if (chatId <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(chatId),
				chatId,
				"Идентификатор чата должен быть положительным.");
		}
	}

	/// <summary>Идентификатор конструкции привязки обязан быть положительным ключом журнала.</summary>
	private static void EnsureConstructionId(long constructionId)
	{
		if (constructionId <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(constructionId),
				constructionId,
				"Идентификатор конструкции должен быть положительным.");
		}
	}

	/// <summary>Модель чата триммируется; пустая модель — ошибка параметров создания.</summary>
	private static string NormalizeModel(string model)
	{
		if (string.IsNullOrWhiteSpace(model))
		{
			throw new ArgumentException("ИИ-модель чата не задана.", nameof(model));
		}

		return model.Trim();
	}

	/// <summary>
	/// Источники нормализуются в подмножество закрытого справочника:
	/// дедупликация с сохранением порядка, повторные категории дают одну
	/// запись; пустой набор — ошибка, чат обязан выбирать хотя бы одну
	/// категорию справочника.
	// Traceability: openspec:chats/sources#scenario-sources-subset-parameter
	/// </summary>
	private static IReadOnlyList<ChatDataSource> NormalizeSources(IReadOnlyList<ChatDataSource> sources)
	{
		ArgumentNullException.ThrowIfNull(sources);
		var normalized = sources.Distinct().ToList();
		if (normalized.Count == 0)
		{
			throw new ArgumentException("Набор источников данных чата не может быть пустым.");
		}

		return normalized;
	}

	/// <summary>Источники хранятся JSON-списком имён категорий справочника — стабильнее числовых кодов.</summary>
	private static string SerializeSources(IReadOnlyList<ChatDataSource> sources)
	{
		var names = sources.Select(source => source.ToString()).ToList();
		return JsonSerializer.Serialize(names, JsonOptions);
	}

	/// <summary>Имя категории справочника, неизвестное справочнику, — повреждённые данные, а не тишина.</summary>
	private static ChatDataSource ParseSource(string name) =>
		Enum.TryParse<ChatDataSource>(name, out var source)
			? source
			: throw new InvalidOperationException($"Неизвестный источник данных чата: «{name}».");

	private static ChatRecord ToRecord(ChatEntity entity) => new()
	{
		Id = entity.Id,
		Model = entity.Model,
		ConstructionId = entity.ConstructionId,
		Sources = JsonSerializer.Deserialize<List<string>>(entity.SourcesJson, JsonOptions)?
				.Select(ParseSource)
				.ToList() ?? [],
		Status = Enum.Parse<ChatStatus>(entity.Status),
		CreatedAt = entity.CreatedAt,
	};

	private static ChatMessage ToRecord(ChatMessageEntity entity) => new()
	{
		Id = entity.Id,
		ChatId = entity.ChatId,
		Role = Enum.Parse<ChatMessageRole>(entity.Role),
		Text = entity.Text,
		AsOf = entity.AsOf,
		SourceTrace = entity.SourceTraceJson == null
			? null
			: JsonSerializer.Deserialize<ChatSourceTrace>(entity.SourceTraceJson, JsonOptions),
	};

	#endregion
}
