namespace TransactionJournal.Infrastructure.Chats;

using Microsoft.EntityFrameworkCore;

/// <summary>
/// EF-контекст единой SQLite-базы чатов агента: таблицы чатов и сообщений
/// вместо per-construction баз консультаций. База разворачивается по
/// требованию и не привязана к жизненному циклу записей журнала, поэтому
/// миграции для неё не предусмотрены — схема разворачивается EnsureCreated,
/// по образцу записи окружения консультаций.
// Traceability: openspec:chats/history#requirement-chat-environment-record
/// </summary>
internal sealed class ChatDbContext : DbContext
{
	/// <summary>Создаёт контекст над опциями единой SQLite-базы чатов.</summary>
	/// <param name="options">Опции EF-контекста.</param>
	public ChatDbContext(DbContextOptions<ChatDbContext> options)
		: base(options)
	{
	}

	/// <summary>Чаты агента с параметрами и статусом.</summary>
	public DbSet<ChatEntity> Chats => Set<ChatEntity>();

	/// <summary>Сообщения чатов с as-of и следом источников.</summary>
	public DbSet<ChatMessageEntity> Messages => Set<ChatMessageEntity>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		// Сообщения выбираются списком по чату — индекс по внешнему ключу.
		modelBuilder.Entity<ChatMessageEntity>()
			.HasIndex(message => message.ChatId);
	}
}
