namespace TransactionJournal.Hints;

using TransactionJournal.Hints.Ports;

/// <summary>
/// Каркас прохода агента подсказок. Проход детерминирован: отбор подсказок
/// делает код по ключам триггеров корпуса, LLM не участвует. Фазы прохода
/// выполняются в строгом порядке: снимок журнала → рыночные марки → оценка
/// триггеров → дедуп и запись; недоступность марок пропускает проход целиком.
/// Загрузчик корпуса (предусловие валидности), реестр триггеров и дедуп-окно
/// наращиваются следующими задачами change.
// Traceability: openspec:hints/engine-pass#requirement-engine-deterministic-selection
/// </summary>
public sealed class HintAgentPass
{
	private readonly IJournalSnapshotReader _journalSnapshotReader;

	private readonly IMarkSource _markSource;

	private readonly IHintStore _hintStore;

	private readonly IClock _clock;

	/// <summary>Создаёт проход над портами окружения агента.</summary>
	/// <param name="journalSnapshotReader">Порт журнал-снапшота.</param>
	/// <param name="markSource">Порт рыночных марок.</param>
	/// <param name="hintStore">Порт хранения подсказок.</param>
	/// <param name="clock">Часы окружения — источник as-of прохода.</param>
	/// <exception cref="ArgumentNullException">Порт не задан.</exception>
	public HintAgentPass(
		IJournalSnapshotReader journalSnapshotReader,
		IMarkSource markSource,
		IHintStore hintStore,
		IClock clock)
	{
		_journalSnapshotReader = journalSnapshotReader ?? throw new ArgumentNullException(nameof(journalSnapshotReader));
		_markSource = markSource ?? throw new ArgumentNullException(nameof(markSource));
		_hintStore = hintStore ?? throw new ArgumentNullException(nameof(hintStore));
		_clock = clock ?? throw new ArgumentNullException(nameof(clock));
	}

	/// <summary>Выполняет проход агента над текущими журналом, марками и корпусом.</summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Итог прохода с исходом и счётчиками записей.</returns>
	public async Task<HintPassResult> RunAsync(CancellationToken cancellationToken = default)
	{
		var asOf = _clock.UtcNow;

		// Фаза 1: снимок журнала — данные триггеров строятся до любых решений прохода.
		var snapshot = await _journalSnapshotReader.ReadAsync(cancellationToken).ConfigureAwait(false);

		// Марки нужны только открытым остаткам открытых конструкций — минимальный
		// набор символов прохода; непериодные журнальные триггеры марок не требуют.
		var symbols = snapshot.Constructions
			.Where(construction => construction.IsOpen)
			.SelectMany(construction => construction.Positions)
			.Where(position => position.IsOpen)
			.Select(position => position.Symbol)
			.Distinct(StringComparer.Ordinal)
			.ToArray();

		// Фаза 2: рыночные марки. Недоступность источника пропускает проход
		// с диагностикой — подсказки на неполных данных не выпускаются.
		// Traceability: openspec:hints/engine-pass#requirement-engine-market-unavailable-skips-pass
		var marks = await _markSource.GetMarksAsync(symbols, cancellationToken).ConfigureAwait(false);
		if (marks.IsAvailable == false)
		{
			return new HintPassResult
			{
				Outcome = HintPassOutcome.SkippedMarketUnavailable,
				AsOf = asOf,
				Diagnostics = [marks.FailureReason!],
			};
		}

		// Фазы 3-5 (оценка триггеров, дедуп-окно и запись кандидатов) наращиваются
		// задачами 3.x и 4.x; каркас возвращает нейтральный итог без записей.
		return new HintPassResult
		{
			Outcome = HintPassOutcome.Completed,
			AsOf = asOf,
			CreatedHints = 0,
			ExpiredHints = 0,
		};
	}
}

/// <summary>Исход прохода агента: завершён, пропущен из-за марок или сломан корпусом.</summary>
public enum HintPassOutcome
{
	/// <summary>Проход завершён: триггеры оценены, дедуп применён, записи сохранены.</summary>
	Completed,

	/// <summary>Проход пропущен: рыночные данные недоступны, подсказки не выпускались.</summary>
	SkippedMarketUnavailable,

	/// <summary>Проход сломан невалидным корпусом правил до любых чтений журнала и рынка.</summary>
	CorpusInvalid,
}

/// <summary>Итог прохода агента: исход, отметка времени, счётчики записей и диагностика.</summary>
public sealed record HintPassResult
{
	/// <summary>Исход прохода.</summary>
	public required HintPassOutcome Outcome { get; init; }

	/// <summary>Отметка as-of начала прохода.</summary>
	public required DateTimeOffset AsOf { get; init; }

	/// <summary>Созданных записей подсказок за проход.</summary>
	public int CreatedHints { get; init; }

	/// <summary>Погашенных в expired записей за проход.</summary>
	public int ExpiredHints { get; init; }

	/// <summary>Диагностика прохода: причина пропуска, проблемы корпуса; null — диагностик нет.</summary>
	public IReadOnlyList<string>? Diagnostics { get; init; }
}
