using System.Globalization;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain.Materialization;

namespace TransactionJournal.Infrastructure.UseCases;

/// <summary>
/// Материал ручных синтетических связок над материализованными сделками
/// конструкции: единый ключ сделки («execId» биржевой либо «manual:{id}» ручной),
/// поиск сделки по идентификатору связки и вывод закрываемой ноги — опциона
/// противоположного типа на той же доске (базовый актив, страйк и экспирация
/// совпадают со встречным опционом), существующего длинной частью ноги.
/// Символ закрываемой ноги детерминирован: первый по прогону
/// «время, ключ»symbol ноги — тот же приём, что у распознавателя связок.
/// </summary>
// Помощник общий для сервиса ручных связок (валидация создания) и read-модели
// деталей (включение ручных связок в логический слой): правила вывода закрываемой
// ноги едины в обоих местах.
// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
internal static class SyntheticCloseManualLinkMaterial
{
	/// <summary>Категория опционных записей журнала — материал сопоставления связок.</summary>
	private const string OptionCategory = "option";

	/// <summary>Строит единый ключ сделки: execId биржевой, ключ строки сырья ручной.</summary>
	public static string TradeKey(MaterializedTrade trade) =>
		trade.ExecId ?? $"manual:{trade.ManualTradeId?.ToString(CultureInfo.InvariantCulture)}";

	/// <summary>Ищет сделку конструкции по идентификатору связки.</summary>
	public static MaterializedTrade? FindTrade(IReadOnlyList<MaterializedTrade> trades, string tradeId) =>
		trades.FirstOrDefault(trade => string.Equals(TradeKey(trade), tradeId, StringComparison.Ordinal));

	/// <summary>
	/// Выводит закрываемую ногу ручной связки: опцион противоположного типа
	/// встречному на той же доске должен существовать в конструкции длинной
	/// частью (суммарное количество ноги положительно). Возвращает символ ноги
	/// и её страйк с экспирацией — вход расчёта лока и проверки совпадения
	/// страйка/экспирации встречного опциона с закрываемой ногой.
	/// </summary>
	public static bool TryResolveClosedLeg(
		IReadOnlyList<MaterializedTrade> trades,
		OptionSymbolParts counterParts,
		out string? closedLegSymbol)
	{
		closedLegSymbol = null;
		var closedType = counterParts.Type == OptionType.Call ? OptionType.Put : OptionType.Call;
		var closedKey = new LegKey(counterParts.BaseCoin, counterParts.ExpiryDate, counterParts.Strike, closedType);

		// Прогон по сделкам конструкции в порядке «время исполнения, ключ»:
		// первый встреченный инструмент закрываемой ноги — детерминированное имя,
		// повторное чтение над тем же составом даёт тот же символ.
		var net = 0m;
		foreach (var trade in trades
			.Where(trade => string.Equals(trade.Category, OptionCategory, StringComparison.Ordinal))
			.OrderBy(trade => trade.ExecutedAt)
			.ThenBy(TradeKey, StringComparer.Ordinal))
		{
			if (OptionSymbolParser.TryParse(trade.Symbol, out var parts) == false || parts is null)
			{
				continue;
			}

			var key = new LegKey(parts.BaseCoin, parts.ExpiryDate, parts.Strike, parts.Type);
			if (key.Equals(closedKey))
			{
				closedLegSymbol ??= trade.Symbol;
				net += trade.Quantity;
			}
		}

		// Закрываемая нога обязана быть длинной: связка закрывает часть длинной
		// ноги, из её открывающих частей считается стоимость входа в локе.
		return closedLegSymbol is not null && net > 0m;
	}

	/// <summary>Ключ опционной ноги: доска определяется активом, экспирацией, страйком и типом.</summary>
	private readonly record struct LegKey(string BaseCoin, DateTime ExpiryDate, decimal Strike, OptionType Type);
}
