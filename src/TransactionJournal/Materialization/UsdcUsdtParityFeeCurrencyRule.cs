namespace TransactionJournal.Materialization;

/// <summary>
/// Правило приведения валюты комиссии по ADR-0001: USDC учитывается как USDT
/// в паритете 1:1, потому что журнал ведёт учёт в одной валюте; прочие валюты биржи
/// (например, BTC у опционов) остаются как есть, а пустое значение остаётся пустым.
// Traceability: adr:docs/adr/0001-usdc-usdt-parity.md#usdc-usdt-parity-1-1
/// </summary>
public sealed class UsdcUsdtParityFeeCurrencyRule : IFeeCurrencyRule
{
	/// <summary>Единственный экземпляр правила: состояние правилу не нужно.</summary>
	public static UsdcUsdtParityFeeCurrencyRule Instance { get; } = new();

	private UsdcUsdtParityFeeCurrencyRule()
	{
	}

	/// <inheritdoc cref="IFeeCurrencyRule.Canonicalize" />
	public string? Canonicalize(string? feeCurrency)
	{
		if (string.IsNullOrWhiteSpace(feeCurrency))
		{
			return null;
		}

		return string.Equals(feeCurrency, "USDC", StringComparison.Ordinal) ? "USDT" : feeCurrency;
	}
}
