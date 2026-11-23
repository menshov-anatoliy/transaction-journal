namespace TransactionJournal.Api.Constructions;

using TransactionJournal.Application.Analytics;

/// <summary>
/// Сериализация вида события и вида операции расшифровки маркера изменения
/// результата в стабильные строки JSON-контракта API. Единая точка для всех
/// эндпоинтов раздела: карточка сериализует вид маркера и вид операции его
/// перечня расшифровки одними строками, список и превью — вид операции
/// последнего маркера.
/// </summary>
// Формат строк задан design-решением D3 change'а fix-finresult-marker-contributions
// и delta-спецификацией analytics/performance.
// Traceability: openspec:analytics/performance#requirement-result-markers-contract
public static class ResultMarkerContract
{
	/// <summary>Сериализует вид события или операции расшифровки маркера в строку контракта.</summary>
	/// <param name="kind">Вид события из вычислителя истории результата.</param>
	/// <returns>Стабильная kebab-case строка вида в JSON-контракте API.</returns>
	// Маркеры карточки и операции их расшифровки различаются одним набором
	// видов: сервер формирует перечни целиком, UI не реконструирует вклады.
	// Traceability: openspec:analytics/performance#scenario-card-contract-publishes-markers
	// Traceability: openspec:analytics/performance#scenario-marker-operations-sum-to-change
	public static string SerializeKind(ResultMarkerKind kind) => kind switch
	{
		ResultMarkerKind.Opening => "opening",
		ResultMarkerKind.Closing => "closing",
		ResultMarkerKind.Roll => "roll",
		ResultMarkerKind.SyntheticClose => "synthetic-close",
		ResultMarkerKind.SyntheticRelease => "synthetic-release",
		ResultMarkerKind.FuturesClose => "futures-close",
		ResultMarkerKind.PnLAdjustment => "pnl-adjustment",
		ResultMarkerKind.Delivery => "delivery",
		_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
	};

	/// <summary>Переводит перечень операций расшифровки маркера в контракт API: карточка, список и превью получают один и тот же маппинг.</summary>
	/// <param name="operations">Операции расшифровки вычислителя истории результата.</param>
	/// <returns>Строки перечня контракта с видом, моментом, вкладом и детализацией.</returns>
	// Единая точка маппинга исключает расхождение перечней карточки и хвостов
	// списка/превью: сумма вкладов в любом контракте равна изменению маркера.
	// Детализация проедет как есть: окна несут сделки с суммами, фьючерсный
	// цикл — записи без сумм, связки — периоды; пустая детализация уходит
	// null, чтобы «детализации нет» отличалось от пустого перечня.
	// Traceability: openspec:analytics/performance#requirement-result-markers-contract
	// Traceability: openspec:analytics/performance#scenario-marker-operations-sum-to-change
	public static IReadOnlyList<ResultMarkerOperationResponse> SerializeOperations(IReadOnlyList<ResultMarkerOperation> operations) =>
		operations.Select(operation => new ResultMarkerOperationResponse(
			SerializeKind(operation.Kind),
			operation.EventTime,
			operation.AmountUsdt,
			operation.Trades.Count == 0
				? null
				: operation.Trades.Select(trade => new ResultMarkerTradeResponse(
					trade.Symbol,
					trade.Side,
					trade.Quantity,
					trade.Price,
					trade.ContributionUsdt)).ToArray(),
			operation.PeriodSince,
			operation.PeriodUntil)).ToArray();

	/// <summary>Переводит сводку последнего маркера списка в контракт вместе с перечнем операций расшифровки.</summary>
	/// <param name="summary">Хвост истории результата строки списка.</param>
	/// <returns>Контракт последнего маркера с перечнем расшифровки.</returns>
	// Traceability: openspec:analytics/performance#scenario-list-preview-publish-last-marker
	public static ResultMarkerSummaryResponse ToSummaryResponse(ResultMarkerSummary summary) => new(
		summary.TotalUsdt,
		summary.EventAt,
		SerializeOperations(summary.Operations));

	/// <summary>Переводит последний выведенный маркер карточки в контракт превью вместе с перечнем операций расшифровки.</summary>
	/// <param name="lastMarker">Последний выведенный маркер истории карточки.</param>
	/// <returns>Контракт последнего маркера с перечнем расшифровки.</returns>
	// Traceability: openspec:analytics/performance#scenario-list-preview-publish-last-marker
	public static ResultMarkerSummaryResponse ToSummaryResponse(ResultMarker lastMarker) => new(
		lastMarker.TotalUsdt,
		lastMarker.EventTime,
		SerializeOperations(lastMarker.Operations));
}
