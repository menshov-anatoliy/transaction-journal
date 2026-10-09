namespace TransactionJournal.Chats.Ports;

using TransactionJournal.Domain.Materialization;

/// <summary>
/// Порт рыночных данных для инструментов чата: снимок фьючерсного
/// рынка по базовому активу и доска опционов. Каждое чтение — ровно один
/// биржевой запрос через единый клиент тикеров с его троттлером и
/// resilience; отдельные счётчики запросов не вводятся, глубину вызовов
/// ограничивает потолок итераций агентного цикла. Недоступность биржи —
/// управляемый исход в записи результата, а не исключение: инструмент
/// чата обязан ответить структурированным «недоступно».
// Traceability: openspec:chats/sources#requirement-sources-single-request-per-call
/// </summary>
public interface IChatMarketReader
{
	/// <summary>Читает снимок фьючерсного рынка по базовому активу: марка, бид-аск, открытый интерес, ставка фандинга.</summary>
	/// <param name="baseCoin">Базовый актив, например BTC или ETH.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<ChatMarketSnapshot> ReadSnapshotAsync(string baseCoin, CancellationToken cancellationToken = default);

	/// <summary>Читает доску опционов по базовому активу: страйки с IV, греками, открытым интересом и бид-аском.</summary>
	/// <param name="baseCoin">Базовый актив, например BTC или ETH.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	Task<ChatOptionBoard> ReadOptionBoardAsync(string baseCoin, CancellationToken cancellationToken = default);
}

/// <summary>
/// Снимок фьючерсного рынка по базовому активу — результат одного запроса
/// тикеров категории linear. Недоступность биржи помечается флагом с
/// причиной: ассистент обязан помечать устаревшие данные и не давать
/// рыночно-зависимых рекомендаций.
// Traceability: openspec:chats/sources#requirement-sources-degradation-cached-asof
/// </summary>
public sealed record ChatMarketSnapshot
{
	/// <summary>Базовый актив снимка.</summary>
	public required string BaseCoin { get; init; }

	/// <summary>Отметка as-of данных снимка: момент ответа биржи, а при деградации — момент получения кэшированной марки.</summary>
	public required DateTimeOffset AsOf { get; init; }

	/// <summary>Источник доступен и снимку можно доверять как рыночным данным.</summary>
	public required bool IsAvailable { get; init; }

	/// <summary>Причина недоступности источника; null — источник доступен.</summary>
	public string? UnavailableReason { get; init; }

	/// <summary>Символ запрошенного фьючерса; null — тикер не получен.</summary>
	public string? Symbol { get; init; }

	/// <summary>Марка фьючерса; null — биржа значения не отдала.</summary>
	public decimal? MarkPrice { get; init; }

	/// <summary>Лучшая цена покупки; null — биржа значения не отдала.</summary>
	public decimal? Bid1Price { get; init; }

	/// <summary>Объём лучшей цены покупки; null — биржа значения не отдала.</summary>
	public decimal? Bid1Size { get; init; }

	/// <summary>Лучшая цена продажи; null — биржа значения не отдала.</summary>
	public decimal? Ask1Price { get; init; }

	/// <summary>Объём лучшей цены продажи; null — биржа значения не отдала.</summary>
	public decimal? Ask1Size { get; init; }

	/// <summary>Открытый интерес в базовой валюте; null — биржа значения не отдала.</summary>
	public decimal? OpenInterest { get; init; }

	/// <summary>Текущая ставка фандинга бессрочного фьючерса; null — биржа значения не отдала.</summary>
	public decimal? FundingRate { get; init; }
}

/// <summary>
/// Доска опционов по базовому активу — результат одного запроса тикеров
/// категории option с фильтром baseCoin. Котировки приведены к каноническим
/// частям символа; компактную проекцию доски строит инструмент чата.
/// Недоступность биржи помечается флагом с причиной.
// Traceability: openspec:chats/sources#requirement-sources-degradation-cached-asof
/// </summary>
public sealed record ChatOptionBoard
{
	/// <summary>Базовый актив доски.</summary>
	public required string BaseCoin { get; init; }

	/// <summary>Отметка as-of данных доски: момент ответа биржи, а при деградации — момент получения самой старой кэшированной марки.</summary>
	public required DateTimeOffset AsOf { get; init; }

	/// <summary>Источник доступен и доске можно доверять как рыночным данным.</summary>
	public required bool IsAvailable { get; init; }

	/// <summary>Причина недоступности источника; null — источник доступен.</summary>
	public string? UnavailableReason { get; init; }

	/// <summary>Марка базового актива — якорь окрестности ATM; null — биржа значения не отдала.</summary>
	public decimal? UnderlyingPrice { get; init; }

	/// <summary>Число инструментов в ответе биржи до отбора в проекцию.</summary>
	public required int TotalTickerCount { get; init; }

	/// <summary>Котировки доски с разобранными частями символа; нечитаемые символы отброшены.</summary>
	public required IReadOnlyList<ChatOptionQuote> Quotes { get; init; }
}

/// <summary>Котировка опциона доски: канонические части символа плюс рыночные поля тикера.</summary>
public sealed record ChatOptionQuote
{
	/// <summary>Символ опциона биржи.</summary>
	public required string Symbol { get; init; }

	/// <summary>Дата экспирации из символа.</summary>
	public required DateOnly Expiry { get; init; }

	/// <summary>Страйк из символа.</summary>
	public required decimal Strike { get; init; }

	/// <summary>Тип опциона из символа: колл или пут.</summary>
	public required OptionType Type { get; init; }

	/// <summary>Марка опциона; null — биржа значения не отдала.</summary>
	public decimal? MarkPrice { get; init; }

	/// <summary>Подразумеваемая волатильность марки; null — биржа значения не отдала.</summary>
	public decimal? MarkIv { get; init; }

	/// <summary>Дельта опциона; null — биржа значения не отдала.</summary>
	public decimal? Delta { get; init; }

	/// <summary>Гамма опциона; null — биржа значения не отдала.</summary>
	public decimal? Gamma { get; init; }

	/// <summary>Вега опциона; null — биржа значения не отдала.</summary>
	public decimal? Vega { get; init; }

	/// <summary>Тета опциона; null — биржа значения не отдала.</summary>
	public decimal? Theta { get; init; }

	/// <summary>Открытый интерес в контрактах; null — биржа значения не отдала.</summary>
	public decimal? OpenInterest { get; init; }

	/// <summary>Лучшая цена покупки; null — биржа значения не отдала.</summary>
	public decimal? Bid1Price { get; init; }

	/// <summary>Объём лучшей цены покупки; null — биржа значения не отдала.</summary>
	public decimal? Bid1Size { get; init; }

	/// <summary>Лучшая цена продажи; null — биржа значения не отдала.</summary>
	public decimal? Ask1Price { get; init; }

	/// <summary>Объём лучшей цены продажи; null — биржа значения не отдала.</summary>
	public decimal? Ask1Size { get; init; }
}
