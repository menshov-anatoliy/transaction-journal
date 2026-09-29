namespace TransactionJournal.Ops;

/// <summary>
/// Опции подсистемы резервного копирования журнала: каталог копий и лимит
/// одновременно хранимых копий. Собираются при старте приложения
/// <see cref="BackupOptionsReader.Read"/>; сервис копирования их не меняет.
/// </summary>
public sealed record JournalBackupOptions
{
	/// <summary>
	/// Каталог резервных копий. Относительный путь разрешается от корня содержимого
	/// приложения при регистрации сервиса; создаётся лени при первом бэкапе.
	/// </summary>
	public string Directory { get; init; } = BackupOptionsReader.DefaultDirectory;

	/// <summary>Лимит одновременно хранимых копий; старейшие сверх лимита удаляются.</summary>
	public int RetentionLimit { get; init; } = BackupOptionsReader.DefaultRetentionLimit;
}
