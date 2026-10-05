namespace TransactionJournal.Infrastructure.Consultations;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF-контекст SQLite-базы консультаций одной конструкции: таблицы диалогов
/// и сообщений. База создаётся по требованию и стирается целиком при удалении
/// консультаций конструкции, поэтому миграции для неё не предусмотрены —
/// схема разворачивается EnsureCreated, а сама запись окружения одноразова.
// Traceability: openspec:consultations/history#requirement-history-environment-record
/// </summary>
internal sealed class ConsultationDbContext : DbContext
{
	/// <summary>Создаёт контекст над опциями SQLite-базы конкретной конструкции.</summary>
	/// <param name="options">Опции EF-контекста.</param>
	public ConsultationDbContext(DbContextOptions<ConsultationDbContext> options)
		: base(options)
	{
	}

	/// <summary>Диалоги конструкции в порядке создания.</summary>
	public DbSet<ConsultationDialogueEntity> Dialogues => Set<ConsultationDialogueEntity>();

	/// <summary>Сообщения диалогов конструкции.</summary>
	public DbSet<ConsultationMessageEntity> Messages => Set<ConsultationMessageEntity>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		// Сообщения выбираются списком по диалогу — индекс по внешнему ключу.
		modelBuilder.Entity<ConsultationMessageEntity>()
			.HasIndex(message => message.DialogueId);
	}
}
