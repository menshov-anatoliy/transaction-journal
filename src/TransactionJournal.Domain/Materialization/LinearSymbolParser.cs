namespace TransactionJournal.Domain.Materialization;

/// <summary>
/// Разбор символа вечного линейного фьючерса Bybit формата {BASE}{QUOTE} с
/// QUOTE ∈ {USDT, USDC}: сплошная строка без дефисных сегментов, хвост — котируемая
/// валюта, всё перед ним — базовый актив. Сравнения идут порядковые, без культуры,
/// поэтому разбор не зависит от региональных настроек; регистр символа сохраняется.
/// Опционы ({BASE}-{dMMMyy}-{strike}-{C|P}[-{QUOTE}]) и датированные фьючерсы
/// ({BASE}{QUOTE}-{экспирация}) содержат дефис и этим парсером не распознаются.
/// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
/// </summary>
public static class LinearSymbolParser
{
	/// <summary>Котируемые валюты вечных линейных фьючерсов: живая доска в USDT, исторические — в USDC.</summary>
	private static readonly string[] QuoteCoins = ["USDT", "USDC"];

	/// <summary>
	/// Разбирает символ вечного линейного фьючерса; для строк вне формата возвращает false без исключений.
	/// </summary>
	/// <param name="symbol">Символ фьючерса, например XAUTUSDT или ETHUSDC.</param>
	/// <param name="parts">Разобранные части символа или null при неудаче.</param>
	public static bool TryParse(string? symbol, out LinearSymbolParts? parts)
	{
		parts = null;
		if (string.IsNullOrWhiteSpace(symbol))
		{
			return false;
		}

		// Пробельные знаки внутри символа — мусор, а не часть формата перпа.
		if (symbol.Any(char.IsWhiteSpace))
		{
			return false;
		}

		// Вечный перп пишется сплошной строкой: любой дефис — признак сегментированного
		// символа (опцион или датированный фьючерс), линейной ногой не являющегося.
		// Traceability: openspec:analytics/performance#scenario-real-risk-unparseable-symbol-is-null
		if (symbol.Contains('-'))
		{
			return false;
		}

		// Хвост символа обязан быть котируемой валютой USDT/USDC в верхнем регистре;
		// всё перед ним — базовый актив в исходном регистре. Пустой остаток («USDT»
		// без актива) символом фьючерса не является.
		foreach (var quoteCoin in QuoteCoins)
		{
			if (symbol.EndsWith(quoteCoin, StringComparison.Ordinal) == false)
			{
				continue;
			}

			var baseCoin = symbol[..^quoteCoin.Length];
			if (baseCoin.Length == 0)
			{
				return false;
			}

			parts = new LinearSymbolParts { BaseCoin = baseCoin, QuoteCoin = quoteCoin };
			return true;
		}

		return false;
	}

	/// <summary>
	/// Разбирает символ вечного линейного фьючерса или выбрасывает исключение формата —
	/// строгий вариант для мест, где символ обязан быть перпом.
	/// </summary>
	/// <param name="symbol">Символ фьючерса, например XAUTUSDT или ETHUSDC.</param>
	/// <exception cref="FormatException">Символ не соответствует формату {BASE}{QUOTE} с QUOTE ∈ {USDT, USDC}.</exception>
	public static LinearSymbolParts Parse(string symbol)
	{
		if (TryParse(symbol, out var parts) == false)
		{
			throw new FormatException(
				$"Символ «{symbol}» не соответствует формату вечного линейного фьючерса {{BASE}}{{QUOTE}} с QUOTE ∈ {{USDT, USDC}}.");
		}

		return parts!;
	}
}
