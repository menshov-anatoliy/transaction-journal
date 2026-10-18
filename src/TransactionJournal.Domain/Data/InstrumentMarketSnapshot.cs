namespace TransactionJournal.Domain.Data;

/// <summary>
/// Снимок рыночных данных инструмента из свежего ответа публичных тикеров:
/// append-only строка истории, которую провайдер марок дописывает при каждой
/// успешной выборке. История переживает перезапуски и не очищается — её
/// невозможно восстановить задним числом, поэтому она копится с первого дня
/// и служит материалом прироста распада: сравнением текущей теты со снимком
/// суточной давности. Хранение снимков не считается хранением аналитики:
/// метрики вычисляются при чтении. Индексная цена и тета nullable: биржа
/// отдаёт их только опционам, а строка истории фиксирует сам факт выборки
/// любого инструмента провайдера.
/// Traceability: openspec:analytics/option-lifetime#requirement-market-snapshot-history
/// Traceability: adr:docs/adr/0017-option-lifetime-metrics.md#considered-options
/// </summary>
public sealed class InstrumentMarketSnapshot
{
	/// <summary>Суррогатный ключ строки истории.</summary>
	public long Id { get; set; }

	/// <summary>Символ инструмента, например BTC-25JUN27-106000-P-USDT или BTCUSDT.</summary>
	public required string Symbol { get; set; }

	/// <summary>Марка инструмента на момент снимка.</summary>
	public decimal MarkPrice { get; set; }

	/// <summary>Марка базового актива опциона; у не-опционов биржей не отдаётся.</summary>
	public decimal? UnderlyingPrice { get; set; }

	/// <summary>Суточная тета опциона на контракт в котируемой монете; у не-опционов не отдаётся.</summary>
	public decimal? Theta { get; set; }

	/// <summary>Момент получения снимка: штампуется в момент прихода свежего ответа биржи.</summary>
	public required DateTimeOffset ReceivedAt { get; set; }
}
