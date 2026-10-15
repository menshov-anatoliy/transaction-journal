namespace TransactionJournal.Domain.Data;

/// <summary>
/// Запись оспаривания распознанной синтетической связки — ручные данные
/// пользователя по ключу «конструкция + пара execId линейной и встречной
/// опционной сделок». Оспоренная связка исключается из логического слоя
/// до явного возврата: инкрементная сборка запись переживает, полный
/// пересбор стирает вместе с содержимым конструкций, после чего
/// автоматическое распознавание возвращает связку.
// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#requirement-synthetic-close-dispute
// Traceability: change:synthetic-close-logical-layer/design#d3
/// </summary>
public sealed class SyntheticCloseDispute
{
	/// <summary>Суррогатный ключ строки хранилища.</summary>
	public long Id { get; set; }

	/// <summary>Конструкция, внутри которой оспорена связка.</summary>
	public Construction Construction { get; set; } = null!;

	/// <summary>Внешний ключ конструкции — владельца оспоренной связки.</summary>
	public long ConstructionId { get; set; }

	/// <summary>Биржевой идентификатор линейного исполнения оспоренной связки.</summary>
	public required string LinearExecId { get; set; }

	/// <summary>Биржевой идентификатор встречного опционного исполнения оспоренной связки.</summary>
	public required string CounterOptionExecId { get; set; }

	/// <summary>Момент оспаривания — время создания записи.</summary>
	public required DateTimeOffset DisputedAt { get; set; }
}
