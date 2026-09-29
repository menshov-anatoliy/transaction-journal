namespace TransactionJournal.Ops;

/// <summary>
/// Сервис резервного копирования журнала: единственная точка создания копий базы
/// перед операциями, значимо манипулирующими данными (полный пересбор, синхронизация,
/// удаление конструкции). Один вызов создаёт консистентный снапшот работающей базы
/// в каталоге копий, выполняет ротацию и возвращает имя созданной копии и предупреждения
/// ротации для отображения пользователю.
/// Traceability: openspec:ops/db-backup#requirement-backup-consistent-snapshot
/// </summary>
public interface IJournalBackupService
{
	/// <summary>
	/// Создаёт резервную копию базы журнала снапшотом работающей базы и ротирует
	/// хранимые копии до лимита. Ошибка создания копии проходит наружу исключением —
	/// вызывающая операция обязана прерваться; ошибки ротации возвращаются
	/// предупреждениями и операцию не отменяют.
	/// </summary>
	/// <param name="reason">Причина копии, попадающая в имя файла: <c>rebuild</c>, <c>sync</c> или <c>delete-construction</c>.</param>
	/// <param name="cancellationToken">Токен отмены копирования.</param>
	/// <exception cref="ArgumentException">Причина пуста или непригодна для имени файла.</exception>
	/// <exception cref="IOException">Снапшот создать не удалось.</exception>
	Task<JournalBackupResult> CreateBackupAsync(string reason, CancellationToken cancellationToken = default);
}
