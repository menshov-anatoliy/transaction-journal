using System.Globalization;

namespace TransactionJournal.Application.Analytics;

/// <summary>
/// Предупреждение об экспирации: чисто временнАя запись об открытой опционной
/// ноге с остатком не более четырнадцати полных суток — инструмент, остаток
/// суток и дневной распад в готовом тексте на русском языке. Положение цены
/// к страйку на срабатывание не влияет; запись вычисляется при чтении рядом
/// с метриками и хранением не живёт, а канал агента подсказок остаётся
/// отдельным слоем со своим триггером.
// Traceability: openspec:analytics/option-lifetime#requirement-expiry-warning-two-weeks
/// </summary>
public sealed record OptionExpiryWarning
{
	/// <summary>Инструмент с приближающейся экспирацией.</summary>
	public required string Symbol { get; init; }

	/// <summary>Остаток полных суток до экспирации на момент вычисления.</summary>
	public required int DaysToExpiry { get; init; }

	/// <summary>Дневной распад ноги, USDT/день; null без рыночных данных.</summary>
	public required decimal? DailyDecayUsdt { get; init; }

	/// <summary>Готовый текст записи на русском языке.</summary>
	public required string Text { get; init; }

	/// <summary>Собирает запись предупреждения с текстом об инструменте, остатке суток и дневном распаде.</summary>
	/// <param name="symbol">Инструмент с приближающейся экспирацией.</param>
	/// <param name="daysToExpiry">Остаток полных суток до экспирации.</param>
	/// <param name="dailyDecayUsdt">Дневной распад ноги; null без рыночных данных.</param>
	/// <returns>Запись предупреждения об экспирации.</returns>
	internal static OptionExpiryWarning Create(string symbol, int daysToExpiry, decimal? dailyDecayUsdt) => new()
	{
		Symbol = symbol,
		DaysToExpiry = daysToExpiry,
		DailyDecayUsdt = dailyDecayUsdt,
		Text = dailyDecayUsdt == null
			? $"{symbol}: до экспирации {daysToExpiry} полных сут. — распад неизвестен, нет рыночных данных."
			: $"{symbol}: до экспирации {daysToExpiry} полных сут., распад {FormatNumber(dailyDecayUsdt.Value)} USDT/день.",
	};

	/// <summary>Число без хвостовых нулей в инвариантном формате.</summary>
	private static string FormatNumber(decimal value) =>
		value.ToString("0.############################", CultureInfo.InvariantCulture);
}
