using System.Net;
using System.Text;

namespace TransactionJournal.Tests.Bybit;

/// <summary>
/// Фиктивный транспорт для интеграционных проверок клиента против зафиксированных
/// HTTP-ответов: запоминает все отправленные запросы и отвечает заготовленными
/// телами по порядку; при исчерпании сценария отвечает пустым успешным ответом.
/// Поддерживает произвольные заголовки ответов, транспортные сбои и фиксацию
/// моментов отправки по часам подставленного поставщика времени.
/// </summary>
internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
	private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
	private readonly TimeProvider? _timeProvider;

	/// <summary>Все запросы, прошедшие через транспорт, в порядке отправки.</summary>
	public List<HttpRequestMessage> Requests { get; } = [];

	/// <summary>Моменты отправки запросов по часам фиктивного поставщика времени, epoch мс.</summary>
	public List<long> RequestTimestampsMs { get; } = [];

	public ScriptedHttpMessageHandler(TimeProvider? timeProvider = null)
	{
		_timeProvider = timeProvider;
	}

	/// <summary>Добавляет ответ с JSON-телом, статусом и произвольными заголовками в конец сценария.</summary>
	public void EnqueueJson(
		string json,
		HttpStatusCode statusCode = HttpStatusCode.OK,
		IReadOnlyDictionary<string, string>? headers = null)
	{
		_responses.Enqueue(_ =>
		{
			var response = new HttpResponseMessage(statusCode)
			{
				Content = new StringContent(json, Encoding.UTF8, "application/json"),
			};
			if (headers != null)
			{
				foreach (var (name, value) in headers)
				{
					response.Headers.TryAddWithoutValidation(name, value);
				}
			}

			return response;
		});
	}

	/// <summary>Добавляет транспортный сбой: отправка запроса завершается исключением.</summary>
	public void EnqueueException(Exception exception)
	{
		_responses.Enqueue(_ => throw exception);
	}

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		Requests.Add(request);
		if (_timeProvider != null)
		{
			RequestTimestampsMs.Add(_timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
		}

		var respond = _responses.Count > 0 ? _responses.Dequeue() : _ => new HttpResponseMessage(HttpStatusCode.OK);
		return Task.FromResult(respond(request));
	}
}
