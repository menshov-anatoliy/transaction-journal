namespace TransactionJournal.Hints;

using TransactionJournal.Domain.Materialization;
using TransactionJournal.Hints.Corpus;
using TransactionJournal.Hints.Engine;
using TransactionJournal.Hints.Ports;

/// <summary>
/// Каркас прохода агента подсказок. Проход детерминирован: отбор подсказок
/// делает код по ключам триггеров корпуса, LLM не участвует. Фазы прохода
/// выполняются в строгом порядке: снимок корпуса → гашение выведенных правил →
/// снимок журнала → гашение закрытых субъектов → рыночные марки → оценка
/// триггеров с дедуп-окном; невалидный корпус ломает проход до любых чтений,
/// недоступность марок пропускает проход целиком. Новая подсказка подавляется
/// записью текущего окна «правило × субъект (+ период)» с любым живым
/// статусом; ушедшее условие, закрытый субъект и выведенное правило гасят
/// записи в expired.
// Traceability: openspec:hints/engine-pass#requirement-engine-deterministic-selection
// Traceability: openspec:hints/hint-lifecycle#requirement-hint-dedup-rule-subject-window
/// </summary>
public sealed class HintAgentPass : IHintPassRunner
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

		// Фаза 1: гашение живых записей выведенных правил — retired-карточек
		// и карточек, удалённых из корпуса. Гашение выполняется при загрузке
		// снимка корпуса; журнал сделок и рыночные данные не участвуют.
		// Traceability: openspec:hints/hint-lifecycle#requirement-hint-expiry-on-subject-close-and-retirement
		var expiredHints = await ExpireWithdrawnRulesAsync(corpus, cancellationToken).ConfigureAwait(false);

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

		// Фаза 2: гашение записей закрытых субъектов-конструкций — первый же
		// проход после закрытия гасит их живые записи; журнал сделок в гашении
		// не участвует, достаточно набора открытых конструкций снимка.
		// Traceability: openspec:hints/hint-lifecycle#requirement-hint-expiry-on-subject-close-and-retirement
		expiredHints += await ExpireClosedSubjectsAsync(snapshot, cancellationToken).ConfigureAwait(false);

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

		// Фаза 3: рыночные марки. Недоступность источника пропускает проход
		// с диагностикой — подсказки на неполных данных не выпускаются.
		// Гашения фаз 1–2 не зависят от рынка и остаются в силе.
		// Traceability: openspec:hints/engine-pass#requirement-engine-market-unavailable-skips-pass
		var marks = await _markSource.GetMarksAsync(symbols, cancellationToken).ConfigureAwait(false);
		if (marks.IsAvailable == false)
		{
			return new HintPassResult
			{
				Outcome = HintPassOutcome.SkippedMarketUnavailable,
				AsOf = asOf,
				CreatedHints = 0,
				ExpiredHints = expiredHints,
				Diagnostics = [marks.FailureReason!],
			};
		}

		// Фаза 4: оценка триггеров активных правил с машинной реализацией.
		// Журнальные триггеры оцениваются один раз по всему снимку (субъект
		// «журнал»), триггеры конструкций — по каждой открытой конструкции;
		// вид субъекта объявляет триггер, а не скоуп карточки.
		// Traceability: openspec:hints/engine-pass#requirement-engine-deterministic-selection
		// Traceability: openspec:hints/hint-lifecycle#requirement-hint-subject-v1-closed-set
		var openConstructions = snapshot.Constructions
			.Where(construction => construction.IsOpen)
			.ToArray();

		// Живые записи читаются один раз на цикл оценки: решения фазы опираются
		// на согласованный срез, записи этого прохода в гашение не попадают.
		var liveRecords = await _hintStore.ListLiveAsync(cancellationToken).ConfigureAwait(false);
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
				var (created, expired) = await EvaluateRuleOnSubjectAsync(
					trigger,
					input,
					HintSubject.ForJournal(),
					liveRecords,
					asOf,
					cancellationToken).ConfigureAwait(false);
				createdHints += created;
				expiredHints += expired;
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
					var (created, expired) = await EvaluateRuleOnSubjectAsync(
						trigger,
						input,
						HintSubject.ForConstruction(construction.Id),
						liveRecords,
						asOf,
						cancellationToken).ConfigureAwait(false);
					createdHints += created;
					expiredHints += expired;
				}
			}
		}

		// Фаза 5: записи сохранены, дедуп-окно и гашения применены по фазам прохода.
		return new HintPassResult
		{
			Outcome = HintPassOutcome.Completed,
			AsOf = asOf,
			CreatedHints = createdHints,
			ExpiredHints = expiredHints,
			UnimplementedRuleIds = unimplementedRuleIds,
		};
	}

	/// <summary>
	/// Оценивает правило на одном субъекте и применяет дедуп-окно «правило ×
	/// субъект (+ период)»: сработавший триггер создаёт подсказку, только если
	/// у ключа нет записи текущего окна — подавляет любая живая запись,
	/// включая applied и dismissed (отклонённое не повторяется). Для периодных
	/// правил в ключ окна входит период из фактов: смена периода открывает
	/// новое окно. Несработавший триггер гасит живые new-записи ключа — окно
	/// закрыто ушедшим условием, и вернувшееся условие породит новую
	/// подсказку; applied и dismissed терминальны и агентом не трогаются.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-dedup-rule-subject-window
	/// </summary>
	private async Task<(int Created, int Expired)> EvaluateRuleOnSubjectAsync(
		IHintTrigger trigger,
		TriggerEvaluationInput input,
		HintSubject subject,
		IReadOnlyList<HintRecord> liveRecords,
		DateTimeOffset asOf,
		CancellationToken cancellationToken)
	{
		var outcome = trigger.Evaluate(input);
		var card = input.Card;

		if (outcome.Fired)
		{
			var windowEntry = await _hintStore.FindWindowEntryAsync(
				card.Id,
				subject,
				outcome.WindowPeriodKey,
				cancellationToken).ConfigureAwait(false);
			if (windowEntry is not null)
			{
				return (0, 0);
			}

			await _hintStore.AddAsync(RenderRecord(card, subject, outcome, asOf), cancellationToken).ConfigureAwait(false);
			return (1, 0);
		}

		var expired = 0;
		foreach (var record in liveRecords)
		{
			if (record.RuleId != card.Id
				|| record.Subject != subject
				|| record.Status != HintStatus.New)
			{
				continue;
			}

			if (await _hintStore.TryTransitionAsync(record.Id, HintStatus.Expired, cancellationToken).ConfigureAwait(false))
			{
				expired++;
			}
		}

		return (0, expired);
	}

	/// <summary>
	/// Гасит живые new-записи правил, выведенных из корпуса: retired-карточек
	/// и карточек, удалённых из каталога. Выполняется при загрузке снимка
	/// корпуса; журнал сделок и рыночные данные не участвуют.
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-expiry-on-subject-close-and-retirement
	/// </summary>
	private async Task<int> ExpireWithdrawnRulesAsync(RulesCorpusSnapshot corpus, CancellationToken cancellationToken)
	{
		var activeRuleIds = corpus.ExecutableCards
			.Concat(corpus.UnimplementedCards)
			.Select(card => card.Id)
			.ToHashSet(StringComparer.Ordinal);

		return await ExpireRecordsAsync(
			record => record.Status == HintStatus.New && activeRuleIds.Contains(record.RuleId) == false,
			cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Гасит живые new-записи закрытых субъектов-конструкций: субъект больше
	/// не входит в открытые конструкции снимка журнала (закрыт или удалён).
	// Traceability: openspec:hints/hint-lifecycle#requirement-hint-expiry-on-subject-close-and-retirement
	/// </summary>
	private async Task<int> ExpireClosedSubjectsAsync(JournalSnapshot snapshot, CancellationToken cancellationToken)
	{
		var openConstructionIds = snapshot.Constructions
			.Where(construction => construction.IsOpen)
			.Select(construction => construction.Id)
			.ToHashSet();

		return await ExpireRecordsAsync(
			record => record.Status == HintStatus.New
				&& record.Subject.Kind == HintSubjectKind.Construction
				&& openConstructionIds.Contains(record.Subject.ConstructionId!.Value) == false,
			cancellationToken).ConfigureAwait(false);
	}

	/// <summary>Гасит в expired отобранные предикатом живые записи; возвращает счётчик.</summary>
	private async Task<int> ExpireRecordsAsync(Func<HintRecord, bool> shouldExpire, CancellationToken cancellationToken)
	{
		var liveRecords = await _hintStore.ListLiveAsync(cancellationToken).ConfigureAwait(false);
		var expired = 0;
		foreach (var record in liveRecords)
		{
			if (shouldExpire(record) == false)
			{
				continue;
			}

			// Переход в expired выполняет только агент; applied и dismissed
			// терминальны — дальнейшие переходы для них запрещены.
			// Traceability: openspec:hints/hint-lifecycle#requirement-hint-lifecycle-transitions
			if (await _hintStore.TryTransitionAsync(record.Id, HintStatus.Expired, cancellationToken).ConfigureAwait(false))
			{
				expired++;
			}
		}

		return expired;
	}

	/// <summary>
	/// Рендерит запись подсказки сработавшего триггера. Запись денормализована:
	/// характер, чёткость, источники и текст фиксируются на момент прохода,
	/// позднейшие правки карточки историю не искажают.
	// Traceability: openspec:hints/engine-pass#requirement-engine-self-describing-record
	// Traceability: openspec:hints/engine-pass#requirement-engine-clarity-shapes-wording
	/// </summary>
	private HintRecord RenderRecord(RuleCard card, HintSubject subject, TriggerOutcome outcome, DateTimeOffset asOf) => new()
	{
		RuleId = card.Id,
		Subject = subject,
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
