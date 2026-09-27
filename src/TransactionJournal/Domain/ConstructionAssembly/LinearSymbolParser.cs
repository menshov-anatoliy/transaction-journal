namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Разбор символа линейного фьючерса Bybit для привязки сделок робота:
/// базовый актив получается отбрасыванием хвостового суффикса котируемой
/// валюты USDT (ETHUSDT → ETH) и сопоставляется с опционными активами
/// точно, по строке.
// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#requirement-robot-trade-binding
/// Traceability: change:add-construction-auto-assembly/design#d3
/// </summary>
public static class LinearSymbolParser
{
	/// <summary>Суффикс котируемой валюты линейных фьючерсов журнала.</summary>
	private const string QuoteSuffix = "USDT";

	/// <summary>
	/// Выделяет базовый актив linear-символа; для строк вне формата возвращает false без исключений.
	/// </summary>
	/// <param name="symbol">Символ фьючерса, например ETHUSDT.</param>
	/// <param name="baseCoin">Базовый актив (например, ETH) или null при неудаче.</param>
	public static bool TryParseBaseCoin(string? symbol, out string? baseCoin)
	{
		baseCoin = null;
		if (string.IsNullOrWhiteSpace(symbol))
		{
			return false;
		}

		// Актив — всё, кроме хвостового USDT: пустой остаток («USDT» без актива)
		// символом фьючерса не является.
		if (symbol.EndsWith(QuoteSuffix, StringComparison.Ordinal) == false
			|| symbol.Length <= QuoteSuffix.Length)
		{
			return false;
		}

		baseCoin = symbol[..^QuoteSuffix.Length];
		return true;
	}
}
