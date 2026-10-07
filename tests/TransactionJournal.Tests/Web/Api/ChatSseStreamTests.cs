namespace TransactionJournal.Tests.Web.Api;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Api.Sse;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Проверки SSE-примитивов доставки ответов ИИ-помощника через реальный HTTP:
/// мини-хост TestServer использует тот же транспортный слой, что и будущий
/// эндпоинт чата, — без домена чата (change add-agent-chat).
/// </summary>
[TestClass]
public sealed class ChatSseStreamTests
{
	[TestMethod]
	[Description("Токены ответа чата приходят потоком событий SSE по одному соединению")]
	// Ответ ИИ-помощника доставляется потоковыми событиями SSE: соединение
	// открывается событием начала, токены идут потоком по тому же каналу,
	// завершение — терминальное событие протокола, без polling.
	// Traceability: openspec:http-api/transport#scenario-chat-tokens-stream-over-sse
	public async Task TryIfTokensStreamOverSseConnection()
	{
		// Arrange — Act: стрим из двух токенов с явным завершением.
		var (contentType, body) = await StreamChatAsync(TokenStream);

		// Assert: соединение открыто как SSE-канал.
		Assert.That(contentType, Is.EqualTo("text/event-stream"));
		// Assert: начало, токены и завершение идут кадрами одного канала.
		Assert.That(body, Is.EqualTo(HappyPathBody));
	}

	[TestMethod]
	[Description("Сбой генерации доставляется событием ошибки по тому же SSE-соединению")]
	// Ошибка провайдера не рвёт канал молча: SPA получает терминальное
	// событие ошибки по тому же открытому соединении и показывает
	// деградацию без потери истории чата.
	// Traceability: openspec:http-api/transport#scenario-chat-error-delivered-as-sse-event
	public async Task TryIfGeneratorFailureDeliveredAsErrorEvent()
	{
		// Arrange — Act: генератор падает после первого токена.
		var (_, body) = await StreamChatAsync(FailingStream);

		// Assert: начало и токен пришли, сбой — терминальное событие
		// ошибки с сообщением, события завершения нет.
		Assert.That(body, Is.EqualTo(
			"event: start\n" +
			"data: {}\n" +
			"\n" +
			"event: token\n" +
			"data: {\"text\":\"При\"}\n" +
			"\n" +
			"event: error\n" +
			"data: {\"message\":\"Провайдер модели недоступен\"}\n" +
			"\n"));
	}

	[TestMethod]
	[Description("Тихое завершение генератора закрывает канал событием завершения")]
	// Транспорт гарантирует терминальный кадр: даже если генератор закончился
	// без явного события, канал закрывается событием завершения — SPA не
	// остаётся ждать в подвешенном соединении.
	// Traceability: openspec:http-api/transport#scenario-chat-tokens-stream-over-sse
	public async Task TryIfSilentGeneratorEndClosesStreamWithDone()
	{
		// Arrange — Act: генератор отдаёт токен и завершается без события Done.
		var (_, body) = await StreamChatAsync(SilentEndStream);

		// Assert: транспорт дополнил стрим терминальным событием завершения.
		Assert.That(body, Is.EqualTo(
			"event: start\n" +
			"data: {}\n" +
			"\n" +
			"event: token\n" +
			"data: {\"text\":\"При\"}\n" +
			"\n" +
			"event: done\n" +
			"data: {}\n" +
			"\n"));
	}

	/// <summary>Кадры эталонного стрима: начало, два токена, завершение.</summary>
	private const string HappyPathBody =
		"event: start\n" +
		"data: {}\n" +
		"\n" +
		"event: token\n" +
		"data: {\"text\":\"При\"}\n" +
		"\n" +
		"event: token\n" +
		"data: {\"text\":\"вет\"}\n" +
		"\n" +
		"event: done\n" +
		"data: {}\n" +
		"\n";

	/// <summary>Генератор чата: два токена и явное завершение.</summary>
	private static async IAsyncEnumerable<ChatStreamEvent> TokenStream()
	{
		yield return ChatStreamEvents.Token("При");
		yield return ChatStreamEvents.Token("вет");
		yield return ChatStreamEvents.Done();
		await Task.CompletedTask;
	}

	/// <summary>Генератор чата, падающий после первого токена.</summary>
	private static async IAsyncEnumerable<ChatStreamEvent> FailingStream()
	{
		yield return ChatStreamEvents.Token("При");
		await Task.CompletedTask;
		throw new InvalidOperationException("Провайдер модели недоступен");
	}

	/// <summary>Генератор чата, завершающийся без явного события Done.</summary>
	private static async IAsyncEnumerable<ChatStreamEvent> SilentEndStream()
	{
		yield return ChatStreamEvents.Token("При");
		await Task.CompletedTask;
	}

	/// <summary>
	/// Поднимает мини-хост с эндпоинтом, стримящим события чата через
	/// транспортный слой SSE, и возвращает тип содержимого и тело ответа.
	/// </summary>
	private static async Task<(string ContentType, string Body)> StreamChatAsync(
		Func<IAsyncEnumerable<ChatStreamEvent>> generator)
	{
		using var host = new HostBuilder()
			.ConfigureWebHost(web => web
				.UseTestServer()
				.Configure(app => app.Run(async context =>
				{
					context.Response.StatusCode = StatusCodes.Status200OK;
					await ChatSseStream.WriteStreamAsync(context.Response, generator(), context.RequestAborted);
				})))
			.Build();
		await host.StartAsync();
		try
		{
			using var client = host.GetTestClient();
			using var response = await client.GetAsync(
				"http://localhost/chat", HttpCompletionOption.ResponseHeadersRead);
			return (response.Content.Headers.ContentType!.MediaType!, await response.Content.ReadAsStringAsync());
		}
		finally
		{
			await host.StopAsync();
		}
	}
}
