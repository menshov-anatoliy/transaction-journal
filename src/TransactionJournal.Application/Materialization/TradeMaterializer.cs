using System.Text.Json;
using TransactionJournal.Domain.Bybit;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Materialization;

namespace TransactionJournal.Application.Materialization;

/// <summary>
/// Материализатор сделок «Входящих»: детерминированно выводит сделки из сырых
/// записей исполнения RawExecution без сетевых запросов — каждый атрибут берётся
/// из биржевой записи. Один execId даёт одну сделку, поэтому повторные синки
/// и повторные материализации не создают дубликатов доменных сущностей.
/// Сделкой становится только сырая запись исполнения с биржевым типом Trade;
/// записи прочих типов исполнения (фандинг и иные не-Trade события) хранятся
/// в сырье, но сделками не становятся.
// Traceability: openspec:sync/bybit-history#requirement-new-records-land-in-inbox
/// Traceability: openspec:sync/bybit-history#requirement-sync-idempotency
/// Traceability: change:add-bybit-sync/design#d7
/// </summary>
public sealed class TradeMaterializer
{
	/// <summary>Сторона исполнения Buy биржи: покупка увеличивает остаток.</summary>
	private const string BuySide = "Buy";

	/// <summary>Сторона исполнения Sell биржи: продажа уменьшает остаток.</summary>
	private const string SellSide = "Sell";

	/// <summary>Единственный тип исполнения биржи, из которого выводится сделка «Входящих».</summary>
	private const string TradeExecType = "Trade";

	/// <summary>Торговая категория линейного инструмента; ручная сделка по символу вне формата опциона — линейная.</summary>
	private const string LinearCategory = "linear";

	/// <summary>Валюта комиссии ручной сделки: комиссия вводится сразу в USDT, приводить её не нужно.</summary>
	private const string ManualFeeCurrency = "USDT";

	private readonly InstrumentResolver _instrumentResolver;

	/// <summary>Правило приведения валюты комиссии к валюте журнала; выделено в зависимость, чтобы смена правила разбора не требовала правок конвейера.</summary>
	private readonly IFeeCurrencyRule _feeCurrencyRule;

	/// <summary>Создаёт материализатор над сверщиком символов опционов со справочником.</summary>
	/// <param name="instrumentResolver">Сверщик, связывающий символ опциона с канонической спецификацией биржи.</param>
	/// <param name="feeCurrencyRule">Правило приведения валюты комиссии; по умолчанию — паритет USDC/USDT по ADR-0001.</param>
	/// <exception cref="ArgumentNullException">Сверщик не задан.</exception>
	public TradeMaterializer(InstrumentResolver instrumentResolver, IFeeCurrencyRule? feeCurrencyRule = null)
	{
		_instrumentResolver = instrumentResolver ?? throw new ArgumentNullException(nameof(instrumentResolver));
		_feeCurrencyRule = feeCurrencyRule ?? UsdcUsdtParityFeeCurrencyRule.Instance;
	}

	/// <summary>
	/// Выводит сделки «Входящих» из сырья журнала — сырых записей исполнения биржи
	/// и ручных сделок пользователя; оба источника материализуются в одну проекцию
	/// наравне друг с другом и различаются признаком источника. Результат упорядочен
	/// по времени исполнения, затем по источнику (биржевые раньше ручных того же
	/// момента), затем по идентификатору, и не зависит от порядка входных записей.
	/// Биржевой сделкой становится только сырая запись с биржевым типом исполнения
	/// Trade (без учёта регистра); записи прочих типов исполнения (фандинг и иные
	/// не-Trade события) хранятся в сырье, но сделками не становятся. Символ опциона,
	/// отсутствующий в справочнике, откладывает запись любого источника: сделка не
	/// материализуется, символ попадает в перечень неразрешённых результата; прочие
	/// причины сверки и повреждённые записи остаются ошибками.
	/// </summary>
	/// <param name="rawExecutions">Сырые записи исполнения из хранилища журнала.</param>
	/// <param name="manualTrades">Ручные сделки пользователя из хранилища журнала; null — ручного сырья нет.</param>
	/// <exception cref="ArgumentNullException">Записи исполнения не заданы.</exception>
	/// <exception cref="TradeMaterializationException">Запись повреждена или конфликтует с другой записью того же идентификатора.</exception>
	/// <exception cref="InstrumentResolveException">Символ опциона расходится со справочником инструментов — не отсутствует, а противоречит ему.</exception>
	public TradeMaterializationResult Materialize(IEnumerable<RawExecution> rawExecutions, IEnumerable<ManualTrade>? manualTrades = null)
	{
		ArgumentNullException.ThrowIfNull(rawExecutions);

		var trades = new List<MaterializedTrade>();
		var unresolvedSymbols = new SortedSet<string>(StringComparer.Ordinal);
		var tradesByExecId = new Dictionary<string, MaterializedTrade>(StringComparer.Ordinal);
		foreach (var rawExecution in rawExecutions)
		{
			// Разбор payload вынесен до построения сделки: тип исполнения доступен
			// только в полезной нагрузке, поэтому решение «сделка или нет» принимается
			// раньше любых торговых проверок записи. Повреждённый JSON остаётся ошибкой.
			var execution = ParsePayload(rawExecution);

			// Фандинг и прочие не-Trade события исполнения — регулярные записи linear-
			// торговли: они хранятся в сырье целиком и доступны переразбору и диагностической
			// сверке, но сделкой «Входящих» не становятся, поэтому и в результат материализации
			// не попадают ни сделкой, ни неразрешённым символом. Пропуск тихий — без ошибки
			// и предупреждения: предупреждение на каждый фандинг превратило бы синк в шум.
			// Сделкой считается только значение Trade без учёта регистра; пустое и любое
			// неизвестное будущее значение биржи — не сделка по умолчанию.
			// Traceability: openspec:sync/bybit-history#requirement-new-records-land-in-inbox
			// Traceability: openspec:sync/bybit-history#requirement-non-trade-executions-are-not-trades
			// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
			if (string.Equals(execution.ExecType, TradeExecType, StringComparison.OrdinalIgnoreCase) == false)
			{
				continue;
			}

			MaterializedTrade trade;
			try
			{
				trade = BuildTrade(rawExecution, execution);
			}
			catch (InstrumentResolveException exception)
				when (exception.Reason == InstrumentResolveFailureReason.UnknownSymbol)
			{
				// Символ без спецификации в справочнике откладывает запись, а не роняет
				// проекцию: сделка не материализуется, символ собирается в перечень
				// неразрешённых — деградация видна предупреждением, остальные записи
				// строятся, повреждение сырья и расхождения со справочником остаются
				// жёсткими ошибками.
				// Traceability: openspec:sync/bybit-history#scenario-unresolved-symbol-degrades-to-warning
				unresolvedSymbols.Add(exception.Symbol);
				continue;
			}

			// Один execId — одна сделка «Входящих»: идентичная повторная запись пропускается,
			// а конфликтующая останавливает материализацию, потому что источник записей
			// с одним идентификатором обязан быть единственным.
			if (tradesByExecId.TryGetValue(trade.ExecId!, out var existing))
			{
				if (existing == trade)
				{
					continue;
				}

				throw new TradeMaterializationException(
					trade.ExecId!,
					$"Сырые записи с execId {trade.ExecId} конфликтуют: повторная запись с тем же идентификатором отличается от уже материализованной.");
			}

			tradesByExecId.Add(trade.ExecId!, trade);
			trades.Add(trade);
		}

		// Ручные сделки материализуются в ту же проекцию наравне с биржевыми: сырьё
		// другого источника меняет только происхождение записи, а не путь расчёта.
		// Traceability: openspec:domain/manual-trades#requirement-manual-trade-entry
		// Traceability: change:manual-journal-management/design#d1
		var tradesByManualId = new Dictionary<long, MaterializedTrade>();
		foreach (var manualTrade in manualTrades ?? Enumerable.Empty<ManualTrade>())
		{
			ArgumentNullException.ThrowIfNull(manualTrade);

			MaterializedTrade trade;
			try
			{
				trade = BuildManualTrade(manualTrade);
			}
			catch (InstrumentResolveException exception)
				when (exception.Reason == InstrumentResolveFailureReason.UnknownSymbol)
			{
				// Деградация неразрешённого символа едина для обоих источников: ручная
				// сделка по символу без спецификации справочника откладывается так же,
				// как биржева, и появится в проекции после пополнения справочника.
				// Traceability: openspec:sync/bybit-history#scenario-unresolved-symbol-degrades-to-warning
				unresolvedSymbols.Add(exception.Symbol);
				continue;
			}

			// Идентичность ручной записи несёт идентификатор строки сырья: повторная
			// материализация той же строки не создаёт дубликат, конфликтующая — ошибка,
			// по той же причине, что и конфликт execId у биржевых записей.
			if (tradesByManualId.TryGetValue(manualTrade.Id, out var existingManual))
			{
				if (existingManual == trade)
				{
					continue;
				}

				throw new TradeMaterializationException(
					ManualSourceKey(manualTrade.Id),
					$"Ручные записи с идентификатором {manualTrade.Id} конфликтуют: повторная запись отличается от уже материализованной.");
			}

			tradesByManualId.Add(manualTrade.Id, trade);
			trades.Add(trade);
		}

		// Хронологический порядок «Входящих» стабилен: при равном времени исполнения
		// биржевые записи идут раньше ручных (та же конвенция приоритета, что у
		// закрывающих записей), внутри источника записи упорядочены идентификатором,
		// поэтому результат детерминирован.
		return new TradeMaterializationResult
		{
			Trades = trades
				.OrderBy(trade => trade.ExecutedAt)
				.ThenBy(trade => trade.Source)
				.ThenBy(trade => trade.ExecId, StringComparer.Ordinal)
				.ThenBy(trade => trade.ManualTradeId)
				.ToList(),
			UnresolvedSymbols = unresolvedSymbols.ToList(),
		};
	}

	#region Вспомогательные методы

	/// <summary>Строит сделку «Входящих» из сырой записи и её разобранного payload.</summary>
	/// <exception cref="TradeMaterializationException">Запись повреждена или неполна.</exception>
	/// <exception cref="InstrumentResolveException">Символ опциона не прошёл сверку со справочником.</exception>
	private MaterializedTrade BuildTrade(RawExecution rawExecution, BybitExecution execution)
	{
		// Идентичность строки хранилища и полезной нагрузки — залог идемпотентности:
		// execId служит ключом дедупликации, расхождение означает повреждение сырья.
		if (string.Equals(execution.ExecId, rawExecution.ExecId, StringComparison.Ordinal) == false)
		{
			throw new TradeMaterializationException(
				rawExecution.ExecId,
				$"Идентификатор исполнения полезной нагрузки ({execution.ExecId}) расходится со строкой хранилища ({rawExecution.ExecId}).");
		}

		// Знак количества берётся по стороне исполнения биржи: покупка увеличивает
		// остаток, продажа уменьшает.
		var sideSign = execution.Side switch
		{
			BuySide => 1,
			SellSide => -1,
			_ => throw new TradeMaterializationException(
				rawExecution.ExecId,
				$"Сторона исполнения «{execution.Side}» не распознана: ожидается Buy или Sell."),
		};

		if (execution.ExecPrice is null)
		{
			throw new TradeMaterializationException(
				rawExecution.ExecId,
				$"Запись исполнения {rawExecution.ExecId} не содержит цену исполнения (execPrice).");
		}

		if (execution.ExecQty is null)
		{
			throw new TradeMaterializationException(
				rawExecution.ExecId,
				$"Запись исполнения {rawExecution.ExecId} не содержит исполненное количество (execQty).");
		}

		return new MaterializedTrade
		{
			ExecId = execution.ExecId,
			Source = TradeSource.Exchange,
			Category = rawExecution.Category,
			Symbol = execution.Symbol,
			ExecutedAt = DateTimeOffset.FromUnixTimeMilliseconds(execution.ExecTimeMs),
			Quantity = sideSign * execution.ExecQty.Value,
			Price = execution.ExecPrice.Value,
			// Знак комиссии сохраняется знаком биржи: уплаченная положительна, rebate отрицателен.
			Fee = execution.ExecFee ?? 0m,
			// Валюта комиссии приводится правилом разбора: по умолчанию USDC учитывается
			// как USDT в паритете 1:1, смена правила применяется повторной материализацией.
			FeeCurrency = _feeCurrencyRule.Canonicalize(execution.FeeCurrency),
			IsMaker = execution.IsMaker,
			Option = ResolveOptionAttributes(rawExecution, execution),
		};
	}

	/// <summary>
	/// Строит сделку «Входящих» из ручной сделки сырья: атрибуты пользовательского
	/// ввода переносятся как есть (количество знакуется стороной, комиссия уже в
	/// USDT), а категория и атрибуты опциона выводятся сверкой символа со справочником
	/// — тем же путём, что у биржевых сделок. execId у ручной записи отсутствует,
	/// её идентичность несёт идентификатор строки сырья.
	/// </summary>
	/// <param name="manualTrade">Ручная сделка из хранилища журнала.</param>
	/// <exception cref="TradeMaterializationException">Сторона ручной сделки не распознана.</exception>
	/// <exception cref="InstrumentResolveException">Символ опциона не сверён со справочником инструментов.</exception>
	// Ручная сделка материализуется в общий журнал наравне с биржевой: сырьё —
	// отдельная таблица ManualTrades, проекция — общая, различие только в источнике.
	// Traceability: openspec:domain/manual-trades#requirement-manual-trade-entry
	// Traceability: change:manual-journal-management/design#d1
	// Traceability: change:manual-journal-management/design#d2
	private MaterializedTrade BuildManualTrade(ManualTrade manualTrade)
	{
		// Знак количества берётся по стороне биржевым значением: покупка увеличивает
		// остаток, продажа уменьшает — как у записей исполнения биржи.
		var sideSign = manualTrade.Side switch
		{
			BuySide => 1,
			SellSide => -1,
			_ => throw new TradeMaterializationException(
				ManualSourceKey(manualTrade.Id),
				$"Сторона ручной сделки {ManualSourceKey(manualTrade.Id)} «{manualTrade.Side}» не распознана: ожидается Buy или Sell."),
		};

		// Категория ручной сделки выводится из формата символа: символ в формате
		// опциона Bybit сверяется со справочником, символ вне формата — линейный
		// инструмент без атрибутов опциона.
		var option = ResolveManualOptionAttributes(manualTrade.Symbol);

		return new MaterializedTrade
		{
			ExecId = null,
			Source = TradeSource.Manual,
			ManualTradeId = manualTrade.Id,
			ConstructionId = manualTrade.ConstructionId,
			Category = option is null ? LinearCategory : InstrumentResolver.OptionsCategory,
			Symbol = manualTrade.Symbol,
			ExecutedAt = manualTrade.ExecutedAt,
			Quantity = sideSign * manualTrade.Quantity,
			Price = manualTrade.Price,
			// Комиссия ручной сделки вводится в USDT и неотрицательна: знак и валюта
			// биржевой записи здесь не нужны, приводить валюту не требуется.
			Fee = manualTrade.FeeUsdt,
			FeeCurrency = ManualFeeCurrency,
			// Мейкерство — биржевое свойство исполнения; у ручного ввода его нет.
			IsMaker = false,
			Option = option,
		};
	}

	/// <summary>
	/// Выводит канонические атрибуты опциона ручной сделки: символ в формате опциона
	/// Bybit сверяется со справочником инструментов, как у биржевых сделок; символ вне
	/// формата — линейный инструмент, атрибутов опциона у него нет.
	/// </summary>
	/// <exception cref="InstrumentResolveException">Символ не сверён со справочником инструментов.</exception>
	// Сверка символа со справочником едина для обоих источников: ручной разбор
	// строки символа источником метаданных не является.
	// Traceability: openspec:domain/manual-trades#requirement-manual-symbol-resolution
	private TradeOptionAttributes? ResolveManualOptionAttributes(string symbol)
	{
		if (OptionSymbolParser.TryParse(symbol, out _) == false)
		{
			return null;
		}

		var resolved = _instrumentResolver.ResolveOption(symbol);
		return new TradeOptionAttributes
		{
			BaseCoin = resolved.BaseCoin,
			OptionsType = resolved.OptionsType,
			Strike = resolved.Strike,
			DeliveryTime = resolved.DeliveryTime,
		};
	}

	/// <summary>Строит ключ источника ручной сделки в сообщениях и привязках: идентификатор строки сырья.</summary>
	private static string ManualSourceKey(long manualTradeId) => $"manual:{manualTradeId}";

	/// <summary>Разбирает JSON сырой записи в типизированную запись исполнения биржи.</summary>
	/// <exception cref="TradeMaterializationException">JSON некорректен или пуст.</exception>
	private static BybitExecution ParsePayload(RawExecution rawExecution)
	{
		try
		{
			return JsonSerializer.Deserialize<BybitExecution>(rawExecution.PayloadJson, BybitJson.Options)
				?? throw new TradeMaterializationException(
					rawExecution.ExecId,
					$"Полезная нагрузка записи исполнения {rawExecution.ExecId} оказалась пустой после разбора JSON.");
		}
		catch (JsonException exception)
		{
			throw new TradeMaterializationException(
				rawExecution.ExecId,
				$"Полезная нагрузка записи исполнения {rawExecution.ExecId} содержит некорректный JSON: {exception.Message}",
				exception);
		}
	}

	/// <summary>
	/// Выводит канонические атрибуты опциона для записи категории option: свойства
	/// берутся из справочника инструментов после сверки символа, а не из строки символа.
	/// Для прочих категорий атрибутов опциона нет.
	/// </summary>
	/// <exception cref="InstrumentResolveException">Символ не сверён со справочником инструментов.</exception>
	// Traceability: openspec:sync/bybit-history#requirement-instrument-reference
	private TradeOptionAttributes? ResolveOptionAttributes(RawExecution rawExecution, BybitExecution execution)
	{
		if (string.Equals(rawExecution.Category, InstrumentResolver.OptionsCategory, StringComparison.Ordinal) == false)
		{
			return null;
		}

		var resolved = _instrumentResolver.ResolveOption(execution.Symbol);
		return new TradeOptionAttributes
		{
			BaseCoin = resolved.BaseCoin,
			OptionsType = resolved.OptionsType,
			Strike = resolved.Strike,
			DeliveryTime = resolved.DeliveryTime,
		};
	}

	#endregion
}
