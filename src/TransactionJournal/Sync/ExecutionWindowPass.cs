using TransactionJournal.Bybit;
using TransactionJournal.Domain.Sync;

namespace TransactionJournal.Sync;

/// <summary>
/// Проход одного 7-дневного окна истории исполнения: листает страницы окном курсорной
/// пагинации nextPageCursor до исчерпания и умеет останавливаться раньше, когда очередная
/// страница целиком состоит из известных журналу execId. Биржа отдаёт записи по убыванию
/// времени, поэтому за целиком известной страницей лежат только более старые записи —
/// их можно не запрашивать. Ранняя остановка — правило инкрементальной догрузки;
/// backfill отключает её опцией, чтобы возобновление после обрыва не теряло хвост окна.
/// Фандинг-записи отсекаются политикой загрузки при сборе страницы и не возвращаются
/// в NewExecutions; прочие типы, включая неизвестные, загружаются как раньше.
/// Traceability: change:add-bybit-sync/design#d4
/// Traceability: openspec:sync/bybit-history#requirement-backfill-full-history
/// Traceability: openspec:sync/bybit-history#scenario-window-overlap-no-duplicates
/// Traceability: openspec:sync/bybit-history#requirement-non-trade-executions-are-not-trades
/// Traceability: change:drop-funding-executions/design#d1
/// </summary>
public sealed class ExecutionWindowPass
{
	/// <summary>Максимальная длительность окна истории исполнения: 7 дней в мс.</summary>
	public const long MaxWindowMs = 604_800_000L;

	/// <summary>Биржевой тип исполнения фандинга, отсекаемый политикой загрузки окна.</summary>
	private const string FundingExecType = "Funding";

	private readonly IBybitHistoryGateway _gateway;
	private readonly IExecutionKnownIdProbe _knownIdProbe;

	/// <summary>Создаёт проход с шлюзом биржи и проверкой известных журналу execId.</summary>
	/// <exception cref="ArgumentNullException">Шлюз или проверка не заданы.</exception>
	public ExecutionWindowPass(IBybitHistoryGateway gateway, IExecutionKnownIdProbe knownIdProbe)
	{
		_gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
		_knownIdProbe = knownIdProbe ?? throw new ArgumentNullException(nameof(knownIdProbe));
	}

	/// <summary>
	/// Выполняет проход окна: запрашивает страницы курсорной пагинацией с границами окна,
	/// фильтрует известные записи через проверку хранилища и возвращает только новые.
	/// </summary>
	/// <param name="window">Окно прохода: категория и границы времени длительностью не больше семи дней.</param>
	/// <param name="options">Параметры прохода: размер страницы и признак ранней остановки.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="ArgumentNullException">Окно не задано.</exception>
	/// <exception cref="ArgumentException">Категория окна не задана.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Окно пусто, перевёрнуто или длиннее семи дней; размер страницы вне [1..100].</exception>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой после всех повторов.</exception>
	public async Task<ExecutionWindowPassResult> RunAsync(
		ExecutionWindow window,
		ExecutionWindowPassOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(window);
		options ??= new ExecutionWindowPassOptions();
		ValidateWindow(window);
		if (options.PageSize is < 1 or > 100)
		{
			throw new ArgumentOutOfRangeException(
				nameof(options), options.PageSize, "Размер страницы прохода должен быть в диапазоне [1..100].");
		}

		var newExecutions = new List<BybitExecution>();
		var allExecutions = new List<BybitExecution>();
		var pagesFetched = 0;
		var earlyStopped = false;
		string? cursor = null;

		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// Каждая страница запрашивается границами окна и курсором предыдущего ответа;
			// первый запрос уходит без курсора. Фильтр базового актива окна переносится в
			// запрос: без него опционная доска отдаёт записи только одного актива по умолчанию.
			// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
			var query = new BybitExecutionListQuery
			{
				Category = window.Category,
				BaseCoin = window.BaseCoin,
				StartTimeMs = window.StartMs,
				EndTimeMs = window.EndMs,
				Limit = options.PageSize,
				Cursor = cursor,
			};
			var page = await _gateway.GetExecutionListAsync(query, cancellationToken).ConfigureAwait(false);
			pagesFetched++;

			if (page.List.Count > 0)
			{
				// Все записи окна запоминаются целиком: движок синхронизации отличает пустое
				// окно от окна только с известными записями и ищет самую раннюю запись для границы backfill.
				allExecutions.AddRange(page.List);

				// Фандинг отсекается политикой загрузки при сборе страницы, до проверки
				// известных execId: он не попадает в запрос известности, в NewExecutions
				// и далее в писателя сырья. Прочие типы, включая неизвестные журналу,
				// загружаются в сырье как раньше — сравнение типа без учёта регистра.
				// Traceability: openspec:sync/bybit-history#requirement-non-trade-executions-are-not-trades
				// Traceability: change:drop-funding-executions/design#d2
				var loadableOnPage = page.List
					.Where(execution => IsFundingExecType(execution.ExecType) == false)
					.ToList();

				// Известность записей спрашиваем у хранилища пачкой — по одной странице за раз.
				if (loadableOnPage.Count > 0)
				{
					var pageExecIds = loadableOnPage.Select(execution => execution.ExecId).ToArray();
					var knownExecIds = await _knownIdProbe.FindKnownAsync(pageExecIds, cancellationToken).ConfigureAwait(false);
					var freshOnPage = loadableOnPage
						.Where(execution => knownExecIds.Contains(execution.ExecId) == false)
						.ToList();
					newExecutions.AddRange(freshOnPage);

					// Целиком известная страница при сортировке по убыванию означает, что глубже
					// новых записей нет, — проход останавливается, не запрашивая следующие страницы.
					// Страница целиком из фандинга известной не считается: её записи отфильтрованы
					// политикой загрузки, а не журналом, — за ней могут лежать новые сделки.
					if (options.EarlyStopOnKnownPage && freshOnPage.Count == 0)
					{
						earlyStopped = true;
						break;
					}
				}
			}

			// Пустой nextPageCursor — биржа исчерпала страницы окна.
			if (page.HasNextPage == false)
			{
				break;
			}

			cursor = page.NextPageCursor;
		}

		return new ExecutionWindowPassResult
		{
			AllExecutions = allExecutions,
			NewExecutions = newExecutions,
			PagesFetched = pagesFetched,
			EarlyStopped = earlyStopped,
		};
	}

	#region Вспомогательные методы

	/// <summary>Сравнивает тип исполнения с фандингом без учёта регистра.</summary>
	private static bool IsFundingExecType(string execType) =>
		string.Equals(execType, FundingExecType, StringComparison.OrdinalIgnoreCase);

	private static void ValidateWindow(ExecutionWindow window)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(window.Category);
		if (window.EndMs <= window.StartMs)
		{
			throw new ArgumentOutOfRangeException(nameof(window), "Конец окна должен быть позже начала.");
		}

		if (window.EndMs - window.StartMs > MaxWindowMs)
		{
			throw new ArgumentOutOfRangeException(nameof(window), "Окно истории исполнения длиннее семи дней.");
		}
	}

	#endregion
}
