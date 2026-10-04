namespace TransactionJournal.Hints.Corpus;

/// <summary>
/// Неизменяемый снимок корпуса правил одного прохода: карточки разбиты по
/// назначению — машинные триггеры, чек-лист правил без реализации и retired
/// для гашения живых записей. Снимок строится заново при каждом проходе;
/// правки файлов корпуса действуют со следующего прохода без пересборки.
/// Traceability: openspec:hints/rules-corpus#requirement-corpus-snapshot-per-pass
/// </summary>
public sealed record RulesCorpusSnapshot
{
	private static readonly IReadOnlyList<RuleCard> EmptyCards = new List<RuleCard>();

	/// <summary>Активные карточки с известным движку машинным ключом — исполняются триггерами.</summary>
	public IReadOnlyList<RuleCard> ExecutableCards { get; init; } = EmptyCards;

	/// <summary>
	/// Активные карточки без машинной реализации (implementation: null или
	/// неизвестный ключ): подсказок не порождают, входят в чек-лист сводки
	/// и лог «непокрытых кодом».
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-unimplemented-trigger-checklist
	/// </summary>
	public IReadOnlyList<RuleCard> UnimplementedCards { get; init; } = EmptyCards;

	/// <summary>Retired-карточки: не исполняются, служат только гашению живых записей подсказок.</summary>
	public IReadOnlyList<RuleCard> RetiredCards { get; init; } = EmptyCards;
}
