namespace TransactionJournal.Infrastructure.Consultations;

/// <summary>
/// Строка таблицы диалогов в SQLite-базе конструкции — запись окружения чата.
/// Сущность живёт в Infrastructure: проект Consultations не ссылается на EF,
/// а домен о консультациях не знает; в запись порта строка отображается
/// адаптером хранилища. База создаётся на конструкцию, поэтому отдельного
/// столбца идентификатора конструкции у диалога нет — изоляция конструкций
/// структурная, уровнем файлов.
// Traceability: openspec:consultations/history#requirement-history-environment-record
/// </summary>
internal sealed class ConsultationDialogueEntity
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>Момент создания диалога — as-of первого сообщения владельца.</summary>
	public required DateTimeOffset CreatedAt { get; set; }
}
