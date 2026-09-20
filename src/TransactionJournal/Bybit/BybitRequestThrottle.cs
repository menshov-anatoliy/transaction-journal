using System.Globalization;
using System.Net.Http;

namespace TransactionJournal.Bybit;

/// <summary>
/// Троттлинг запросов к Bybit: фиксированный минимальный интервал между отправками
/// и учёт заголовков лимитов X-Bapi-Limit-* — при исчерпании остатка следующая
/// отправка задерживается до отметки сброса окна.
/// Traceability: change:add-bybit-sync/design#d5
/// Traceability: openspec:sync/bybit-history#requirement-api-limits-and-error-handling
/// Traceability: doc:docs/research/bybit-api.md#8-rate-limits-по-нужным-эндпоинтам
/// </summary>
internal sealed class BybitRequestThrottle
{
	internal const string LimitStatusHeaderName = "X-Bapi-Limit-Status";
	internal const string LimitResetTimestampHeaderName = "X-Bapi-Limit-Reset-Timestamp";

	private readonly TimeSpan _minInterval;
	private readonly TimeProvider _timeProvider;
	private readonly object _sync = new();
	private long _nextPermittedAtMs;
	private long _limitResetsAtMs;

	internal BybitRequestThrottle(TimeSpan minInterval, TimeProvider timeProvider)
	{
		_minInterval = minInterval;
		_timeProvider = timeProvider;
	}

	/// <summary>
	/// Задерживает вызывающего до момента, когда биржа готова принять следующий запрос:
	/// не раньше минимального интервала от предыдущей отправки и не раньше отметки
	/// сброса окна лимитов, если предыдущий ответ сообщил об исчерпании.
	/// </summary>
	internal async Task WaitBeforeRequestAsync(CancellationToken cancellationToken)
	{
		TimeSpan delay;
		lock (_sync)
		{
			var nowMs = _timeProvider.GetUtcNow().ToUnixTimeMilliseconds();
			// Слот следующей отправки — самый поздний из двух ограничений: интервал и окно лимитов.
			var earliestMs = Math.Max(_nextPermittedAtMs, _limitResetsAtMs);
			if (earliestMs < nowMs)
			{
				earliestMs = nowMs;
			}

			_nextPermittedAtMs = earliestMs + (long)_minInterval.TotalMilliseconds;
			delay = TimeSpan.FromMilliseconds(earliestMs - nowMs);
		}

		if (delay > TimeSpan.Zero)
		{
			await _timeProvider.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
		}
	}

	/// <summary>
	/// Запоминает состояние окна лимитов из заголовков ответа: при исчерпанном остатке
	/// X-Bapi-Limit-Status следующая отправка задержится до X-Bapi-Limit-Reset-Timestamp.
	/// Отметка сброса выражена серверными часами; расхождение с локальными часами
	/// пренебрежимо мало по сравнению с длиной окна.
	/// </summary>
	internal void OnResponse(HttpResponseMessage response)
	{
		if (response.Headers.TryGetValues(LimitStatusHeaderName, out var statusValues) == false
			|| int.TryParse(statusValues.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var remaining) == false
			|| remaining > 0)
		{
			// Заголовков нет или остаток положителен — пауза по лимитам не требуется.
			return;
		}

		if (response.Headers.TryGetValues(LimitResetTimestampHeaderName, out var resetValues) == false
			|| long.TryParse(resetValues.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var resetsAtMs) == false
			|| resetsAtMs <= 0)
		{
			return;
		}

		lock (_sync)
		{
			if (resetsAtMs > _limitResetsAtMs)
			{
				_limitResetsAtMs = resetsAtMs;
			}
		}
	}
}
