namespace TransactionJournal.Chats;

using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using TransactionJournal.Chats.Ports;
using TransactionJournal.Domain.Materialization;

/// <summary>
/// Реестр инструментов чата: ровно три read-only инструмента —
/// чтение карточки правила по id, снимок фьючерсного рынка и компактная
/// проекция доски опционов по базовому активу. Пишущих инструментов и
/// инструментов по чужим конструкциям нет: реестр собирается только из
/// этих трёх функций, каждое рыночное чтение выполняет ровно один биржевой
/// запрос через порт рыночных данных. Результаты инструментов — markdown:
/// компактная проекция вместо сырых данных биржи.
// Traceability: openspec:chats/sources#requirement-sources-read-only-tool-registry
// Traceability: openspec:chats/sources#requirement-sources-single-request-per-call
/// </summary>
public sealed class ChatTools
{
	/// <summary>Имя инструмента чтения карточки правила.</summary>
	public const string ReadRuleCardToolName = "read_rule_card";

	/// <summary>Имя инструмента снимка фьючерсного рынка.</summary>
	public const string GetMarketSnapshotToolName = "get_market_snapshot";

	/// <summary>Имя инструмента проекции доски опционов.</summary>
	public const string GetOptionBoardToolName = "get_option_board";

	/// <summary>Относительная полуширина окна проекции доски вокруг марки базового актива, доли.</summary>
	private const decimal BoardWindowShare = 0.20m;

	/// <summary>Максимум страйков одной экспирации в проекции доски — ближайшие к якорю окном.</summary>
	private const int MaxStrikesPerExpiry = 21;

	// Компактная сериализация аргументов вызовов: рыночный след читается адаптером,
	// а не человеком.
	private static readonly JsonSerializerOptions CompactJsonOptions = new()
	{
		WriteIndented = false,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
	};

	private readonly IRuleCorpusReader _ruleCorpusReader;

	private readonly IChatMarketReader _marketReader;

	/// <summary>Создаёт реестр поверх читателя корпуса и порта рыночных данных.</summary>
	/// <param name="ruleCorpusReader">Читатель корпуса правил: полный текст карточки только по id.</param>
	/// <param name="marketReader">Порт рыночных данных: одно чтение — один биржевой запрос.</param>
	public ChatTools(IRuleCorpusReader ruleCorpusReader, IChatMarketReader marketReader)
	{
		_ruleCorpusReader = ruleCorpusReader ?? throw new ArgumentNullException(nameof(ruleCorpusReader));
		_marketReader = marketReader ?? throw new ArgumentNullException(nameof(marketReader));
	}

	/// <summary>
	/// Собирает реестр инструментов ассистента: ровно три read-only функции
	/// с фиксированными именами; проверка реестра показывает отсутствие
	/// пишущих инструментов. Накопитель следа опционален: каждый вызов тула
	/// записывается в него в момент исполнения — вместе с as-of отданных данных.
	// Traceability: openspec:chats/sources#scenario-sources-write-never
	// Traceability: openspec:chats/history#scenario-chat-source-trace-persisted
	/// </summary>
	/// <param name="traceRecorder">Накопитель рыночного следа ответа; null — вызовы не записываются.</param>
	/// <returns>Список из трёх функций инструментов.</returns>
	public IReadOnlyList<AIFunction> CreateTools(ChatMarketTraceRecorder? traceRecorder = null) =>
	[
		AIFunctionFactory.Create(
			ReadRuleCardAsync,
			new AIFunctionFactoryOptions { Name = ReadRuleCardToolName, Description = "Возвращает полный текст карточки правила корпуса по её id из индекса.", ConfigureParameterBinding = TraceBinding(traceRecorder) }),
		AIFunctionFactory.Create(
			GetMarketSnapshotAsync,
			new AIFunctionFactoryOptions { Name = GetMarketSnapshotToolName, Description = "Возвращает снимок фьючерсного рынка по базовому активу: марка, бид-аск, открытый интерес, ставка фандинга.", ConfigureParameterBinding = TraceBinding(traceRecorder) }),
		AIFunctionFactory.Create(
			GetOptionBoardAsync,
			new AIFunctionFactoryOptions { Name = GetOptionBoardToolName, Description = "Возвращает компактную проекцию доски опционов по базовому активу: страйки с IV, греками, открытым интересом и бид-аском.", ConfigureParameterBinding = TraceBinding(traceRecorder) }),
	];

	/// <summary>
	/// Привязка накопителя следа к вызову тула: параметр — обвязка вызова, а не
	/// аргумент модели, поэтому из схемы тула он исключён, а значение подставляется
	/// замыканием в момент исполнения.
	/// </summary>
	private static Func<ParameterInfo, AIFunctionFactoryOptions.ParameterBindingOptions> TraceBinding(
		ChatMarketTraceRecorder? traceRecorder) =>
		parameter => parameter.ParameterType == typeof(ChatMarketTraceRecorder)
			? new AIFunctionFactoryOptions.ParameterBindingOptions
			{
				BindParameter = (_, _) => traceRecorder,
				ExcludeFromSchema = true,
			}
			: default;

	/// <summary>Компактная сериализация аргументов вызова тула для рыночного следа.</summary>
	private static string CompactArguments(object arguments) =>
		JsonSerializer.Serialize(arguments, CompactJsonOptions);

	/// <summary>
	/// Читает полный текст карточки правила по id из индекса корпуса; карточки
	/// с таким id нет — инструмент отвечает текстом об отсутствии, а не ошибкой.
	// Traceability: openspec:chats/sources#scenario-sources-card-by-id
	/// </summary>
	/// <param name="cardId">Идентификатор карточки из индекса корпуса, например ac-01.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Полный текст карточки или сообщение об отсутствии.</returns>
	public async Task<string> ReadRuleCardAsync(
		[Description("Идентификатор карточки из индекса корпуса, например ac-01.")] string cardId,
		ChatMarketTraceRecorder? traceRecorder = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
		var card = await _ruleCorpusReader.ReadCardAsync(cardId, cancellationToken).ConfigureAwait(false);

		// Чтение карточки попадает в след без as-of: корпус правил — не рыночные данные.
		// Traceability: openspec:chats/history#scenario-chat-source-trace-persisted
		traceRecorder?.Record(ReadRuleCardToolName, CompactArguments(new { cardId }), dataAsOf: null);

		return card is null
			? $"Карточка «{cardId}» в корпусе правил не найдена."
			: card.Text;
	}

	/// <summary>
	/// Читает снимок фьючерсного рынка по базовому активу: одно чтение порта —
	/// один биржевой запрос; недоступность биржи отдаётся структурированным
	/// «недоступно» с причиной и последней кэшированной маркой с её as-of.
	// Traceability: openspec:chats/sources#requirement-sources-single-request-per-call
	// Traceability: openspec:chats/sources#requirement-sources-degradation-cached-asof
	/// </summary>
	/// <param name="baseCoin">Базовый актив, например BTC или ETH.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Markdown-снимок рынка.</returns>
	public async Task<string> GetMarketSnapshotAsync(
		[Description("Базовый актив, например BTC или ETH.")] string baseCoin,
		ChatMarketTraceRecorder? traceRecorder = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(baseCoin);
		var snapshot = await _marketReader.ReadSnapshotAsync(baseCoin, cancellationToken).ConfigureAwait(false);

		// As-of следа: живой снимок или кэшированная проекция дают as-of данных;
		// без того и другого рыночных данных нет вовсе — след хранит null.
		// Traceability: openspec:chats/history#scenario-chat-source-trace-persisted
		traceRecorder?.Record(
			GetMarketSnapshotToolName,
			CompactArguments(new { baseCoin }),
			snapshot.IsAvailable || snapshot.MarkPrice is not null ? snapshot.AsOf : null);

		var markdown = new StringBuilder();
		if (snapshot.IsAvailable)
		{
			markdown.AppendLine($"# Снимок фьючерсного рынка {snapshot.BaseCoin} {AsOfTag(snapshot.AsOf)}");
			markdown.AppendLine();
			markdown.AppendLine(snapshot.Symbol is { } symbol
				? $"- Инструмент: {symbol}"
				: $"- Инструмент {snapshot.BaseCoin}USDT: биржа тикер не отдала");
			markdown.AppendLine(snapshot.MarkPrice is { } mark
				? $"- Марка: {Num(mark)} USDT"
				: "- Марка: нет данных");
			if (snapshot.Bid1Price is { } bid)
			{
				markdown.AppendLine($"- Бид/аск: {Num(bid)} / {NumOpt(snapshot.Ask1Price)} (объёмы {NumOpt(snapshot.Bid1Size)} / {NumOpt(snapshot.Ask1Size)})");
			}

			if (snapshot.OpenInterest is { } openInterest)
			{
				markdown.AppendLine($"- Открытый интерес: {Num(openInterest)} {snapshot.BaseCoin}");
			}

			if (snapshot.FundingRate is { } funding)
			{
				markdown.AppendLine($"- Ставка фандинга: {Num(funding)}");
			}
		}
		else
		{
			// Недоступность биржи — структурированный ответ с причиной и последней
			// кэшированной маркой с её as-of: ассистент обязан пометить устаревшие
			// данные и не давать рыночно-зависимых рекомендаций, отвечая по журналу
			// и корпусу.
			// Traceability: openspec:chats/sources#scenario-sources-market-down-cached-projection
			if (snapshot.MarkPrice is { } cachedMark)
			{
				markdown.AppendLine($"# Снимок фьючерсного рынка {snapshot.BaseCoin} — биржа недоступна");
				markdown.AppendLine();
				markdown.AppendLine($"Причина: {snapshot.UnavailableReason ?? "неизвестна"}");
				markdown.AppendLine();
				markdown.AppendLine($"Последняя кэшированная марка {AsOfTag(snapshot.AsOf)}:");
				markdown.AppendLine(snapshot.Symbol is { } symbol
					? $"- Инструмент: {symbol}"
					: $"- Инструмент {snapshot.BaseCoin}USDT");
				markdown.AppendLine($"- Марка: {Num(cachedMark)} USDT");
			}
			else
			{
				markdown.AppendLine($"# Снимок фьючерсного рынка {snapshot.BaseCoin} — биржа недоступна {AsOfTag(snapshot.AsOf)}");
				markdown.AppendLine();
				markdown.AppendLine($"Причина: {snapshot.UnavailableReason ?? "неизвестна"}");
				markdown.AppendLine();
				markdown.AppendLine($"Кэшированной марки {snapshot.BaseCoin}USDT в кэше марок нет.");
			}
		}

		return markdown.ToString();
	}

	/// <summary>
	/// Читает доску опционов по базовому активу и отдаёт компактную проекцию:
	/// строка на страйк с колонками колла и пута — IV, греки, открытый интерес,
	/// бид-аск. Окно проекции — страйки в пределах ±20% от марки базового
	/// актива, максимум 21 ближайший страйк на экспирацию; отсечённые страйки
	/// объявляются в шапке, сырые данные биржи не отдаются. Недоступность биржи
	/// деградирует в последнюю кэшированную проекцию марок с её as-of.
	// Traceability: openspec:chats/sources#scenario-sources-option-board-projection
	// Traceability: openspec:chats/sources#requirement-sources-degradation-cached-asof
	/// </summary>
	/// <param name="baseCoin">Базовый актив, например BTC или ETH.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Markdown-проекция доски опционов.</returns>
	public async Task<string> GetOptionBoardAsync(
		[Description("Базовый актив, например BTC или ETH.")] string baseCoin,
		ChatMarketTraceRecorder? traceRecorder = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(baseCoin);
		var board = await _marketReader.ReadOptionBoardAsync(baseCoin, cancellationToken).ConfigureAwait(false);

		// As-of следа тот же, что у снимка: живая доска или кэшированные марки
		// дают as-of данных, пустой кэш при недоступной бирже — null.
		// Traceability: openspec:chats/history#scenario-chat-source-trace-persisted
		traceRecorder?.Record(
			GetOptionBoardToolName,
			CompactArguments(new { baseCoin }),
			board.IsAvailable || board.Quotes.Count > 0 ? board.AsOf : null);

		if (board.IsAvailable == false)
		{
			return RenderUnavailableBoard(board);
		}

		var markdown = new StringBuilder();
		markdown.AppendLine($"# Доска опционов {board.BaseCoin} {AsOfTag(board.AsOf)}");
		markdown.AppendLine();
		var anchor = board.UnderlyingPrice;
		markdown.AppendLine(anchor is { } anchorPrice
			? $"- Окно проекции: страйки ±20% от марки базового актива {Num(anchorPrice)} USDT, максимум {MaxStrikesPerExpiry} ближайших страйков на экспирацию"
			: $"- Окно проекции: страйки вокруг медианы доски (марка базового актива биржей не отдана), максимум {MaxStrikesPerExpiry} ближайших страйков на экспирацию");
		markdown.AppendLine($"- Инструментов в ответе биржи: {board.TotalTickerCount}, в проекции: {board.Quotes.Count}");
		markdown.AppendLine();
		if (board.Quotes.Count == 0)
		{
			markdown.AppendLine("Доска пуста: биржа не отдала ни одной котировки.");
			return markdown.ToString();
		}

		foreach (var expiryGroup in board.Quotes.GroupBy(quote => quote.Expiry).OrderBy(group => group.Key))
		{
			AppendExpirySection(markdown, expiryGroup.Key, expiryGroup, anchor);
		}

		return markdown.ToString();
	}

	#region Деградация в кэш марок

	/// <summary>
	/// Рендерит недоступную доску: причина сбоя биржи и последняя кэшированная
	/// проекция марок с её as-of — кэш хранит только последние известные цены
	/// опционов, поэтому таблица деградации показывает страйк и марки колла/пута,
	/// без IV, греков и бид-аска; ассистент обязан пометить устаревший as-of и
	/// не давать рыночно-зависимых рекомендаций.
	// Traceability: openspec:chats/sources#scenario-sources-market-down-cached-projection
	/// </summary>
	/// <param name="board">Недоступная доска с кэшированными марками актива.</param>
	/// <returns>Markdown-ответ инструмента при недоступности биржи.</returns>
	private static string RenderUnavailableBoard(ChatOptionBoard board)
	{
		var markdown = new StringBuilder();
		markdown.AppendLine($"# Доска опционов {board.BaseCoin} — биржа недоступна");
		markdown.AppendLine();
		markdown.AppendLine($"Причина: {board.UnavailableReason ?? "неизвестна"}");
		markdown.AppendLine();
		if (board.Quotes.Count == 0)
		{
			markdown.AppendLine($"Кэшированных марок опционов {board.BaseCoin} в кэше марок нет {AsOfTag(board.AsOf)}.");
			return markdown.ToString();
		}

		markdown.AppendLine($"Последняя кэшированная проекция марок {AsOfTag(board.AsOf)}:");
		markdown.AppendLine();
		var anchor = board.UnderlyingPrice;
		markdown.AppendLine(anchor is { } anchorPrice
			? $"- Окно проекции: страйки ±20% от последней кэшированной марки базового актива {Num(anchorPrice)} USDT, максимум {MaxStrikesPerExpiry} ближайших страйков на экспирацию"
			: $"- Окно проекции: страйки вокруг медианы доски (марка базового актива в кэше отсутствует), максимум {MaxStrikesPerExpiry} ближайших страйков на экспирацию");
		markdown.AppendLine($"- Котировок в кэше: {board.Quotes.Count}");
		markdown.AppendLine();
		foreach (var expiryGroup in board.Quotes.GroupBy(quote => quote.Expiry).OrderBy(group => group.Key))
		{
			AppendCachedExpirySection(markdown, expiryGroup.Key, expiryGroup, anchor);
		}

		return markdown.ToString();
	}

	/// <summary>Таблица кэшированных марок одной экспирации: строка на страйк, только марки колла и пута.</summary>
	private static void AppendCachedExpirySection(
		StringBuilder markdown,
		DateOnly expiry,
		IEnumerable<ChatOptionQuote> quotes,
		decimal? boardAnchor)
	{
		var byStrike = quotes.ToLookup(quote => quote.Strike);
		var strikes = byStrike.Select(group => group.Key).ToArray();

		// Якорь окна тот же, что у живой доски: кэшированная марка перпа, без неё —
		// медиана страйков экспирации.
		var anchor = boardAnchor ?? MedianStrike(strikes);
		var selected = SelectWindowStrikes(strikes, anchor);

		markdown.AppendLine($"## Экспирация {expiry:yyyy-MM-dd}");
		markdown.AppendLine();
		if (selected.Count < strikes.Length)
		{
			markdown.AppendLine($"Показаны {selected.Count} из {strikes.Length} страйков экспирации — ближайшие к якорю окна.");
		}

		markdown.AppendLine("| Страйк | Колл марка | Пут марка |");
		markdown.AppendLine("|---|---|---|");
		foreach (var strike in selected)
		{
			var call = byStrike[strike].FirstOrDefault(quote => quote.Type == OptionType.Call);
			var put = byStrike[strike].FirstOrDefault(quote => quote.Type == OptionType.Put);
			markdown.AppendLine($"| {Num(strike)} | {NumOpt(call?.MarkPrice)} | {NumOpt(put?.MarkPrice)} |");
		}

		markdown.AppendLine();
	}

	#endregion

	#region Проекция одной экспирации

	/// <summary>Таблица проекции одной экспирации: строка на страйк, колл и пут в колонках одной строки.</summary>
	private static void AppendExpirySection(
		StringBuilder markdown,
		DateOnly expiry,
		IEnumerable<ChatOptionQuote> quotes,
		decimal? boardAnchor)
	{
		var byStrike = quotes.ToLookup(quote => quote.Strike);
		var strikes = byStrike.Select(group => group.Key).ToArray();

		// Якорь окна: марка базового актива; без неё — медиана страйков самой
		// экспирации, чтобы проекция оставалась детерминированной и компактной.
		var anchor = boardAnchor ?? MedianStrike(strikes);
		var selected = SelectWindowStrikes(strikes, anchor);

		markdown.AppendLine($"## Экспирация {expiry:yyyy-MM-dd}");
		markdown.AppendLine();
		if (selected.Count < strikes.Length)
		{
			markdown.AppendLine($"Показаны {selected.Count} из {strikes.Length} страйков экспирации — ближайшие к якорю окна.");
		}

		markdown.AppendLine("| Страйк | Колл бид/аск | Колл IV | Колл Δ | Колл Γ | Колл Θ | Колл Vega | Колл OI | Пут бид/аск | Пут IV | Пут Δ | Пут Γ | Пут Θ | Пут Vega | Пут OI |");
		markdown.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
		foreach (var strike in selected)
		{
			var call = byStrike[strike].FirstOrDefault(quote => quote.Type == OptionType.Call);
			var put = byStrike[strike].FirstOrDefault(quote => quote.Type == OptionType.Put);
			markdown.AppendLine(
				$"| {Num(strike)} | {BidAsk(call)} | {Iv(call)} | {Greek(call?.Delta)} | {Greek(call?.Gamma)} | {Greek(call?.Theta)} | {Greek(call?.Vega)} | {NumOpt(call?.OpenInterest)}" +
				$" | {BidAsk(put)} | {Iv(put)} | {Greek(put?.Delta)} | {Greek(put?.Gamma)} | {Greek(put?.Theta)} | {Greek(put?.Vega)} | {NumOpt(put?.OpenInterest)} |");
		}

		markdown.AppendLine();
	}

	/// <summary>Страйки окна: в пределах ±20% от якоря, максимум 21 ближайший к якорю.</summary>
	private static IReadOnlyList<decimal> SelectWindowStrikes(decimal[] strikes, decimal? anchor)
	{
		if (anchor is not { } anchorPrice)
		{
			return [.. strikes.OrderBy(strike => strike)];
		}

		var lower = anchorPrice * (1 - BoardWindowShare);
		var upper = anchorPrice * (1 + BoardWindowShare);
		var inWindow = strikes
			.Where(strike => strike >= lower && strike <= upper)
			.OrderBy(strike => Math.Abs(strike - anchorPrice))
			.Take(MaxStrikesPerExpiry)
			.OrderBy(strike => strike)
			.ToArray();
		return inWindow.Length > 0 ? inWindow : [.. strikes.OrderBy(strike => Math.Abs(strike - anchorPrice)).Take(1)];
	}

	/// <summary>Медиана страйков: нижняя из двух средних при чётном числе — детерминизм без округлений.</summary>
	private static decimal? MedianStrike(decimal[] strikes)
	{
		if (strikes.Length == 0)
		{
			return null;
		}

		var ordered = strikes.OrderBy(strike => strike).ToArray();
		return ordered[(ordered.Length - 1) / 2];
	}

	/// <summary>Ячейка бид-аска котировки; котировки нет — прочерк.</summary>
	private static string BidAsk(ChatOptionQuote? quote) =>
		quote is null ? "—" : $"{NumOpt(quote.Bid1Price)}/{NumOpt(quote.Ask1Price)}";

	/// <summary>Ячейка подразумеваемой волатильности в процентах; значения нет — прочерк.</summary>
	private static string Iv(ChatOptionQuote? quote) =>
		quote?.MarkIv is { } iv ? $"{Num(iv * 100)}%" : "—";

	/// <summary>Ячейка грекa; значения нет — прочерк.</summary>
	private static string Greek(decimal? value) => value is { } present ? Num(present) : "—";

	#endregion

	#region Форматирование

	/// <summary>Единый формат as-of отметки ответа инструмента.</summary>
	private static string AsOfTag(DateTimeOffset moment) =>
		$"(as-of: {moment.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)})";

	/// <summary>Число фиксированным инвариантным форматом до восьми знаков: мелкие греки (гамма ~0.00004) не должны обнуляться.</summary>
	private static string Num(decimal value) => value.ToString("0.########", CultureInfo.InvariantCulture);

	/// <summary>Необязательное число или прочерк.</summary>
	private static string NumOpt(decimal? value) => value is { } present ? Num(present) : "—";

	#endregion
}
