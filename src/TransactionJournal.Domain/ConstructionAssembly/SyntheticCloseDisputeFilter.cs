using TransactionJournal.Domain.Data;

namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Чистое правило исключения оспоренных связок из результата распознавания:
/// спор по паре execId «линейное исполнение + встречный опцион» скрывает
/// связку из логического слоя — логическая нога возвращается к полному
/// размеру, строка «зафиксировано синтетикой» исчезает. Возврат записи
/// возвращает связку ближайшему чтению. Инструментальные остатки правило
/// не трогает: распознавание остаётся чистой функцией записей.
// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-dispute-hides-logical-link
/// Traceability: change:synthetic-close-logical-layer/design#d3
/// </summary>
public static class SyntheticCloseDisputeFilter
{
	/// <summary>
	/// Исключает из связок те, чья пара execId оспорена. Порядок неоспоренных
	/// связок сохраняется: повторное чтение над тем же входом детерминировано.
	/// </summary>
	/// <param name="links">Связки, распознанные над записями конструкции.</param>
	/// <param name="disputes">Записи оспаривания конструкции.</param>
	/// <exception cref="ArgumentNullException">Связки или споры не заданы.</exception>
	// Спор ключуется парой execId: execId стабилен и глобально уникален,
	// поэтому совпадение пары однозначно указывает на ту же связку.
	// Traceability: change:synthetic-close-logical-layer/design#d3
	public static IReadOnlyList<SyntheticCloseLink> ExcludeDisputed(
		IReadOnlyList<SyntheticCloseLink> links,
		IReadOnlyCollection<SyntheticCloseDispute> disputes)
	{
		ArgumentNullException.ThrowIfNull(links);
		ArgumentNullException.ThrowIfNull(disputes);

		// Ключ спора — пара execId связки; состав неоспоренных повторяет порядок
		// распознанного прогона, логический слой остаётся детерминированным.
		var disputedPairs = disputes
			.Select(dispute => new LinkKey(dispute.LinearExecId, dispute.CounterOptionExecId))
			.ToHashSet();
		return links
			.Where(link => disputedPairs.Contains(new LinkKey(link.LinearExecId, link.CounterOptionExecId)) == false)
			.ToList();
	}

	/// <summary>Ключ связки — пара execId линейного и встречного опционного исполнений.</summary>
	private readonly record struct LinkKey(string LinearExecId, string CounterOptionExecId);
}
