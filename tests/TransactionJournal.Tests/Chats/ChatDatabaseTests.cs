namespace TransactionJournal.Tests.Chats;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using TransactionJournal.Infrastructure.Chats;

/// <summary>
/// Каркас тестов чатов над файловой базой SQLite: каждая проверка получает
/// собственную пустую базу во временной папке, по завершении файл базы
/// вместе с соседними -wal/-shm удаляется. Пул соединений SQLite держит
/// файл открытым, поэтому перед удалением пул сбрасывается.
/// </summary>
[TestClass]
public abstract class ChatDatabaseTests
{
	/// <summary>Фиксированный момент as-of сообщений и рыночных данных проверок чатов.</summary>
	protected static readonly DateTimeOffset FixedNow = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

	private string _databasePath = null!;

	[TestInitialize]
	public void InitializeChatDatabase()
	{
		// Каждая проверка работает со своей пустой базой чатов во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"chat-tests-{Guid.NewGuid():N}.db");
	}

	[TestCleanup]
	public void CleanupChatDatabase()
	{
		// Пул соединений SQLite держит файл базы открытым — сбрасываем перед удалением.
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
		foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
		{
			var file = _databasePath + suffix;
			if (File.Exists(file))
			{
				File.Delete(file);
			}
		}
	}

	/// <summary>Хранилище над файлом базы проверки: каждое обращение создаёт независимый контекст.</summary>
	protected ChatStore CreateStore() => new(_databasePath);
}
