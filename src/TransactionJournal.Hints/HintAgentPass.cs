namespace TransactionJournal.Hints;

using TransactionJournal.Domain.Materialization;
using TransactionJournal.Hints.Corpus;
using TransactionJournal.Hints.Engine;
using TransactionJournal.Hints.Ports;

/// <summary>
/// Каркас прохода агента подсказок. Проход детерминирован: отбор подсказок
/// делает код по ключам триггеров корпуса, LLM не участвует. Фазы прохода
/// выполняются в строгом порядке: снимок корпуса → снимок журнала → рыночные
/// марки → оценка триггеров → рендер и запись кандидатов; невалидный корпус
/// ломает проход до любых чтений, недоступность марок пропускает проход
/// целиком. Дедуп-окно наращивается следующей задачей change.
// Traceability: openspec:hints/engine-pass#requirement-engine-deterministic-selection
/// </summary>
public sealed class HintAgentPass
{
	private readonly RulesCorpusLoader _corpusLoader;

	private readonly IJournalSnapshotReader _journalSnapshotReader;

	private readonly IMarkSource _markSource;

	private readonly IHintStore _hintStore;

	private readonly IClock _clock;

	private readonly HintTriggerRegistry _triggerRegistry;

	/// <summary>Создаёт проход над портами окружения агента.</summary>
	/// <param name="corpusLoader">Загрузчик корпуса правил — предусловие валидности прохода.</param>
	/// <param name="journalSnapshotReader">Порт журнал-снапшота.</param>
	/// <param name="markSource">Порт рыночных марок.</param>
	/// <param name="hintStore">Порт хранения подсказок.</param>
	/// <param name="clock">Часы окружения — источник as-of прохода.</param>
	/// <param name="triggerRegistry">Реестр триггеров; null — полный набор машинных триггеров v1.</param>
	/// <exception cref="ArgumentNullException">Порт не задан.</exception>
	public HintAgentPass(
		RulesCorpusLoader corpusLoader,
		IJournalSnapshotReader journalSnapshotReader,
		IMarkSource markSource,
		IHintStore hintStore,
		IClock clock,
		HintTriggerRegistry? triggerRegistry = null)
	{
		_corpusLoader = corpusLoader ?? throw new ArgumentNullException(nameof(corpusLoader));
		_journalSnapshotReader = journalSnapshotReader ?? throw new ArgumentNullException(nameof(journalSnapshotReader));
		_markSource = markSource ?? throw new ArgumentNullException(nameof(markSource));
		_hintStore = hintStore ?? throw new ArgumentNullException(nameof(hintStore));
		_clock = clock ?? throw new ArgumentNullException(nameof(clock));
		_triggerRegistry = triggerRegistry ?? new HintTriggerRegistry();
	}

	/// <summary>Выполняет проход агента над текущими журналом, марками и корпусом.</summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Итог прохода с исходом и счётчиками записей.</returns>
	public async Task<HintPassResult> RunAsync(CancellationToken cancellationToken = default)
	{
		var asOf = _clock.UtcNow;

		// Фаза 0: снимок корпуса — предусловие валидности прохода. Невалидный
		// корпус ломает проход до построения снимка и любых чтений журнала и
		// рынка: ни подсказки, ни записи не производятся; хост продолжает
		// работать, диагностика видна на входе запуска.
		// Traceability: openspec:hints/rules-corpus#requirement-corpus-validity-precondition
		RulesCorpusSnapshot corpus;
		try
		{
			corpus = _corpusLoader.Load();
		}
		catch (CorpusInvalidException ex)
		{
			return new HintPassResult
			{
				Outcome = HintPassOutcome.CorpusInvalid,
				AsOf = asOf,
				Diagnostics = ex.Problems,
			};
		}

		// Активные правила без машинной реализации (implementation: null или
		// неизвестный движку ключ) валидны: подсказок не порождают, проход
		// продолжается; их идентификаторы уходят в чек-лист сводки и лог
		// «непокрытых кодом».
		// Traceability: openspec:hints/rules-corpus#requirement-corpus-unimplemented-trigger-checklist
		var unimplementedRuleIds = corpus.UnimplementedCards
			.Select(card => card.Id)
			.ToArray();

		// Фаза 1: снимок журнала — данные триггеров строятся до любых решений прохода.
		var snapshot = await _journalSnapshotReader.ReadAsync(cancellationToken).ConfigureAwait(false);

		// Марки нужны открытым остаткам открытых конструкций и спотам базовых
		// активов их опционных ног (условия ролла, распада и ITM сравнивают
		// спот со страйками) — минимальный набор символов прохода; спот базового
		// актива берётся линейным фьючерсом «{база}USDT» по конвенции парсера.
		var symbols = snapshot.Constructions
			.Where(construction => construction.IsOpen)
			.SelectMany(construction => construction.Positions)
			.Where(position => position.IsOpen)
			.Select(position => position.Symbol)
			.Concat(snapshot.Constructions
				.Where(construction => construction.IsOpen)
				.SelectMany(construction => construction.Positions)
				.Where(position => position.IsOpen)
				.Select(position => OptionSymbolParser.TryParse(position.Symbol, out var parts) && parts is not null
					? parts.BaseCoin + "USDT"
					: null))
			.OfType<string>()
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

		// Фаза 3: оценка триггеров активных правил с машинной реализацией.
		// Журнальные триггеры оцениваются один раз по всему снимку (субъект
		// «журнал»), триггеры конструкций — по каждой открытой конструкции;
		// вид субъекта объявляет триггер, а не скоуп карточки.
		// Traceability: openspec:hints/engine-pass#requirement-engine-deterministic-selection
		// Traceability: openspec:hints/hint-lifecycle#requirement-hint-subject-v1-closed-set
		var openConstructions = snapshot.Constructions
			.Where(construction => construction.IsOpen)
			.ToArray();
		var createdHints = 0;
		foreach (var card in corpus.ExecutableCards)
		{
			if (_triggerRegistry.TryResolve(card.TriggerImplementation!, out var trigger) == false)
			{
				continue;
			}

			if (trigger.SubjectKind == HintSubjectKind.Journal)
			{
				var input = new TriggerEvaluationInput
				{
					Snapshot = snapshot,
					Marks = marks,
					Card = card,
					AsOf = asOf,
				};
				createdHints += await CreateHintAsync(trigger, input, asOf, cancellationToken).ConfigureAwait(false);
			}
			else
			{
				foreach (var construction in openConstructions)
				{
					var input = new TriggerEvaluationInput
					{
						Snapshot = snapshot,
						Marks = marks,
						Card = card,
						AsOf = asOf,
						Construction = construction,
					};
					createdHints += await CreateHintAsync(trigger, input, asOf, cancellationToken).ConfigureAwait(false);
				}
			}
		}

		// Фаза 5: записи сохранены. Дедуп-окно пока не применяется (задача 4.1):
		// каждый сработавший триггер выпускает новую запись — временное поведение
		// до включения окна «правило × субъект (+ период)».
		// Traceability: change:add-hints-engine/tasks#4-группа-дедуп-и-гашение
		return new HintPassResult
		{
			Outcome = HintPassOutcome.Completed,
			AsOf = asOf,
			CreatedHints = createdHints,
			ExpiredHints = 0,
			UnimplementedRuleIds = unimplementedRuleIds,
		};
	}

	/// <summary>
	/// Рендерит и сохраняет подсказку сработавшего триггера; не сработавший
	/// триггер записи не создаёт. Запись денормализована: характер, чёткость,
	/// источники и текст фиксируются на момент прохода, позднейшие правки
	/// карточки историю не искажают.
	// Traceability: openspec:hints/engine-pass#requirement-engine-self-describing-record
	// Traceability: openspec:hints/engine-pass#requirement-engine-clarity-shapes-wording
	/// </summary>
	private async Task<int> CreateHintAsync(IHintTrigger trigger, TriggerEvaluationInput input, DateTimeOffset asOf, CancellationToken cancellationToken)
	{
		var outcome = trigger.Evaluate(input);
		if (outcome.Fired == false)
		{
			return 0;
		}

		var card = input.Card;
		var record = new HintRecord
		{
			RuleId = card.Id,
			Subject = trigger.SubjectKind == HintSubjectKind.Journal
				? HintSubject.ForJournal()
				: HintSubject.ForConstruction(input.Construction!.Id),
			Character = card.Character,
			Clarity = HintTextRenderer.ClarityText(card.Clarity),
			Sources = card.Sources
				.Select(source => new HintSourceTag { Tag = source.Tag, File = source.File, Quotes = source.Quotes })
				.ToArray(),
			Text = HintTextRenderer.Render(card, outcome.Facts),
			Facts = outcome.Facts,
			AsOf = asOf,
			Status = HintStatus.New,
			WindowPeriodKey = outcome.WindowPeriodKey,
		};
		await _hintStore.AddAsync(record, cancellationToken).ConfigureAwait(false);
		return 1;
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

	/// <summary>
	/// Идентификаторы активных правил без машинной реализации — чек-лист сводки
	/// и лог «непокрытых кодом»; проход при них продолжается.
	// Traceability: openspec:hints/rules-corpus#requirement-corpus-unimplemented-trigger-checklist
	/// </summary>
	public IReadOnlyList<string> UnimplementedRuleIds { get; init; } = Array.Empty<string>();
}
