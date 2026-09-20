using System.Globalization;
using System.Text.Json.Serialization;

namespace TransactionJournal.Bybit;

/// <summary>
/// Ответ публичного эндпоинта GET /v5/market/time: серверное время строками секунд
/// и наносекунд; используется для сверки часов перед подписью запросов.
/// Traceability: change:add-bybit-sync/design#d6
/// </summary>
public sealed class BybitServerTime
{
	/// <summary>Число разрядов наносекундной строки, составляющих миллисекунды.</summary>
	private const int MillisecondsDigitsCount = 13;

	/// <summary>Серверное время в секундах (строка биржи).</summary>
	[JsonPropertyName("timeSecond")]
	public string TimeSecond { get; init; } = string.Empty;

	/// <summary>Серверное время в наносекундах (строка биржи).</summary>
	[JsonPropertyName("timeNano")]
	public string TimeNano { get; init; } = string.Empty;

	/// <summary>
	/// Миллисекунды серверного времени — первые 13 разрядов timeNano; из них
	/// рассчитывается смещение локальных часов для корректного timestamp подписи.
	/// </summary>
	/// <exception cref="InvalidOperationException">Строка timeNano отсутствует или короче 13 разрядов.</exception>
	public long Milliseconds => TryGetMilliseconds(out var milliseconds)
		? milliseconds
		: throw new InvalidOperationException("timeNano не содержит корректные миллисекунды.");

	/// <summary>Пытается получить миллисекунды из первых 13 разрядов строки timeNano.</summary>
	/// <param name="milliseconds">Миллисекунды серверного времени; ноль при неудаче.</param>
	/// <returns>true, если строка timeNano корректна и достаточно длинна.</returns>
	public bool TryGetMilliseconds(out long milliseconds)
	{
		if (TimeNano.Length >= MillisecondsDigitsCount)
		{
			return long.TryParse(
				TimeNano[..MillisecondsDigitsCount],
				NumberStyles.Integer,
				CultureInfo.InvariantCulture,
				out milliseconds);
		}

		milliseconds = 0;
		return false;
	}
}
