namespace TransactionJournal.Hints.Engine;

using System.Globalization;
using TransactionJournal.Hints.Corpus;

/// <summary>
/// Чтение порогов условия из карточки правила: единственный источник числовых
/// порогов триггеров — thresholds карточки, код движка порогов не содержит.
/// Значение порога хранится строкой (бывают диапазоны и доли), поэтому парсинг
/// инвариантной культурой лежит здесь; нечисловое значение порога делает
/// условие непроверяемым — триггер не срабатывает.
/// Traceability: openspec:hints/engine-pass#requirement-engine-deterministic-selection
/// </summary>
public static class CorpusThresholds
{
	/// <summary>Читает числовой порог по имени; null — порога нет или он не число.</summary>
	/// <param name="card">Карточка правила.</param>
	/// <param name="name">Имя порога, на которое ссылается триггер.</param>
	public static decimal? Decimal(RuleCard card, string name)
	{
		var raw = Raw(card, name);
		return raw is not null
			&& decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
				? value
				: null;
	}

	/// <summary>Читает порог-долю вида «1/3»; null — порога нет или доля не разбирается.</summary>
	/// <param name="card">Карточка правила.</param>
	/// <param name="name">Имя порога, на которое ссылается триггер.</param>
	public static decimal? Fraction(RuleCard card, string name)
	{
		var raw = Raw(card, name);
		if (raw is null)
		{
			return null;
		}

		var parts = raw.Split('/');
		if (parts.Length == 2
			&& decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var numerator)
			&& decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var denominator)
			&& denominator != 0m)
		{
			return numerator / denominator;
		}

		return Decimal(card, name);
	}

	/// <summary>Читает порог-диапазон вида «7-10»; null — порога нет или диапазон не разбирается.</summary>
	/// <param name="card">Карточка правила.</param>
	/// <param name="name">Имя порога, на которое ссылается триггер.</param>
	public static (decimal Min, decimal Max)? Range(RuleCard card, string name)
	{
		var raw = Raw(card, name);
		if (raw is null)
		{
			return null;
		}

		var parts = raw.Split('-');
		if (parts.Length == 2
			&& decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var min)
			&& decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var max)
			&& min <= max)
		{
			return (min, max);
		}

		return null;
	}

	/// <summary>Сырое значение порога карточки по имени; null — порога с таким именем нет.</summary>
	private static string? Raw(RuleCard card, string name)
		=> card.Thresholds.FirstOrDefault(threshold => threshold.Name == name)?.Value;
}
