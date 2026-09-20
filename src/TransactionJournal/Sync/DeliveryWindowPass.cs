using TransactionJournal.Bybit;

namespace TransactionJournal.Sync;

/// <summary>
/// Проход одного 30-дневного окна delivery-истории: листает страницы окном курсорной
/// пагинации nextPageCursor до исчерпания и отбрасывает уже известные журналу записи
/// по ключу symbol + deliveryTime — пересекающиеся окна и повторные прогоны не создают
/// дублей. Ранней остановки на целиком известной странице нет: дизайн задаёт её только
/// для истории исполнения, а глубину инкрементального прохода ограничивает водяной знак.
/// Traceability: change:add-bybit-sync/design#d4
/// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
/// Traceability: openspec:sync/bybit-history#requirement-expiry-delivery-closing-entries
/// </summary>
public sealed class DeliveryWindowPass
{
	/// <summary>Максимальная длительность окна delivery-истории: 30 дней в мс.</summary>
	public const long MaxWindowMs = 2_592_000_000L;

	private readonly IBybitHistoryGateway _gateway;
	private readonly IDeliveryKnownKeyProbe _knownKeyProbe;

	/// <summary>Создаёт проход с шлюзом биржи и проверкой известных ключей delivery-записей.</summary>
	/// <exception cref="ArgumentNullException">Шлюз или проверка не заданы.</exception>
	public DeliveryWindowPass(IBybitHistoryGateway gateway, IDeliveryKnownKeyProbe knownKeyProbe)
	{
		_gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
		_knownKeyProbe = knownKeyProbe ?? throw new ArgumentNullException(nameof(knownKeyProbe));
	}

	/// <summary>
	/// Выполняет проход окна: запрашивает страницы курсорной пагинацией с границами окна
	/// и возвращает только новые записи, отфильтровав известные по symbol + deliveryTime.
	/// </summary>
	/// <param name="window">Окно прохода: категория и границы времени длительностью не больше тридцати дней.</param>
	/// <param name="options">Параметры прохода: размер страницы.</param>
	/// <param name="cancellationToken">Токен отмены синхронизации.</param>
	/// <exception cref="ArgumentNullException">Окно не задано.</exception>
	/// <exception cref="ArgumentException">Категория окна не задана.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Окно пусто, перевёрнуто или длиннее тридцати дней; размер страницы вне [1..50].</exception>
	/// <exception cref="BybitApiException">Биржа ответила ошибкой после всех повторов.</exception>
	public async Task<DeliveryWindowPassResult> RunAsync(
		DeliveryWindow window,
		DeliveryWindowPassOptions? options = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(window);
		options ??= new DeliveryWindowPassOptions();
		ValidateWindow(window);
		if (options.PageSize is < 1 or > 50)
		{
			throw new ArgumentOutOfRangeException(
				nameof(options), options.PageSize, "Размер страницы прохода должен быть в диапазоне [1..50].");
		}

		var newDeliveries = new List<BybitDeliveryRecord>();
		var allDeliveries = new List<BybitDeliveryRecord>();
		var pagesFetched = 0;
		string? cursor = null;

		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();

			// Каждая страница запрашивается границами окна и курсором предыдущего ответа;
			// первый запрос уходит без курсора.
			var query = new BybitDeliveryRecordQuery
			{
				Category = window.Category,
				StartTimeMs = window.StartMs,
				EndTimeMs = window.EndMs,
				Limit = options.PageSize,
				Cursor = cursor,
			};
			var page = await _gateway.GetDeliveryRecordAsync(query, cancellationToken).ConfigureAwait(false);
			pagesFetched++;

			if (page.List.Count > 0)
			{
				// Все записи окна запоминаются целиком: движок синхронизации отличает пустое
				// окно (биржа исчерпала данные) от окна только с известными записями.
				allDeliveries.AddRange(page.List);

				// Известность записей спрашиваем у хранилища пачкой — по одной странице за раз.
				// Известные по symbol + deliveryTime записи отсеиваются: пересекающиеся окна
				// и повторные прогоны возвращают только действительно новые записи.
				// Traceability: openspec:sync/bybit-history#scenario-window-overlap-no-duplicates
				var pageKeys = page.List
					.Select(delivery => new DeliveryRecordKey(delivery.Symbol, delivery.DeliveryTimeMs))
					.ToArray();
				var knownKeys = await _knownKeyProbe.FindKnownAsync(pageKeys, cancellationToken).ConfigureAwait(false);
				newDeliveries.AddRange(page.List
					.Where(delivery => knownKeys.Contains(
						new DeliveryRecordKey(delivery.Symbol, delivery.DeliveryTimeMs)) == false));
			}

			// Пустой nextPageCursor — биржа исчерпала страницы окна.
			if (page.HasNextPage == false)
			{
				break;
			}

			cursor = page.NextPageCursor;
		}

		return new DeliveryWindowPassResult
		{
			AllDeliveries = allDeliveries,
			NewDeliveries = newDeliveries,
			PagesFetched = pagesFetched,
		};
	}

	#region Вспомогательные методы

	private static void ValidateWindow(DeliveryWindow window)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(window.Category);
		if (window.EndMs <= window.StartMs)
		{
			throw new ArgumentOutOfRangeException(nameof(window), "Конец окна должен быть позже начала.");
		}

		if (window.EndMs - window.StartMs > MaxWindowMs)
		{
			throw new ArgumentOutOfRangeException(nameof(window), "Окно delivery-истории длиннее тридцати дней.");
		}
	}

	#endregion
}
