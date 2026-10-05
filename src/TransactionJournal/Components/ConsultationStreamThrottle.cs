namespace TransactionJournal.Components;

/// <summary>
/// Троттлинг перерисовок стримящегося ответа консультации: чанки модели идут
/// часто, а перерисовка панели разрешается не чаще интервала (~150 мс) —
/// SignalR-канал не забивается StateHasChanged на каждый чанк. Статусные
/// строки tool-вызовов рендерятся без троттлинга — они редки, а их задержка
/// читалась бы как зависание панели.
/// Traceability: openspec:ui/screens#scenario-ui-streaming-with-tool-status
/// </summary>
public sealed class ConsultationStreamThrottle
{
	/// <summary>Интервал между разрешёнными перерисовками.</summary>
	private readonly TimeSpan _interval;

	/// <summary>Поставщик времени; в проверках подменяется управляемыми часами.</summary>
	private readonly TimeProvider _timeProvider;

	/// <summary>Момент последней разрешённой перерисовки; null — перерисовок ещё не было.</summary>
	private DateTimeOffset? _lastRender;

	/// <summary>Создаёт троттлер с заданным минимальным интервалом перерисовок.</summary>
	/// <param name="interval">Минимальный интервал между перерисовками.</param>
	/// <param name="timeProvider">Поставщик времени; по умолчанию системные часы.</param>
	public ConsultationStreamThrottle(TimeSpan interval, TimeProvider? timeProvider = null)
	{
		if (interval <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(nameof(interval), interval, "Интервал троттлинга должен быть положительным.");
		}

		_interval = interval;
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	/// <summary>
	/// Говорит, пора ли рендерить накопленное: с прошлой перерисовки истёк
	/// интервал либо перерисовок ещё не было; разрешение сразу фиксируется.
	/// Traceability: openspec:ui/screens#scenario-ui-streaming-with-tool-status
	/// </summary>
	/// <returns>true — перерисовка разрешена и учтена, false — рано, ждём интервала.</returns>
	public bool ShouldRender()
	{
		var now = _timeProvider.GetUtcNow();
		if (_lastRender is { } last && now - last < _interval)
		{
			return false;
		}

		_lastRender = now;
		return true;
	}
}
