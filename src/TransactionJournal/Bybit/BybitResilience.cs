using System.Net.Http;
using Polly;
using Polly.Retry;

namespace TransactionJournal.Bybit;

/// <summary>
/// Устойчивость запросов к Bybit: каждый вызов проходит через троттлинг минимального
/// интервала и заголовков лимитов X-Bapi-Limit-*, а сбои повторяются Polly — сеть и 5xx
/// коротким экспоненциальным бэкоффом, retCode 10006 паузой секундами с нарастанием,
/// HTTP 403 длинной паузой с ограничением попыток.
/// Traceability: change:add-bybit-sync/design#d5
/// Traceability: openspec:sync/bybit-history#requirement-api-limits-and-error-handling
/// Traceability: doc:docs/research/bybit-api.md#8-rate-limits-по-нужным-эндпоинтам
/// </summary>
internal sealed class BybitResilience
{
	/// <summary>Код ошибки биржи retCode 10006 «Too many visits!» — превышение частоты запросов per-UID.</summary>
	internal const int RateLimitRetCode = 10006;

	/// <summary>HTTP-статус 403 — блокировка IP после исчерпания лимита «access too frequent».</summary>
	internal const int AccessBlockedStatusCode = 403;

	private readonly BybitResilienceOptions _options;
	private readonly BybitRequestThrottle _throttle;
	private readonly ResiliencePipeline<string> _pipeline;

	internal BybitResilience(BybitResilienceOptions options, TimeProvider? timeProvider = null)
	{
		_options = options;
		TimeProvider = timeProvider ?? TimeProvider.System;
		_throttle = new BybitRequestThrottle(options.MinRequestInterval, TimeProvider);
		_pipeline = BuildPipeline(options, TimeProvider);
	}

	/// <summary>Поставщик времени конвейера; тесты подменяют его виртуальными часами для детерминированных пауз.</summary>
	internal TimeProvider TimeProvider { get; }

	/// <summary>
	/// Выполняет один запрос к бирже со всей устойчивостью: каждая попытка проходит
	/// троттлинг, создаёт свежий запрос через createRequest (новая подпись с актуальным
	/// timestamp — после длинных пауз старая вышла бы за recv_window), читает тело
	/// и проверяет его через validateBody, чтобы повторы покрывали и ошибки конверта retCode.
	/// </summary>
	internal async Task<string> SendAsync(
		HttpClient httpClient,
		Func<HttpRequestMessage> createRequest,
		Action<string> validateBody,
		CancellationToken cancellationToken)
	{
		try
		{
			return await _pipeline.ExecuteAsync(async token =>
			{
				await _throttle.WaitBeforeRequestAsync(token).ConfigureAwait(false);
				using var request = createRequest();
				using var response = await httpClient.SendAsync(request, token).ConfigureAwait(false);
				var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
				_throttle.OnResponse(response);
				if (response.IsSuccessStatusCode == false)
				{
					throw BybitApiException.FromHttpStatus((int)response.StatusCode, body);
				}

				// Ошибки конверта (включая retCode 10006) проверяются внутри попытки,
				// чтобы Polly повторял и их, а не только транспортные сбои.
				validateBody(body);
				return body;
			}, cancellationToken).ConfigureAwait(false);
		}
		catch (BybitApiException exception) when (exception.HttpStatusCode == AccessBlockedStatusCode)
		{
			// Сюда попадаем, только когда повтор с длинной паузой тоже отвергнут:
			// пользователю нужна внятная ошибка вместо сырого тела ответа.
			throw BybitApiException.FromAccessBlocked(
				AccessBlockedStatusCode, _options.AccessBlockedPause, exception.ResponseBody ?? string.Empty);
		}
	}

	/// <summary>
	/// Собирает конвейер повторов Polly: три независимые стратегии по классам сбоев,
	/// у каждой своя пауза и лимит попыток.
	/// </summary>
	private static ResiliencePipeline<string> BuildPipeline(BybitResilienceOptions options, TimeProvider timeProvider)
	{
		var builder = new ResiliencePipelineBuilder<string>
		{
			TimeProvider = timeProvider,
		};

		// Сеть и HTTP 5xx — короткий экспоненциальный бэкофф: сбой переходящий, повторяем быстро.
		builder.AddRetry(new RetryStrategyOptions<string>
		{
			Name = "BybitNetwork",
			ShouldHandle = new PredicateBuilder<string>()
				.Handle<HttpRequestException>()
				.Handle<TaskCanceledException>(exception => exception.InnerException is TimeoutException)
				.Handle<BybitApiException>(exception => exception.HttpStatusCode >= 500),
			MaxRetryAttempts = options.NetworkRetryCount,
			BackoffType = DelayBackoffType.Exponential,
			Delay = options.NetworkRetryBaseDelay,
		});

		// retCode 10006 — пауза секундами с нарастанием: окно per-UID односекундное,
		// каждому повтору должно достаться свежее окно.
		builder.AddRetry(new RetryStrategyOptions<string>
		{
			Name = "BybitRateLimit",
			ShouldHandle = new PredicateBuilder<string>()
				.Handle<BybitApiException>(exception => exception.RetCode == RateLimitRetCode),
			MaxRetryAttempts = options.RateLimitRetryCount,
			BackoffType = DelayBackoffType.Exponential,
			Delay = options.RateLimitRetryDelay,
		});

		// HTTP 403 — длинная пауза длительности IP-блокировки, число попыток ограничено.
		builder.AddRetry(new RetryStrategyOptions<string>
		{
			Name = "BybitAccessBlocked",
			ShouldHandle = new PredicateBuilder<string>()
				.Handle<BybitApiException>(exception => exception.HttpStatusCode == AccessBlockedStatusCode),
			MaxRetryAttempts = options.AccessBlockedRetryCount,
			BackoffType = DelayBackoffType.Constant,
			Delay = options.AccessBlockedPause,
		});

		return builder.Build();
	}
}
