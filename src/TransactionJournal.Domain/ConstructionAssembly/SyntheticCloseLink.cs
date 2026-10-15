using TransactionJournal.Domain.Materialization;

namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Распознанная синтетическая связка конструкции: пара «линейное исполнение +
/// встречное опционное исполнение», закрывающая часть длинной опционной ноги
/// по шаблону правила ac-43. Связка — результат чистой функции распознавания:
/// она вычисляется при чтении, не хранится в сборке и не меняет
/// инструментальных остатков ног.
/// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#requirement-synthetic-close-recognition-deterministic
/// Traceability: change:synthetic-close-logical-layer/design#d1
/// </summary>
public sealed record SyntheticCloseLink
{
	/// <summary>Биржевой идентификатор линейного исполнения связки.</summary>
	public required string LinearExecId { get; init; }

	/// <summary>Время линейного исполнения связки, мс Unix-эпохи.</summary>
	public required long LinearExecTimeMs { get; init; }

	/// <summary>Биржевой идентификатор встречного опционного исполнения связки.</summary>
	public required string CounterOptionExecId { get; init; }

	/// <summary>Время встречного опционного исполнения связки, мс Unix-эпохи.</summary>
	public required long CounterOptionExecTimeMs { get; init; }

	/// <summary>Базовый актив связки: совпадает у линейной сделки, встречного опциона и закрываемой ноги.</summary>
	public required string BaseCoin { get; init; }

	/// <summary>Символ закрываемой опционной ноги — тот инструмент, который в связке не продаётся.</summary>
	public required string ClosedLegSymbol { get; init; }

	/// <summary>Тип закрываемой ноги: противоположен встречному опциону.</summary>
	public required OptionType ClosedLegType { get; init; }

	/// <summary>Страйк доски связки: совпадает у встречного опциона и закрываемой ноги.</summary>
	public required decimal Strike { get; init; }

	/// <summary>Доска связки — дата экспирации, совпадающая у встречного опциона и закрываемой ноги.</summary>
	public required DateTime BoardExpiryDate { get; init; }

	/// <summary>Количество связки: закрываемая часть ноги, равная количествам линейной сделки и встречного опциона.</summary>
	public required decimal Quantity { get; init; }
}
