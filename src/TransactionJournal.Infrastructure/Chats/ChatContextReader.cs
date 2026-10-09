namespace TransactionJournal.Infrastructure.Chats;

using System.Globalization;
using System.Text;
using TransactionJournal.Application;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Chats.Ports;
using TransactionJournal.Domain;
using TransactionJournal.Infrastructure.Analytics;
using TransactionJournal.Infrastructure.ReadModels;

/// <summary>
/// Адаптер снимка контекста чата: собирает детерминированный
/// markdown-снимок из read-моделей деталей конструкции и метрик журнала плюс
/// компактного индекса корпуса правил; чат без привязки получает
/// портфельный уровень — агрегаты и лимиты журнала с индексом корпуса без
/// раздела конструкции. Каждый раздел несёт собственную as-of
/// отметку сборки, живые подсказки движка в снимок не попадают вовсе —
/// контекст формируется только из фактов журнала и канона правил.
/// Traceability: openspec:chats/context#requirement-chat-context-deterministic-snapshot
/// Traceability: openspec:chats/context#scenario-chat-context-construction-snapshot-with-asof
/// Traceability: openspec:chats/context#scenario-chat-context-hints-excluded
/// Traceability: openspec:chats/context#scenario-chat-context-portfolio-snapshot-without-construction
/// </summary>
public sealed class ChatContextReader : IChatContextReader
{
	private readonly IConstructionDetailReadModel _detailReadModel;

	private readonly IJournalMetricsReadModel _metricsReadModel;

	private readonly IRuleCorpusReader _ruleCorpusReader;

	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт адаптер поверх read-моделей журнала и читателя корпуса правил.</summary>
	/// <param name="detailReadModel">Read-модель деталей конструкции: первичные факты конструкции.</param>
	/// <param name="metricsReadModel">Read-модель метрик журнала: портфельные агрегаты.</param>
	/// <param name="ruleCorpusReader">Читатель корпуса правил: компактный индекс карточек.</param>
	/// <param name="timeProvider">Поставщик момента сборки снимка; по умолчанию системные часы.</param>
	public ChatContextReader(
		IConstructionDetailReadModel detailReadModel,
		IJournalMetricsReadModel metricsReadModel,
		IRuleCorpusReader ruleCorpusReader,
		TimeProvider? timeProvider = null)
	{
		_detailReadModel = detailReadModel;
		_metricsReadModel = metricsReadModel;
		_ruleCorpusReader = ruleCorpusReader;
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	/// <inheritdoc />
	/// <remarks>
	/// Снимок собирается из производных read-моделей: каждая секция читает
	/// актуальные на момент сборки данные, поэтому «as-of» раздела — момент
	/// чтения, а не момент вопроса. Внутри read-модель деталей сама пересчитывает
	/// метрики конструкции; отдельное чтение метрик журнала нужно только для
	/// портфельных агрегатов, дублирование вычисления принимается как цена
	/// разделения read-моделей. Чат без привязки получает портфельный уровень:
	/// агрегаты, лимиты журнала и индекс корпуса — без раздела конструкции.
	/// Traceability: openspec:chats/context#requirement-chat-context-deterministic-snapshot
	/// Traceability: openspec:chats/context#scenario-chat-context-portfolio-snapshot-without-construction
	/// </remarks>
	public async Task<ChatContextSnapshot> ReadAsync(long? constructionId, CancellationToken cancellationToken = default)
	{
		var asOf = _timeProvider.GetUtcNow();

		// Чат без привязки консультируется на портфельном уровне журнала:
		// агрегаты, лимиты журнала и индекс корпуса — без раздела конструкции;
		// привязки нет, поэтому пост-мортем на снимке не выбирается.
		// Traceability: openspec:chats/context#scenario-chat-context-portfolio-snapshot-without-construction
		if (constructionId is null)
		{
			var portfolioMetrics = await _metricsReadModel.ReadAsync(cancellationToken);
			var portfolioRuleIndex = await _ruleCorpusReader.ListIndexAsync(cancellationToken);
			var journalLimits = await _ruleCorpusReader.ReadCardThresholdsAsync(JournalRiskLimitsCardId, cancellationToken);

			var portfolioMarkdown = new StringBuilder();
			portfolioMarkdown.AppendLine($"# Снимок контекста чата {AsOfTag(asOf)}");
			portfolioMarkdown.AppendLine();
			AppendPortfolioSection(portfolioMarkdown, portfolioMetrics, asOf);
			AppendJournalLimitsSection(portfolioMarkdown, portfolioMetrics, journalLimits, asOf);
			AppendRuleIndexSection(portfolioMarkdown, portfolioRuleIndex, asOf);

			return new ChatContextSnapshot
			{
				Markdown = portfolioMarkdown.ToString(),
				AsOf = asOf,
				IsConstructionClosed = false,
			};
		}

		var detail = await _detailReadModel.ReadAsync(constructionId.Value, cancellationToken);
		var metrics = await _metricsReadModel.ReadAsync(cancellationToken);
		var ruleIndex = await _ruleCorpusReader.ListIndexAsync(cancellationToken);

		// Конструкции не в открытом статусе уже разобраны: снимок строится по
		// финальному состоянию, промпт пост-мортема выбирает другой режим работы.
		// Traceability: openspec:chats/context#requirement-chat-context-deterministic-snapshot
		var isClosed = detail.Status is ConstructionStatus.Closed or ConstructionStatus.Archived;

		var markdown = new StringBuilder();
		markdown.AppendLine($"# Снимок контекста чата {AsOfTag(asOf)}");
		markdown.AppendLine();
		AppendConstructionSection(markdown, detail, asOf);
		AppendPortfolioSection(markdown, metrics, asOf);
		AppendLimitsSection(markdown, detail, asOf);
		AppendRuleIndexSection(markdown, ruleIndex, asOf);

		return new ChatContextSnapshot
		{
			Markdown = markdown.ToString(),
			AsOf = asOf,
			IsConstructionClosed = isClosed,
		};
	}

	#region Секции снимка

	/// <summary>Раздел первичных фактов конструкции: описание, позиции, результат.</summary>
	private static void AppendConstructionSection(StringBuilder markdown, ConstructionDetailData detail, DateTimeOffset asOf)
	{
		markdown.AppendLine($"## Конструкция «{detail.Name}» {AsOfTag(asOf)}");
		markdown.AppendLine();
		markdown.AppendLine($"- Идентификатор: {detail.ConstructionId}");
		markdown.AppendLine($"- Статус: {StatusText(detail.Status)}");
		markdown.AppendLine(detail.AllocatedCapitalUsdt is { } capital
			? $"- Выделенный капитал: {Num(capital)} USDT"
			: "- Выделенный капитал: не задан");
		if (detail.Metrics.OpenedAt is { } openedAt)
		{
			markdown.AppendLine($"- Открыта: {Stamp(openedAt)}");
		}

		if (detail.Metrics.ClosedAt is { } closedAt)
		{
			markdown.AppendLine($"- Закрыта: {Stamp(closedAt)}");
		}

		if (detail.Comment is { } comment)
		{
			markdown.AppendLine($"- Комментарий владельца: {comment}");
		}

		if (detail.Positions.Count == 0)
		{
			markdown.AppendLine("- Позиций нет");
		}
		else
		{
			markdown.AppendLine($"- Позиции ({detail.Positions.Count}):");
			markdown.AppendLine("  | Инструмент | Остаток | Состояние | Средняя цена входа | Средняя цена выхода | Реализ. PnL | Нереализ. PnL | Общий PnL | Комиссии |");
			markdown.AppendLine("  |---|---|---|---|---|---|---|---|---|");
			foreach (var position in detail.Positions)
			{
				markdown.AppendLine(
					$"  | {position.Symbol} | {Num(position.Residual)} | {(position.IsOpen ? "открыта" : "закрыта")}" +
					$" | {NumOpt(position.AverageEntryPrice)} | {NumOpt(position.AverageClosePrice)}" +
					$" | {Num(position.RealizedPnL)} | {NumOpt(position.UnrealizedPnL, UnavailableMarkText)} | {NumOpt(position.TotalPnL, UnavailableMarkText)}" +
					$" | {Num(position.AccumulatedFees)} |");
			}
		}

		AppendPositionComments(markdown, detail.Positions);

		// Результат приводится с внешними корректировками: итог конструкции
		// по определению включает реализованную, нереализованную части и корректировки.
		// Traceability: openspec:analytics/performance#scenario-construction-total-includes-adjustments
		var m = detail.Metrics;
		markdown.AppendLine(
			$"- Результат: реализованный {Num(m.RealizedPnL)} USDT, нереализованный {NumOpt(m.UnrealizedPnL, UnavailableMarkText)}" +
			$", корректировки {Num(m.AdjustmentsPnL)} USDT, итог {NumOpt(m.TotalPnL, UnavailableMarkText)} USDT" +
			$"{PercentTail(m.TotalPnLPercent)}");
		if (detail.MarksAsOf is { } marksAsOf)
		{
			markdown.AppendLine($"- Марки оценки: {Stamp(marksAsOf)}");
		}

		markdown.AppendLine();
	}

	/// <summary>Комментарии позиций отдельным списком — в таблицу они не помещаются.</summary>
	private static void AppendPositionComments(StringBuilder markdown, IReadOnlyList<ConstructionPositionRow> positions)
	{
		if (positions.Any(position => position.Comment is not null) == false)
		{
			return;
		}

		markdown.AppendLine("- Комментарии позиций:");
		foreach (var position in positions.Where(position => position.Comment is not null))
		{
			markdown.AppendLine($"  - {position.Symbol}: {position.Comment}");
		}
	}

	/// <summary>Раздел портфельных агрегатов: масштаб журнала вокруг конструкции.</summary>
	private static void AppendPortfolioSection(StringBuilder markdown, JournalMetrics metrics, DateTimeOffset asOf)
	{
		markdown.AppendLine($"## Портфельные агрегаты {AsOfTag(asOf)}");
		markdown.AppendLine();
		markdown.AppendLine($"- Конструкций в журнале: {metrics.Constructions.Count}, из них с открытыми позициями: {OpenConstructionsCount(metrics)}");
		markdown.AppendLine($"- Реализованный PnL журнала: {Num(metrics.RealizedPnL)} USDT");
		markdown.AppendLine(metrics.UnrealizedPnL is { } unrealized
			? $"- Нереализованный PnL журнала: {Num(unrealized)} USDT"
			: $"- Нереализованный PnL журнала: недоступна ({UnavailableMarkText})");
		markdown.AppendLine(metrics.TotalPnL is { } total
			? $"- Итог по журналу: {Num(total)} USDT"
			: "- Итог по журналу: неполный (сбой марок)");
		markdown.AppendLine();
	}

	/// <summary>Число конструкций с ненулевым остатком хотя бы одной позиции.</summary>
	private static int OpenConstructionsCount(JournalMetrics metrics) =>
		metrics.Positions
			.Where(position => position.Residual != 0)
			.Select(position => position.ConstructionId)
			.Distinct()
			.Count();

	/// <summary>Раздел лимитов конструкции: риск и профит обеими единицами.</summary>
	private static void AppendLimitsSection(StringBuilder markdown, ConstructionDetailData detail, DateTimeOffset asOf)
	{
		markdown.AppendLine($"## Лимиты конструкции {AsOfTag(asOf)}");
		markdown.AppendLine();
		if (detail.RiskPercent is null && detail.ProfitPercent is null)
		{
			markdown.AppendLine("- Плановые границы риска и профита не заданы");
			markdown.AppendLine();
			return;
		}

		// Обе единицы выводятся всегда: введённая единица первоисточник,
		// вторая вычислена конвертером от капитала.
		// Traceability: openspec:analytics/performance#requirement-risk-profit-unit-conversion
		markdown.AppendLine(detail.RiskPercent is { } riskPercent
			? $"- Риск: {Num(riskPercent)}% капитала = {NumOpt(detail.RiskUsdt)} USDT"
			: "- Риск: не задан");
		markdown.AppendLine(detail.ProfitPercent is { } profitPercent
			? $"- Профит: {Num(profitPercent)}% капитала = {NumOpt(detail.ProfitUsdt)} USDT"
			: "- Профит: не задан");
		markdown.AppendLine();
	}

	/// <summary>
	/// Раздел лимитов журнала: совокупный выделенный капитал портфеля и
	/// периодные лимиты риска из карточки корпуса с денежным масштабом;
	/// отсутствие порогов в корпусе не ломает снимок — раздел деградирует
	/// явным текстом.
	/// Traceability: openspec:chats/context#scenario-chat-context-portfolio-snapshot-without-construction
	/// </summary>
	private static void AppendJournalLimitsSection(StringBuilder markdown, JournalMetrics metrics, IReadOnlyList<RuleCardThreshold> thresholds, DateTimeOffset asOf)
	{
		markdown.AppendLine($"## Лимиты журнала {AsOfTag(asOf)}");
		markdown.AppendLine();

		// Совокупный выделенный капитал — процентная база лимитов периода:
		// тот же принцип масштабирования, что и у триггера лимитов периода движка.
		var totalCapital = metrics.Constructions
			.Select(construction => construction.AllocatedCapitalUsdt)
			.Sum(capital => capital ?? 0m);
		markdown.AppendLine(totalCapital > 0m
			? $"- Совокупный выделенный капитал: {Num(totalCapital)} USDT"
			: "- Совокупный выделенный капитал: не задан");

		var weekly = ThresholdPercent(thresholds, "weeklyRiskLimit");
		var monthly = ThresholdPercent(thresholds, "monthlyRiskLimit");
		var quarterly = ThresholdPercent(thresholds, "quarterlyRiskLimit");
		if (weekly is null && monthly is null && quarterly is null)
		{
			markdown.AppendLine("- Периодные лимиты риска в корпусе не заданы");
			markdown.AppendLine();
			return;
		}

		markdown.AppendLine(JournalLimitLine("на неделю", weekly, totalCapital));
		markdown.AppendLine(JournalLimitLine("на месяц", monthly, totalCapital));
		markdown.AppendLine(JournalLimitLine("на квартал", quarterly, totalCapital));
		markdown.AppendLine();
	}

	/// <summary>Процентный порог лимита периода по имени; порога нет, единица не проценты или величина не число — null.</summary>
	private static decimal? ThresholdPercent(IReadOnlyList<RuleCardThreshold> thresholds, string name) =>
		thresholds
			.Where(threshold => string.Equals(threshold.Name, name, StringComparison.Ordinal) && string.Equals(threshold.Unit, "percent", StringComparison.Ordinal))
			.Select(threshold => decimal.TryParse(threshold.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed) ? parsed : (decimal?)null)
			.FirstOrDefault(value => value is not null);

	/// <summary>Строка лимита периода: процент капитала и денежный масштаб при заданном капитале.</summary>
	private static string JournalLimitLine(string period, decimal? percent, decimal totalCapital)
	{
		if (percent is null)
		{
			return $"- Лимит убытка {period}: не задан";
		}

		return totalCapital > 0m
			? $"- Лимит убытка {period}: {Num(percent.Value)}% капитала (= {Num(percent.Value / 100m * totalCapital)} USDT)"
			: $"- Лимит убытка {period}: {Num(percent.Value)}% капитала";
	}

	/// <summary>
	/// Раздел индекса корпуса правил: компактный перечень карточек без полного
	/// текста — полное содержание читается отдельным инструментом по id.
	/// Traceability: openspec:chats/context#scenario-chat-context-card-index-only
	/// </summary>
	private static void AppendRuleIndexSection(StringBuilder markdown, IReadOnlyList<RuleCardSummary> ruleIndex, DateTimeOffset asOf)
	{
		markdown.AppendLine($"## Индекс корпуса правил {AsOfTag(asOf)}");
		markdown.AppendLine();
		markdown.AppendLine("Полный текст карточки читается инструментом read_rule_card по её id.");
		foreach (var card in ruleIndex)
		{
			markdown.AppendLine($"- {card.Id} — {card.Title} — {card.Summary} (статус: {card.Status})");
		}

		markdown.AppendLine();
	}

	#endregion

	#region Форматирование

	/// <summary>Текст недоступной нереализованной оценки при сбое марок.</summary>
	private const string UnavailableMarkText = "оценка недоступна: сбой марок";

	/// <summary>
	/// Карточка корпуса — источник периодных лимитов риска журнала: та же
	/// карточка, что читает триггер лимитов периода движка; пороги в коде
	/// не дублируются, значения берутся из канона на каждом чтении.
	/// Traceability: openspec:chats/context#scenario-chat-context-portfolio-snapshot-without-construction
	/// </summary>
	private const string JournalRiskLimitsCardId = "ac-01";

	/// <summary>Единый формат as-of отметки раздела снимка.</summary>
	private static string AsOfTag(DateTimeOffset moment) => $"(as-of: {Stamp(moment)})";

	/// <summary>Момент в UTC фиксированным форматом — детерминизм снимка.</summary>
	private static string Stamp(DateTimeOffset moment) =>
		moment.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

	/// <summary>Число фиксированным инвариантным форматом до четырёх знаков.</summary>
	private static string Num(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

	/// <summary>Необязательное число или запасной текст.</summary>
	private static string NumOpt(decimal? value, string fallback = "—") =>
		value is { } present ? Num(present) : fallback;

	/// <summary>Хвост «(x% капитала)» для итога при заданном капитале.</summary>
	private static string PercentTail(decimal? percent) =>
		percent is { } present ? $" ({Num(present)}% капитала)" : string.Empty;

	/// <summary>Человекочитаемое имя статуса конструкции.</summary>
	private static string StatusText(ConstructionStatus status) => status switch
	{
		ConstructionStatus.Open => "открыта",
		ConstructionStatus.Closed => "закрыта",
		ConstructionStatus.Archived => "архив",
		_ => status.ToString(),
	};

	#endregion
}
