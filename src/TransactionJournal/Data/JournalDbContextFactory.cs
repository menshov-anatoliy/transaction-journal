using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TransactionJournal.Data;

/// <summary>
/// Фабрика контекста для инструментов design-time (dotnet ef).
/// Хост-приложение собирается через WebApplication.CreateBuilder, поэтому
/// миграции создаются через отдельную точку входа с явными опциями.
/// </summary>
public sealed class JournalDbContextFactory : IDesignTimeDbContextFactory<JournalDbContext>
{
	/// <summary>Создаёт контекст с подключением к временной базе; для генерации миграций реальная база не нужна.</summary>
	public JournalDbContext CreateDbContext(string[] args)
	{
		var options = new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite("Data Source=journal.design.db")
			.Options;

		return new JournalDbContext(options);
	}
}
