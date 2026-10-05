namespace TransactionJournal.Infrastructure.Consultations;

using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TransactionJournal.Consultations.Ports;

/// <summary>
/// Адаптер порта хранения консультаций: SQLite per construction — по одному
/// файлу базы на конструкцию, таблицы диалогов и сообщений; файл создаётся по
/// требованию и стирается целиком при удалении консультаций конструкции.
/// Изоляция конструкций структурная — уровнем файлов, изоляция диалогов —
/// фильтром сообщений по диалогу. Каждый вызов создаёт короткоживущий
/// контекст, поэтому хранилище безопасно в длительных сессиях Blazor Server.
// Traceability: openspec:consultations/history#requirement-history-environment-record
/// </summary>
public sealed class ConsultationStore : IConsultationStore
{
	// Компактный JSON: рыночный след читается адаптером, а не человеком.
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		WriteIndented = false,
	};

	private readonly string _directory;

	/// <summary>Создаёт хранилище над папкой per-construction баз; папка создаётся сразу.</summary>
	/// <param name="directory">Папка файлов баз конструкций (например App_Data/consultations).</param>
	/// <exception cref="ArgumentException">Путь папки пуст.</exception>
	public ConsultationStore(string directory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		_directory = directory;
		Directory.CreateDirectory(directory);
	}

	/// <inheritdoc cref="IConsultationStore.ListDialoguesAsync" />
	public async Task<IReadOnlyList<ConsultationDialogue>> ListDialoguesAsync(
		long constructionId,
		CancellationToken cancellationToken = default)
	{
		EnsureConstructionId(constructionId);
		using var db = CreateContext(constructionId);
		var entities = await db.Dialogues
			.AsNoTracking()
			.OrderBy(dialogue => dialogue.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return entities.Select(entity => ToDialogue(entity, constructionId)).ToList();
	}

	/// <inheritdoc cref="IConsultationStore.AppendMessageAsync" />
	public async Task<ConsultationMessage> AppendMessageAsync(
		long constructionId,
		long? dialogueId,
		ConsultationMessageDraft message,
		CancellationToken cancellationToken = default)
	{
		EnsureConstructionId(constructionId);
		ArgumentNullException.ThrowIfNull(message);

		using var db = CreateContext(constructionId);
		ConsultationDialogueEntity dialogue;
		if (dialogueId is null)
		{
			// Диалог без идентификатора создаётся самим сообщением: отдельной
			// команды создания нет, а момент создания — as-of первого сообщения.
			// Traceability: openspec:consultations/history#scenario-history-created-by-first-message
			dialogue = new ConsultationDialogueEntity { CreatedAt = message.AsOf };
		}
		else
		{
			dialogue = await db.Dialogues
				.FirstOrDefaultAsync(candidate => candidate.Id == dialogueId.Value, cancellationToken)
				.ConfigureAwait(false)
				?? throw new InvalidOperationException(
					$"Диалог {dialogueId} не найден в консультации конструкции {constructionId}.");
		}

		var entity = new ConsultationMessageEntity
		{
			Dialogue = dialogue,
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

	/// <inheritdoc cref="IConsultationStore.ListMessagesAsync" />
	public async Task<IReadOnlyList<ConsultationMessage>> ListMessagesAsync(
		long constructionId,
		long dialogueId,
		CancellationToken cancellationToken = default)
	{
		EnsureConstructionId(constructionId);
		using var db = CreateContext(constructionId);

		// Изоляция историй: выбираются только сообщения своего диалога — соседние
		// диалоги, даже той же конструкции, в выборку не попадают.
		// Traceability: openspec:consultations/history#scenario-history-dialogues-isolated
		var entities = await db.Messages
			.AsNoTracking()
			.Where(entity => entity.DialogueId == dialogueId)
			.OrderBy(entity => entity.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		return entities.Select(ToRecord).ToList();
	}

	/// <inheritdoc cref="IConsultationStore.DeleteDialogueAsync" />
	public async Task<bool> DeleteDialogueAsync(
		long constructionId,
		long dialogueId,
		CancellationToken cancellationToken = default)
	{
		EnsureConstructionId(constructionId);
		using var db = CreateContext(constructionId);
		var dialogue = await db.Dialogues
			.FirstOrDefaultAsync(candidate => candidate.Id == dialogueId, cancellationToken)
			.ConfigureAwait(false);
		if (dialogue == null)
		{
			return false;
		}

		// Hard delete: сообщения вычищаются вместе с диалогом одним сохранением
		// без корзины и восстановления; диалог другой конструкции живёт в своём
		// файле и отсюда не виден — на его идентификатор вернётся false.
		// Traceability: openspec:consultations/history#scenario-history-hard-delete-dialogue
		db.Messages.RemoveRange(db.Messages.Where(entity => entity.DialogueId == dialogueId));
		db.Dialogues.Remove(dialogue);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return true;
	}

	/// <inheritdoc cref="IConsultationStore.DeleteForConstructionAsync" />
	public Task DeleteForConstructionAsync(long constructionId, CancellationToken cancellationToken = default)
	{
		EnsureConstructionId(constructionId);
		var path = DatabasePath(constructionId);
		if (File.Exists(path) == false)
		{
			return Task.CompletedTask;
		}

		// Консультации конструкции — её файл базы целиком: вместе со старой
		// записью конструкции стираются все диалоги и сообщения; пул соединений
		// файла сбрасывается, иначе держатель не даст удалить файл в Windows.
		// Traceability: openspec:consultations/history#scenario-history-rebuild-wipes
		SqliteConnection.ClearPool(new SqliteConnection($"Data Source={path}"));
		foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
		{
			var file = path + suffix;
			if (File.Exists(file))
			{
				File.Delete(file);
			}
		}

		return Task.CompletedTask;
	}

	#region Инфраструктура файла базы и отображение записей порта

	private ConsultationDbContext CreateContext(long constructionId)
	{
		var options = new DbContextOptionsBuilder<ConsultationDbContext>()
			.UseSqlite($"Data Source={DatabasePath(constructionId)}")
			.Options;

		// База конструкции создаётся по требованию: запись окружения одноразова
		// и стирается пересбором, поэтому миграции для неё не предусмотрены.
		var db = new ConsultationDbContext(options);
		db.Database.EnsureCreated();
		return db;
	}

	private string DatabasePath(long constructionId) =>
		Path.Combine(_directory, $"construction-{constructionId}.db");

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

	private static ConsultationDialogue ToDialogue(ConsultationDialogueEntity entity, long constructionId) => new()
	{
		Id = entity.Id,
		ConstructionId = constructionId,
		CreatedAt = entity.CreatedAt,
	};

	private static ConsultationMessage ToRecord(ConsultationMessageEntity entity) => new()
	{
		Id = entity.Id,
		DialogueId = entity.DialogueId,
		Role = Enum.Parse<ConsultationMessageRole>(entity.Role),
		Text = entity.Text,
		AsOf = entity.AsOf,
		MarketTrace = entity.MarketTraceJson == null
			? null
			: JsonSerializer.Deserialize<ConsultationMarketTrace>(entity.MarketTraceJson, JsonOptions),
	};

	#endregion
}
