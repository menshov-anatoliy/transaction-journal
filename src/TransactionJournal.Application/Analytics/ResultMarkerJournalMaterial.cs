namespace TransactionJournal.Application.Analytics;

using System.Globalization;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain;
using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Domain.Materialization;

/// <summary>
/// Материал хронологии маркеров над сделками и закрывающими записями
/// конструкции: единый ключ сделки в потоке позиции, торговые категории
/// записей и окна опционных исполнений по правилам сборки v5 (группировка
/// по базовому активу, зазор не более 60 минут, разрыв на закрывающих
/// записях экспираций — атомарность <see cref="AssemblyWindow"/>).
/// Помощник общий для вычислителя полных историй и читателя хвостов списка:
/// правила окон и ключей объявлены один раз, оба потребителя видят одни и те
/// же окна — расхождение времени последнего маркера исключено.
/// </summary>
/// <remarks>
/// Переиспользуется из Infrastructure читателем хвостов: сборка Application
/// открывает internal-типы для Infrastructure и тестов.
// Traceability: change:add-finresult-result-history-markers/specs/analytics/performance/spec#requirement-result-markers-history-events
// Traceability: change:add-finresult-result-history-markers/design#d2
/// </remarks>
internal static class ResultMarkerJournalMaterial
{
	/// <summary>Торговая категория опционных записей — материал окон сборки.</summary>
	public const string OptionCategory = "option";

	/// <summary>Торговая категория фьючерсных записей — по событию на сделку.</summary>
	public const string LinearCategory = "linear";

	/// <summary>Ключ сделки в потоке позиции: execId биржевой записи либо ключ строки сырья ручной сделки.</summary>
	public static string TradeKey(MaterializedTrade trade) => trade.Source == TradeSource.Manual
		? $"manual:{trade.ManualTradeId?.ToString(CultureInfo.InvariantCulture)}"
		: trade.ExecId!;

	/// <summary>
	/// Строит окна опционных сделок конструкции по правилам сборки v5:
	/// сделки группируются по базовому активу, зазор между соседними
	/// исполнениями не более 60 минут, закрывающая запись экспирации разрывает
	/// окно; ролл, усреднение и открытие целиком живут в одном окне.
	/// </summary>
	/// <param name="trades">Привязанные сделки конструкции обоих источников.</param>
	/// <param name="closingEntries">Действующие закрывающие записи конструкции.</param>
	/// <returns>Окна всех базовых активов конструкции; порядок между активами не значим для потребителя.</returns>
	public static IReadOnlyList<AssemblyWindow> BuildOptionWindows(
		IReadOnlyList<MaterializedTrade> trades,
		IReadOnlyList<PositionClosingEntry> closingEntries)
	{
		var windows = new List<AssemblyWindow>();
		var deliveriesByAsset = ExpiryDeliveriesByAsset(closingEntries);
		foreach (var (baseCoin, executions) in OptionExecutionsByAsset(trades))
		{
			foreach (var window in ConstructionAssembler.BuildWindows(
				executions,
				deliveriesByAsset.GetValueOrDefault(baseCoin, [])))
			{
				windows.Add(window);
			}
		}

		return windows;
	}

	/// <summary>
	/// Группирует опционные сделки конструкции по базовому активу в записи
	/// сборки: ключ исполнения — тот же, что в потоках позиций, поэтому срез
	/// окна совпадает с границей записей потока.
	/// </summary>
	// Опционная сделка с неразбираемым символом не образует событий маркеров
	// и в группировку окон не попадает; запись при этом остаётся в потоке
	// позиции и входит в FIFO-результат срезов — для истории такой случай
	// эквивалентен отсутствию события журнала.
	private static Dictionary<string, List<AssemblyExecution>> OptionExecutionsByAsset(IReadOnlyList<MaterializedTrade> trades)
	{
		var byAsset = new Dictionary<string, List<AssemblyExecution>>(StringComparer.Ordinal);
		foreach (var trade in trades)
		{
			if (string.Equals(trade.Category, OptionCategory, StringComparison.Ordinal) == false
				|| OptionSymbolParser.TryParse(trade.Symbol, out var parts) == false
				|| parts is null)
			{
				continue;
			}

			if (byAsset.TryGetValue(parts.BaseCoin, out var executions) == false)
			{
				executions = new List<AssemblyExecution>();
				byAsset.Add(parts.BaseCoin, executions);
			}

			executions.Add(new AssemblyExecution
			{
				ExecId = TradeKey(trade),
				Category = trade.Category,
				Symbol = trade.Symbol,
				ExecTimeMs = trade.ExecutedAt.ToUnixTimeMilliseconds(),
				SignedQuantity = trade.Quantity,
			});
		}

		return byAsset;
	}

	/// <summary>
	/// Группирует закрывающие записи экспираций по базовому активу: delivery
	/// и OTM-записи разрывают окна сборки тем же событием, что в сборке v5;
	/// ручные пометки окна не разрывают.
	/// </summary>
	private static Dictionary<string, List<AssemblyDelivery>> ExpiryDeliveriesByAsset(IReadOnlyList<PositionClosingEntry> closingEntries)
	{
		var byAsset = new Dictionary<string, List<AssemblyDelivery>>(StringComparer.Ordinal);
		foreach (var closing in closingEntries)
		{
			if (closing.Kind == PositionClosingKind.ManualAnnotation
				|| OptionSymbolParser.TryParse(closing.Symbol, out var parts) == false
				|| parts is null)
			{
				continue;
			}

			if (byAsset.TryGetValue(parts.BaseCoin, out var deliveries) == false)
			{
				deliveries = new List<AssemblyDelivery>();
				byAsset.Add(parts.BaseCoin, deliveries);
			}

			deliveries.Add(new AssemblyDelivery
			{
				Symbol = closing.Symbol,
				DeliveryTimeMs = closing.ClosedAt.ToUnixTimeMilliseconds(),
			});
		}

		return byAsset;
	}
}
