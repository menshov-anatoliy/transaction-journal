namespace TransactionJournal.Ops;

/// <summary>
/// Итог создания резервной копии: имя созданного файла в каталоге копий и
/// предупреждения ротации — неудавшиеся удаления старых копий, не отменившие
/// ни копию, ни защищаемую ею операцию.
/// </summary>
public sealed record JournalBackupResult
{
	/// <summary>Имя созданного файла копии в каталоге резервных копий.</summary>
	public required string FileName { get; init; }

	/// <summary>Предупреждения ротации «лучшие усилия»; пустой список — ротация прошла без ошибок.</summary>
	public IReadOnlyList<string> RotationWarnings { get; init; } = [];
}
