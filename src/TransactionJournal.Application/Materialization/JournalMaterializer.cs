using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Data;

namespace TransactionJournal.Application.Materialization;

/// <summary>
/// Фасад полного переразбора журнала: собирает весь конвейер проекции — справочник
/// инструментов, сверщик символов, материализатор сделок «Входящих» и материализатор
/// закрывающих записей экспираций — заново при каждом вызове. Переразбор работает
/// только над локальными сырыми записями хранилища и не выполняет сетевых запросов
/// к бирже, поэтому изменение правила разбора применяется повторной сборкой и даёт
/// согласованную проекцию без следов прежних правил.
// Traceability: openspec:sync/bybit-history#requirement-raw-record-storage
// Traceability: openspec:sync/bybit-history#scenario-raw-records-persisted-for-reparse
// Traceability: change:add-bybit-sync/design#d2
/// </summary>
public sealed class JournalMaterializer
{
	private readonly IFeeCurrencyRule _feeCurrencyRule;

	private readonly decimal _reconciliationTolerance;

	/// <summary>Создаёт переразборщик журнала.</summary>
	/// <param name="feeCurrencyRule">Правило приведения валюты комиссии к валюте журнала; по умолчанию — паритет USDC/USDT по ADR-0001.</param>
	/// <param name="reconciliationTolerance">Допуск сверки экспираций с deliveryRpl; расхождение свыше допуска — предупреждение.</param>
	/// <exception cref="ArgumentOutOfRangeException">Допуск сверки отрицателен.</exception>
	public JournalMaterializer(
		IFeeCurrencyRule? feeCurrencyRule = null,
		decimal reconciliationTolerance = ExpiryMaterializer.DefaultReconciliationTolerance)
	{
		_feeCurrencyRule = feeCurrencyRule ?? UsdcUsdtParityFeeCurrencyRule.Instance;
		if (reconciliationTolerance < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(reconciliationTolerance), "Допуск сверки не может быть отрицательным.");
		}

		_reconciliationTolerance = reconciliationTolerance;
	}

	/// <summary>
	/// Полностью перестраивает доменные представления журнала из сырых записей:
	/// справочник собирается из сырых спецификаций инструментов, сделки «Входящих» —
	/// из сырых записей исполнения и ручных сделок (обоих источников наравне),
	/// закрывающие записи экспираций и предупреждения сверки — из сырых delivery-записей,
	/// остатков сделок и текущих привязок.
	/// </summary>
	/// <param name="rawInstruments">Сырые спецификации инструментов из хранилища журнала.</param>
	/// <param name="rawExecutions">Сырые записи исполнения из хранилища журнала.</param>
	/// <param name="rawDeliveries">Сырые delivery-записи из хранилища журнала.</param>
	/// <param name="tradeAssignments">Текущие привязки execId к конструкциям; null или отсутствие ключа — сделка во «Входящих».</param>
	/// <param name="asOf">Момент, на который выполняется переразбор: OTM-закрытие выводится только для наступившего deliveryTime.</param>
	/// <param name="manualTrades">Ручные сделки пользователя — второе сырьё журнала наравне с записями исполнения; null — ручного сырья нет.</param>
	/// <exception cref="ArgumentNullException">Какая-либо из коллекций сырых записей не задана.</exception>
	/// <exception cref="FormatException">JSON-спецификация инструмента некорректна.</exception>
	/// <exception cref="TradeMaterializationException">Сырая запись исполнения или ручная сделка повреждена, либо конфликтует с записью того же идентификатора.</exception>
	/// <exception cref="ExpiryMaterializationException">Delivery-запись повреждена, неполна или конфликтует с другой записью того же ключа.</exception>
	/// <exception cref="InstrumentResolveException">Символ опциона расходится со справочником инструментов; отсутствие символа в справочнике деградирует перечнем в результате, а не исключением.</exception>
	public JournalMaterializationResult Materialize(
		IEnumerable<RawInstrument> rawInstruments,
		IEnumerable<RawExecution> rawExecutions,
		IEnumerable<RawDelivery> rawDeliveries,
		IReadOnlyDictionary<string, string?>? tradeAssignments,
		DateTimeOffset asOf,
		IEnumerable<ManualTrade>? manualTrades = null)
	{
		ArgumentNullException.ThrowIfNull(rawInstruments);
		ArgumentNullException.ThrowIfNull(rawExecutions);
		ArgumentNullException.ThrowIfNull(rawDeliveries);

		// Каждый переразбор строит конвейер проекции заново из сырья: снимок справочника,
		// сверщик и материализаторы не переживают вызов, поэтому проекция не может
		// понести следы прежнего правила разбора — смена правила означает пересборку.
		var catalog = new InstrumentCatalog(rawInstruments);
		var resolver = new InstrumentResolver(catalog);
		var tradeMaterializer = new TradeMaterializer(resolver, _feeCurrencyRule);
		var expiryMaterializer = new ExpiryMaterializer(tradeMaterializer, resolver, _reconciliationTolerance);

		// Ручные сделки — второе сырьё журнала наравне с RawExecutions: и сделки,
		// и остатки конструкций для закрывающих записей строятся из обоих источников.
		// Traceability: openspec:domain/manual-trades#requirement-manual-trade-full-member
		// Traceability: change:manual-journal-management/design#d1
		var tradeResult = tradeMaterializer.Materialize(rawExecutions, manualTrades);
		var expiry = expiryMaterializer.Materialize(rawExecutions, rawDeliveries, tradeAssignments, asOf, manualTrades);

		return new JournalMaterializationResult
		{
			InboxTrades = tradeResult.Trades,
			ExpiryClosingEntries = expiry.ClosingEntries,
			ReconciliationWarnings = expiry.Warnings,
			// Перечни неразрешённых символов сделок и экспираций сходятся в один
			// список проекции в стабильном порядке без повторов: пользователь видит
			// все отложенные инструменты одним предупреждением, а не ошибкой.
			// Traceability: openspec:sync/bybit-history#scenario-unresolved-symbol-degrades-to-warning
			UnresolvedInstruments = tradeResult.UnresolvedSymbols
				.Concat(expiry.UnresolvedSymbols)
				.Distinct(StringComparer.Ordinal)
				.OrderBy(symbol => symbol, StringComparer.Ordinal)
				.ToList(),
		};
	}
}
