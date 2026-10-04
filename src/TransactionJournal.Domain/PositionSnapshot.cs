namespace TransactionJournal.Domain;

/// <summary>
/// Позиция — производный чистый остаток по одному инструменту внутри конструкции:
/// агрегат сделок конструкции по этому инструменту и закрывающих записей. Позиция
/// не редактируется напрямую — количество меняется только сделками и закрывающими
/// записями, поэтому снапшот immutable и пересчитывается при каждом чтении.
// Traceability: openspec:domain/constructions#requirement-position-derived-residual
// Traceability: change:add-core-domain/design#d1
/// </summary>
public sealed record PositionSnapshot
{
	/// <summary>Конструкция, внутри которой вычислена позиция.</summary>
	public required long ConstructionId { get; init; }

	/// <summary>Инструмент позиции.</summary>
	public required string Symbol { get; init; }

	/// <summary>
	/// Чистый остаток: сумма знаковых количеств сделок и применённых закрывающих
	/// записей; положителен для длинной позиции, отрицателен для короткой.
	/// </summary>
	public required decimal Residual { get; init; }

	/// <summary>Позиция открыта, пока остаток не нулевой; нулевой остаток — позиция закрыта.</summary>
	public required bool IsOpen { get; init; }
}
