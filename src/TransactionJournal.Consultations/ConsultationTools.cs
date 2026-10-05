namespace TransactionJournal.Consultations;

using System.ComponentModel;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;
using TransactionJournal.Consultations.Ports;
using TransactionJournal.Domain.Materialization;

/// <summary>
/// Реестр инструментов консультаций: ровно три read-only инструмента —
/// чтение карточки правила по id, снимок фьючерсного рынка и компактная
/// проекция доски опционов по базовому активу. Пишущих инструментов и
/// инструментов по чужим конструкциям нет: реестр собирается только из
/// этих трёх функций, каждое рыночное чтение выполняет ровно один биржевой
/// запрос через порт рыночных данных. Результаты инструментов — markdown:
/// компактная проекция вместо сырых данных биржи.
// Traceability: openspec:consultations/tools#requirement-tools-read-only-registry
// Traceability: openspec:consultations/tools#requirement-tools-single-request-per-call
/// </summary>
public sealed class ConsultationTools
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

	private readonly IRuleCorpusReader _ruleCorpusReader;

	private readonly IConsultationMarketReader _marketReader;

	/// <summary>Создаёт реестр поверх читателя корпуса и порта рыночных данных.</summary>
	/// <param name="ruleCorpusReader">Читатель корпуса правил: полный текст карточки только по id.</param>
	/// <param name="marketReader">Порт рыночных данных: одно чтение — один биржевой запрос.</param>
	public ConsultationTools(IRuleCorpusReader ruleCorpusReader, IConsultationMarketReader marketReader)
	{
		_ruleCorpusReader = ruleCorpusReader ?? throw new ArgumentNullException(nameof(ruleCorpusReader));
		_marketReader = marketReader ?? throw new ArgumentNullException(nameof(marketReader));
	}

	/// <summary>
	/// Собирает реестр инструментов ассистента: ровно три read-only функции
	/// с фиксированными именами; проверка реестра показывает отсутствие
	/// пишущих инструментов.
	// Traceability: openspec:consultations/tools#scenario-tools-no-write-tools
	/// </summary>
	/// <returns>Список из трёх функций инструментов.</returns>
	public IReadOnlyList<AIFunction> CreateTools() =>
	[
		AIFunctionFactory.Create(
			ReadRuleCardAsync,
			new AIFunctionFactoryOptions { Name = ReadRuleCardToolName, Description = "Возвращает полный текст карточки правила корпуса по её id из индекса." }),
		AIFunctionFactory.Create(
			GetMarketSnapshotAsync,
			new AIFunctionFactoryOptions { Name = GetMarketSnapshotToolName, Description = "Возвращает снимок фьючерсного рынка по базовому активу: марка, бид-аск, открытый интерес, ставка фандинга." }),
		AIFunctionFactory.Create(
			GetOptionBoardAsync,
			new AIFunctionFactoryOptions { Name = GetOptionBoardToolName, Description = "Возвращает компактную проекцию доски опционов по базовому активу: страйки с IV, греками, открытым интересом и бид-аском." }),
	];

	/// <summary>
	/// Читает полный текст карточки правила по id из индекса корпуса; карточки
	/// с таким id нет — инструмент отвечает текстом об отсутствии, а не ошибкой.
	// Traceability: openspec:consultations/tools#scenario-tools-card-by-id
	/// </summary>
	/// <param name="cardId">Идентификатор карточки из индекса корпуса, например ac-01.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Полный текст карточки или сообщение об отсутствии.</returns>
	public async Task<string> ReadRuleCardAsync(
		[Description("Идентификатор карточки из индекса корпуса, например ac-01.")] string cardId,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(cardId);
		var card = await _ruleCorpusReader.ReadCardAsync(cardId, cancellationToken).ConfigureAwait(false);
		return card is null
			? $"Карточка «{cardId}» в корпусе правил не найдена."
			: card.Text;
	}

	/// <summary>
	/// Читает снимок фьючерсного рынка по базовому активу: одно чтение порта —
	/// один биржевой запрос; недоступность биржи отдаётся структурированным
	/// «недоступно» с причиной и последней кэшированной маркой с её as-of.
	// Traceability: openspec:consultations/tools#requirement-tools-single-request-per-call
	// Traceability: openspec:consultations/tools#requirement-tools-degradation-cached-asof
	/// </summary>
	/// <param name="baseCoin">Базовый актив, например BTC или ETH.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Markdown-снимок рынка.</returns>
	public async Task<string> GetMarketSnapshotAsync(
		[Description("Базовый актив, например BTC или ETH.")] string baseCoin,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(baseCoin);
		var snapshot = await _marketReader.ReadSnapshotAsync(baseCoin, cancellationToken).ConfigureAwait(false);
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
			// Traceability: openspec:consultations/tools#scenario-tools-market-down-cached-projection
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
	// Traceability: openspec:consultations/tools#scenario-tools-option-board-projection
	// Traceability: openspec:consultations/tools#requirement-tools-degradation-cached-asof
	/// </summary>
	/// <param name="baseCoin">Базовый актив, например BTC или ETH.</param>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Markdown-проекция доски опционов.</returns>
	public async Task<string> GetOptionBoardAsync(
		[Description("Базовый актив, например BTC или ETH.")] string baseCoin,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(baseCoin);
		var board = await _marketReader.ReadOptionBoardAsync(baseCoin, cancellationToken).ConfigureAwait(false);
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
	// Traceability: openspec:consultations/tools#scenario-tools-market-down-cached-projection
	/// </summary>
	/// <param name="board">Недоступная доска с кэшированными марками актива.</param>
	/// <returns>Markdown-ответ инструмента при недоступности биржи.</returns>
	private static string RenderUnavailableBoard(ConsultationOptionBoard board)
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
		IEnumerable<ConsultationOptionQuote> quotes,
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
		IEnumerable<ConsultationOptionQuote> quotes,
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
	private static string BidAsk(ConsultationOptionQuote? quote) =>
		quote is null ? "—" : $"{NumOpt(quote.Bid1Price)}/{NumOpt(quote.Ask1Price)}";

	/// <summary>Ячейка подразумеваемой волатильности в процентах; значения нет — прочерк.</summary>
	private static string Iv(ConsultationOptionQuote? quote) =>
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
