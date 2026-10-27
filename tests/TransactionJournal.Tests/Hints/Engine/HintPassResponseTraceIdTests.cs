namespace TransactionJournal.Tests.Hints.Engine;

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using TransactionJournal.Hints;
using TransactionJournal.Tests.Web.Api;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// Контракт ответа POST /api/hints/pass: trace id прохода приходит как
/// опциональное поле — заполнен у трассированного прохода и null у прохода
/// без трассы. HTTP-контракт проверяется на хосте со стабильным раннером.
/// </summary>
[TestClass]
public sealed class HintPassResponseTraceIdTests
{
	[TestMethod]
	[DataRow("0af7651916cd43dd8448eb211c80319c")]
	[DataRow(null)]
	[Description("Ответ прохода несёт trace id прохода как опциональное поле")]
	// Поле traceId всегда присутствует в JSON-контракте: при трассированном
	// проходе несёт W3C TraceId корневого hint_pass, у прохода без трассы — null.
	// Traceability: openspec:hints/engine-pass#scenario-engine-pass-response-trace-id
	public async Task TryIfHintsPassReturnsTraceId(string? traceId)
	{
		// Arrange: стабильный раннер с заданным trace id прохода.
		var result = StubHintPassRunner.Completed() with { TraceId = traceId };
		await using var factory = new SectionApiFactory(services => services.ReplaceHintPassRunner(new StubHintPassRunner(result)));
		using var client = factory.CreateClient();

		// Act: команда ручного запуска прохода.
		var response = await client.PostAsync("/api/v1/hints/pass", content: null);

		// Assert: поле есть в контракте и равно trace id прохода (null у проходов без трассы).
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		var payload = await response.Content.ReadAsStringAsync();
		using var document = JsonDocument.Parse(payload);
		var traceIdProperty = document.RootElement.GetProperty("traceId");
		if (traceId is null)
		{
			Assert.That(traceIdProperty.ValueKind, Is.EqualTo(JsonValueKind.Null));
		}
		else
		{
			Assert.That(traceIdProperty.GetString(), Is.EqualTo(traceId));
		}
	}
}
