namespace TransactionJournal.Infrastructure.Chats;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
				SourcesJson = JsonSerializer.Serialize(NormalizeSources(start.Sources), JsonOptions),
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
		}

		var entity = new ChatMessageEntity
		{
			Chat = chat,
			Role = message.Role.ToString(),
			Text = message.Text,
			AsOf = message.AsOf,
			MarketTraceJson = message.MarketTrace == null
				? null
				: JsonSerializer.Serialize(message.MarketTrace, JsonOptions),
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

	/// <summary>Модель чата триммируется; пустая модель — ошибка параметров создания.</summary>
	private static string NormalizeModel(string model)
	{
		if (string.IsNullOrWhiteSpace(model))
		{
			throw new ArgumentException("ИИ-модель чата не задана.", nameof(model));
		}

		return model.Trim();
	}

	/// <summary>Источники триммятся и дедуплицируются с сохранением порядка; пустой набор — ошибка.</summary>
	private static IReadOnlyList<string> NormalizeSources(IReadOnlyList<string> sources)
	{
		var normalized = sources
			.Select(source => source.Trim())
			.Where(source => source.Length > 0)
			.Distinct()
			.ToList();
		if (normalized.Count == 0)
		{
			throw new ArgumentException("Набор источников данных чата не может быть пустым.");
		}

		return normalized;
	}

	private static ChatRecord ToRecord(ChatEntity entity) => new()
	{
		Id = entity.Id,
		Model = entity.Model,
		ConstructionId = entity.ConstructionId,
		Sources = JsonSerializer.Deserialize<List<string>>(entity.SourcesJson, JsonOptions) ?? [],
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
		MarketTrace = entity.MarketTraceJson == null
			? null
			: JsonSerializer.Deserialize<ChatMarketTrace>(entity.MarketTraceJson, JsonOptions),
	};

	#endregion
}
