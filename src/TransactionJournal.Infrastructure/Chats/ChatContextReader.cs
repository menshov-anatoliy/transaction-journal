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
/// компактного индекса корпуса правил. Каждый раздел несёт собственную as-of
/// отметку сборки, живые подсказки движка в снимок не попадают вовсе —
/// контекст формируется только из фактов журнала и канона правил.
/// Traceability: openspec:chats/context#requirement-chat-context-deterministic-snapshot
/// Traceability: openspec:chats/context#scenario-chat-context-construction-snapshot-with-asof
/// Traceability: openspec:chats/context#scenario-chat-context-hints-excluded
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
	/// разделения read-моделей.
	/// Traceability: openspec:chats/context#requirement-chat-context-deterministic-snapshot
	/// </remarks>
	public async Task<ChatContextSnapshot> ReadAsync(long? constructionId, CancellationToken cancellationToken = default)
	{
		// Чат без привязки консультируется на портфельном уровне журнала —
		// агрегаты и лимиты с индексом корпуса без раздела конструкции.
		// Портфельная ветка снимка появляется в change add-agent-chat; до неё
		// запрос без привязки отвергается явно, а не молча.
		// Traceability: openspec:chats/context#scenario-chat-context-portfolio-snapshot-without-construction
		if (constructionId is null)
		{
			throw new NotSupportedException("Снимок контекста чата без привязки к конструкции пока не поддерживается.");
		}

		var asOf = _timeProvider.GetUtcNow();
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
