using System.Text.Json;
using TransactionJournal.Bybit;
using TransactionJournal.Data;

namespace TransactionJournal.Materialization;

/// <summary>
/// Материализатор сделок «Входящих»: детерминированно выводит сделки из сырых
/// записей исполнения RawExecution без сетевых запросов — каждый атрибут берётся
/// из биржевой записи. Один execId даёт одну сделку, поэтому повторные синки
/// и повторные материализации не создают дубликатов доменных сущностей.
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
	/// Выводит сделки «Входящих» из сырых записей исполнения. Результат упорядочен
	/// по времени исполнения, затем по execId, и не зависит от порядка входных записей.
	/// </summary>
	/// <param name="rawExecutions">Сырые записи исполнения из хранилища журнала.</param>
	/// <exception cref="ArgumentNullException">Записи не заданы.</exception>
	/// <exception cref="TradeMaterializationException">Запись повреждена или конфликтует с другой записью того же execId.</exception>
	/// <exception cref="InstrumentResolveException">Символ опциона не прошёл сверку со справочником инструментов.</exception>
	public IReadOnlyList<MaterializedTrade> Materialize(IEnumerable<RawExecution> rawExecutions)
	{
		ArgumentNullException.ThrowIfNull(rawExecutions);

		var trades = new Dictionary<string, MaterializedTrade>(StringComparer.Ordinal);
		foreach (var rawExecution in rawExecutions)
		{
			var trade = ParseTrade(rawExecution);

			// Один execId — одна сделка «Входящих»: идентичная повторная запись пропускается,
			// а конфликтующая останавливает материализацию, потому что источник записей
			// с одним идентификатором обязан быть единственным.
			if (trades.TryGetValue(trade.ExecId, out var existing))
			{
				if (existing == trade)
				{
					continue;
				}

				throw new TradeMaterializationException(
					trade.ExecId,
					$"Сырые записи с execId {trade.ExecId} конфликтуют: повторная запись с тем же идентификатором отличается от уже материализованной.");
			}

			trades.Add(trade.ExecId, trade);
		}

		// Хронологический порядок «Входящих» стабилен: при равном времени исполнения
		// записи упорядочиваются по execId, поэтому результат детерминирован.
		return trades.Values
			.OrderBy(trade => trade.ExecutedAt)
			.ThenBy(trade => trade.ExecId, StringComparer.Ordinal)
			.ToList();
	}

	#region Вспомогательные методы

	/// <summary>Разбирает одну сырую запись в сделку «Входящих».</summary>
	/// <exception cref="TradeMaterializationException">Запись повреждена или неполна.</exception>
	/// <exception cref="InstrumentResolveException">Символ опциона не прошёл сверку со справочником.</exception>
	private MaterializedTrade ParseTrade(RawExecution rawExecution)
	{
		var execution = ParsePayload(rawExecution);

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
