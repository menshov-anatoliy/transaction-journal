using System.Text.Json;
using TransactionJournal.Bybit;
using TransactionJournal.Data;

namespace TransactionJournal.Materialization;

/// <summary>
/// Материализатор закрывающих записей экспираций: детерминированно выводит их из сырых
/// delivery-записей, справочника инструментов и текущих привязок сделок без сетевых
/// запросов. Delivery ITM-опциона делится между конструкциями пропорционально остаткам,
/// OTM-экспирация без биржевой записи закрывается по нулевой цене в deliveryTime
/// инструмента, а биржевой deliveryRpl участвует только предупреждающей сверкой
/// и не подменяет собственный расчёт.
// Traceability: openspec:sync/bybit-history#requirement-expiry-delivery-closing-entries
// Traceability: adr:docs/adr/0002-option-expiry-closing-entries.md#option-expiry-closing-entries
// Traceability: change:add-bybit-sync/design#d3
/// </summary>
public sealed class ExpiryMaterializer
{
	/// <summary>Допуск сверки по умолчанию: одна миллионная USDT покрывает округления строк биржи. Публичен, чтобы фасад переразбора задавал тот же допуск по умолчанию.</summary>
	public const decimal DefaultReconciliationTolerance = 0.000001m;

	private readonly TradeMaterializer _tradeMaterializer;
	private readonly InstrumentResolver _instrumentResolver;
	private readonly decimal _reconciliationTolerance;

	/// <summary>Создаёт материализатор экспираций над материализатором сделок и сверщиком справочника.</summary>
	/// <param name="tradeMaterializer">Материализатор сделок «Входящих» — источник остатков по инструментам.</param>
	/// <param name="instrumentResolver">Сверщик символов опционов со справочником инструментов.</param>
	/// <param name="reconciliationTolerance">Допуск сверки с deliveryRpl; расхождение свыше допуска — предупреждение.</param>
	/// <exception cref="ArgumentNullException">Зависимость не задана.</exception>
	/// <exception cref="ArgumentOutOfRangeException">Допуск сверки отрицателен.</exception>
	public ExpiryMaterializer(
		TradeMaterializer tradeMaterializer,
		InstrumentResolver instrumentResolver,
		decimal reconciliationTolerance = DefaultReconciliationTolerance)
	{
		_tradeMaterializer = tradeMaterializer ?? throw new ArgumentNullException(nameof(tradeMaterializer));
		_instrumentResolver = instrumentResolver ?? throw new ArgumentNullException(nameof(instrumentResolver));
		if (reconciliationTolerance < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(reconciliationTolerance), "Допуск сверки не может быть отрицательным.");
		}

		_reconciliationTolerance = reconciliationTolerance;
	}

	/// <summary>
	/// Выводит закрывающие записи экспираций и предупреждения сверки из сырых записей
	/// журнала. Остатки конструкций считаются по сделкам до экспирации, закрывающие
	/// записи их обнуляют; состав записей зависит только от сырья и привязок, поэтому
	/// повторная материализация и перепривязка сделок дают согласованный результат.
	/// </summary>
	/// <param name="rawExecutions">Сырые записи исполнения — основа остатков конструкций.</param>
	/// <param name="rawDeliveries">Сырые delivery-записи — единственный источник данных об ITM-экспирациях.</param>
	/// <param name="tradeAssignments">Текущие привязки execId к конструкциям; null или отсутствие ключа — сделка во «Входящих».</param>
	/// <param name="asOf">Момент, на который выполняется материализация: OTM-закрытие выводится только для наступившего deliveryTime.</param>
	/// <exception cref="ArgumentNullException">Сырые записи не заданы.</exception>
	/// <exception cref="ExpiryMaterializationException">Delivery-запись повреждена, неполна или конфликтует с другой записью того же ключа.</exception>
	/// <exception cref="TradeMaterializationException">Сырая запись исполнения повреждена.</exception>
	/// <exception cref="InstrumentResolveException">Символ опциона delivery-записи не прошёл сверку со справочником инструментов.</exception>
	public ExpiryMaterializationResult Materialize(
		IEnumerable<RawExecution> rawExecutions,
		IEnumerable<RawDelivery> rawDeliveries,
		IReadOnlyDictionary<string, string?>? tradeAssignments,
		DateTimeOffset asOf)
	{
		ArgumentNullException.ThrowIfNull(rawExecutions);
		ArgumentNullException.ThrowIfNull(rawDeliveries);

		var trades = _tradeMaterializer.Materialize(rawExecutions);
		var assignments = tradeAssignments ?? new Dictionary<string, string?>();

		// Остаток конструкции по инструменту — чистая сумма знаковых количеств сделок;
		// ConstructionId null собирает остаток непривязанных сделок «Входящих». Канонические
		// атрибуты опциона берутся со сделок: справочник уже сверён с символом при их
		// материализации.
		var residualsBySymbol = new Dictionary<string, List<ConstructionResidual>>(StringComparer.Ordinal);
		var optionBySymbol = new Dictionary<string, TradeOptionAttributes>(StringComparer.Ordinal);
		var tradeFlowBySymbol = new Dictionary<string, decimal>(StringComparer.Ordinal);
		foreach (var trade in trades)
		{
			var constructionId = assignments.TryGetValue(trade.ExecId, out var assigned) ? assigned : null;

			if (residualsBySymbol.TryGetValue(trade.Symbol, out var byConstruction) == false)
			{
				byConstruction = [];
				residualsBySymbol.Add(trade.Symbol, byConstruction);
			}

			var index = byConstruction.FindIndex(entry => entry.ConstructionId == constructionId);
			if (index < 0)
			{
				byConstruction.Add(new ConstructionResidual(constructionId, trade.Quantity));
			}
			else
			{
				byConstruction[index] = new ConstructionResidual(constructionId, byConstruction[index].Residual + trade.Quantity);
			}

			tradeFlowBySymbol.TryGetValue(trade.Symbol, out var tradeFlow);
			tradeFlowBySymbol[trade.Symbol] = tradeFlow + trade.Quantity * trade.Price;

			if (trade.Option is { } option)
			{
				optionBySymbol[trade.Symbol] = option;
			}
		}

		var deliveries = ParseDeliveries(rawDeliveries);
		var closingEntries = new List<ExpiryClosingEntry>();
		var warnings = new List<ExpiryReconciliationWarning>();
		var deliveredSymbols = new HashSet<string>(StringComparer.Ordinal);

		foreach (var delivery in deliveries)
		{
			// Правила ADR-0002 определены для опционов: delivery датированных фьючерсов
			// остаётся в сырье и не порождает закрывающих записей этого слоя.
			if (string.Equals(delivery.Category, InstrumentResolver.OptionsCategory, StringComparison.Ordinal) == false)
			{
				continue;
			}

			deliveredSymbols.Add(delivery.Record.Symbol);
			AppendDeliveryClosingEntries(delivery, residualsBySymbol, tradeFlowBySymbol, closingEntries, warnings);
		}

		AppendOtmExpiryClosingEntries(residualsBySymbol, optionBySymbol, deliveredSymbols, asOf, closingEntries);

		return new ExpiryMaterializationResult
		{
			ClosingEntries = closingEntries
				.OrderBy(entry => entry.ClosedAt)
				.ThenBy(entry => entry.Symbol, StringComparer.Ordinal)
				.ThenBy(entry => entry.ConstructionId ?? string.Empty, StringComparer.Ordinal)
				.ToList(),
			Warnings = warnings
				.OrderBy(warning => warning.DeliveryTime)
				.ThenBy(warning => warning.Symbol, StringComparer.Ordinal)
				.ToList(),
		};
	}

	#region Вспомогательные методы

	/// <summary>
	/// Выводит закрывающие записи delivery ITM-опциона: аккаунтовая delivery-запись
	/// делится между конструкциями пропорционально их остаткам, количество каждой записи
	/// обнуляет остаток конструкции, а эффективная цена равна внутренней стоимости
	/// на расчётной цене экспирации. Биржевой deliveryRpl сверяется с собственным
	/// расчётом результата и при расхождении даёт только предупреждение.
	/// </summary>
	// Требование: экспирации становятся закрывающими записями по правилам ADR-0002.
	// Traceability: openspec:sync/bybit-history#scenario-itm-delivery-split-across-constructions
	// Traceability: adr:docs/adr/0002-option-expiry-closing-entries.md#option-expiry-closing-entries
	private void AppendDeliveryClosingEntries(
		ParsedDelivery delivery,
		Dictionary<string, List<ConstructionResidual>> residualsBySymbol,
		Dictionary<string, decimal> tradeFlowBySymbol,
		List<ExpiryClosingEntry> closingEntries,
		List<ExpiryReconciliationWarning> warnings)
	{
		var record = delivery.Record;
		var resolved = _instrumentResolver.ResolveOption(record.Symbol);

		// Каноническое deliveryTime хранит справочник; запись биржи может нести миллисекунды
		// внутри минуты доставки, поэтому сверяются даты, а моментом закрытия остаётся
		// время самой записи.
		var recordDeliveryTime = DateTimeOffset.FromUnixTimeMilliseconds(record.DeliveryTimeMs);
		if (recordDeliveryTime.UtcDateTime.Date != resolved.DeliveryTime.UtcDateTime.Date)
		{
			throw new ExpiryMaterializationException(
				delivery.SourceKey,
				$"Дата доставки записи ({recordDeliveryTime.UtcDateTime:yyyy-MM-dd}) расходится со временем delivery справочника ({resolved.DeliveryTime.UtcDateTime:yyyy-MM-dd}) по инструменту {record.Symbol}.");
		}

		var effectivePrice = IntrinsicValue(resolved.OptionsType, record, delivery.SourceKey);
		var symbolResiduals = residualsBySymbol.GetValueOrDefault(record.Symbol) ?? [];
		var totalAbsoluteResidual = symbolResiduals.Sum(entry => Math.Abs(entry.Residual));

		foreach (var constructionResidual in symbolResiduals)
		{
			if (constructionResidual.Residual == 0)
			{
				continue;
			}

			// Доля аккаунтовой записи пропорциональна остатку конструкции: комиссия
			// доставки делится теми же долями, что и количество.
			var share = Math.Abs(constructionResidual.Residual) / totalAbsoluteResidual;
			closingEntries.Add(new ExpiryClosingEntry
			{
				ConstructionId = constructionResidual.ConstructionId,
				Symbol = record.Symbol,
				Kind = ExpiryClosingKind.Delivery,
				ClosedAt = recordDeliveryTime,
				Quantity = -constructionResidual.Residual,
				EffectivePrice = effectivePrice,
				Fee = (record.Fee ?? 0m) * share,
				SourceKey = delivery.SourceKey,
			});
		}

		if (record.DeliveryRpl is null)
		{
			return;
		}

		// Собственный расчёт — денежный поток по сделкам и закрывающим записям инструмента
		// без комиссий: deliveryRpl биржи не включает комиссии, они у биржи отдельным полем.
		// Расхождение — только предупреждение: собственный расчёт не переписывается и не блокирует синк.
		// Traceability: openspec:sync/bybit-history#scenario-delivery-reconciliation-warning
		var closingFlow = closingEntries
			.Where(entry => string.Equals(entry.SourceKey, delivery.SourceKey, StringComparison.Ordinal))
			.Sum(entry => entry.Quantity * entry.EffectivePrice);
		var ownResult = -tradeFlowBySymbol.GetValueOrDefault(record.Symbol) - closingFlow;
		var difference = record.DeliveryRpl.Value - ownResult;
		if (Math.Abs(difference) > _reconciliationTolerance)
		{
			warnings.Add(new ExpiryReconciliationWarning
			{
				Symbol = record.Symbol,
				DeliveryTime = recordDeliveryTime,
				DeliveryRpl = record.DeliveryRpl.Value,
				OwnResult = ownResult,
				Difference = difference,
				SourceKey = delivery.SourceKey,
			});
		}
	}

	/// <summary>
	/// Выводит автоматические закрывающие записи OTM-экспираций: инструменты с наступившим
	/// deliveryTime, ненулевым остатком и отсутствующей delivery-записью закрываются
	/// по нулевой цене в deliveryTime инструмента — по каждой конструкции своим остатком.
	/// </summary>
	// Требование: OTM-экспирация без биржевой записи закрывается автоматически.
	// Traceability: openspec:sync/bybit-history#scenario-otm-expiry-auto-close
	// Traceability: change:add-bybit-sync/design#d4
	private static void AppendOtmExpiryClosingEntries(
		Dictionary<string, List<ConstructionResidual>> residualsBySymbol,
		Dictionary<string, TradeOptionAttributes> optionBySymbol,
		HashSet<string> deliveredSymbols,
		DateTimeOffset asOf,
		List<ExpiryClosingEntry> closingEntries)
	{
		foreach (var (symbol, byConstruction) in residualsBySymbol)
		{
			// Биржевая delivery-запись закрывает инструмент сама; нулевое закрытие
			// выводится только при её отсутствии.
			if (deliveredSymbols.Contains(symbol))
			{
				continue;
			}

			if (optionBySymbol.TryGetValue(symbol, out var option) == false)
			{
				continue;
			}

			if (option.DeliveryTime > asOf)
			{
				continue;
			}

			foreach (var constructionResidual in byConstruction)
			{
				if (constructionResidual.Residual == 0)
				{
					continue;
				}

				closingEntries.Add(new ExpiryClosingEntry
				{
					ConstructionId = constructionResidual.ConstructionId,
					Symbol = symbol,
					Kind = ExpiryClosingKind.OtmExpiry,
					ClosedAt = option.DeliveryTime,
					Quantity = -constructionResidual.Residual,
					EffectivePrice = 0m,
					Fee = 0m,
					SourceKey = $"{symbol}|{option.DeliveryTime.ToUnixTimeMilliseconds()}",
				});
			}
		}
	}

	/// <summary>Разбирает и дедуплицирует сырые delivery-записи по ключу «symbol|deliveryTime».</summary>
	/// <exception cref="ExpiryMaterializationException">Запись повреждена или конфликтует с другой записью того же ключа.</exception>
	private static List<ParsedDelivery> ParseDeliveries(IEnumerable<RawDelivery> rawDeliveries)
	{
		var byKey = new Dictionary<string, ParsedDelivery>(StringComparer.Ordinal);
		foreach (var raw in rawDeliveries)
		{
			var sourceKey = $"{raw.Symbol}|{raw.DeliveryTimeMs}";
			BybitDeliveryRecord record;
			try
			{
				record = JsonSerializer.Deserialize<BybitDeliveryRecord>(raw.PayloadJson, BybitJson.Options)
					?? throw new ExpiryMaterializationException(
						sourceKey,
						$"Полезная нагрузка delivery-записи {sourceKey} оказалась пустой после разбора JSON.");
			}
			catch (JsonException exception)
			{
				throw new ExpiryMaterializationException(
					sourceKey,
					$"Полезная нагрузка delivery-записи {sourceKey} содержит некорректный JSON: {exception.Message}",
					exception);
			}

			// Ключ строки обязан совпадать с ключом полезной нагрузки: расхождение
			// означает повреждение сырья и ломает идемпотентность проекции.
			if (string.Equals(record.Symbol, raw.Symbol, StringComparison.Ordinal) == false
				|| record.DeliveryTimeMs != raw.DeliveryTimeMs)
			{
				throw new ExpiryMaterializationException(
					sourceKey,
					$"Идентификаторы полезной нагрузки delivery-записи ({record.Symbol}|{record.DeliveryTimeMs}) расходятся со строкой хранилища ({raw.Symbol}|{raw.DeliveryTimeMs}).");
			}

			if (byKey.TryGetValue(sourceKey, out var existing))
			{
				if (string.Equals(existing.PayloadJson, raw.PayloadJson, StringComparison.Ordinal))
				{
					continue;
				}

				throw new ExpiryMaterializationException(
					sourceKey,
					$"Сырые delivery-записи с ключом {sourceKey} конфликтуют: повторная запись с тем же ключом отличается от уже разобранной.");
			}

			byKey[sourceKey] = new ParsedDelivery(sourceKey, raw.Category, record, raw.PayloadJson);
		}

		return byKey.Values
			.OrderBy(delivery => delivery.Record.Symbol, StringComparer.Ordinal)
			.ThenBy(delivery => delivery.Record.DeliveryTimeMs)
			.ToList();
	}

	/// <summary>Считает внутреннюю стоимость опциона на расчётной цене экспирации по ADR-0002.</summary>
	/// <exception cref="ExpiryMaterializationException">Расчётная цена или страйк записи отсутствуют.</exception>
	// Эффективная цена delivery-закрытия — внутренняя стоимость, а не расчётная цена индекса.
	// Traceability: adr:docs/adr/0002-option-expiry-closing-entries.md#option-expiry-closing-entries
	private static decimal IntrinsicValue(OptionType optionsType, BybitDeliveryRecord record, string sourceKey)
	{
		if (record.DeliveryPrice is null)
		{
			throw new ExpiryMaterializationException(
				sourceKey,
				$"Delivery-запись {sourceKey} не содержит расчётную цену экспирации (deliveryPrice).");
		}

		if (record.Strike is null)
		{
			throw new ExpiryMaterializationException(
				sourceKey,
				$"Delivery-запись {sourceKey} не содержит страйк инструмента (strike).");
		}

		return optionsType switch
		{
			OptionType.Call => Math.Max(0m, record.DeliveryPrice.Value - record.Strike.Value),
			OptionType.Put => Math.Max(0m, record.Strike.Value - record.DeliveryPrice.Value),
			_ => throw new ArgumentOutOfRangeException(nameof(optionsType), optionsType, "Неизвестный тип опциона."),
		};
	}

	/// <summary>Остаток инструмента внутри конструкции; ConstructionId null — остаток непривязанных сделок «Входящих».</summary>
	private readonly record struct ConstructionResidual(string? ConstructionId, decimal Residual);

	/// <summary>Разобранная delivery-запись с ключом источника и исходным JSON для дедупликации.</summary>
	private readonly record struct ParsedDelivery(
		string SourceKey,
		string Category,
		BybitDeliveryRecord Record,
		string PayloadJson);

	#endregion
}
