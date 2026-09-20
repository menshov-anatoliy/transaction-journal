using System.Net;
using System.Text;

namespace TransactionJournal.Tests.Bybit;

/// <summary>
/// Фиктивный транспорт для интеграционных проверок клиента против зафиксированных
/// HTTP-ответов: запоминает все отправленные запросы и отвечает заготовленными
/// телами по порядку; при исчерпании сценария отвечает пустым успешным ответом.
/// </summary>
internal sealed class ScriptedHttpMessageHandler : HttpMessageHandler
{
	private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();

	/// <summary>Все запросы, прошедшие через транспорт, в порядке отправки.</summary>
	public List<HttpRequestMessage> Requests { get; } = [];

	/// <summary>Добавляет ответ с JSON-телом и статусом в конец сценария.</summary>
	public void EnqueueJson(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
	{
		_responses.Enqueue(_ => new HttpResponseMessage(statusCode)
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json"),
		});
	}

	protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		Requests.Add(request);
		var respond = _responses.Count > 0 ? _responses.Dequeue() : _ => new HttpResponseMessage(HttpStatusCode.OK);
		return Task.FromResult(respond(request));
	}
}
