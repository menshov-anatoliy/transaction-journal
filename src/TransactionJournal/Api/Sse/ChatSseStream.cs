namespace TransactionJournal.Api.Sse;

using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

/// <summary>
/// Доставка потока событий чата по SSE: устанавливает соединение, открывает
/// его событием начала, кадрирует события чата в протокол SSE и flush-ит
/// каждый кадр, чтобы токены уходили клиенту по мере генерации. Протокол
/// начала/токена/завершения/ошибки фиксирован транспортом — будущий эндпоинт
/// чата (change add-agent-chat) только порождает события.
/// </summary>
public static class ChatSseStream
{
	// Нагрузка кадров отдаётся без экранирования не-ASCII — как в JSON-ответах
	// ASP.NET Core: токены чата читаемы в потоке и компактны.
	private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerDefaults.Web)
	{
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	/// <summary>
	/// Записывает поток событий чата в HTTP-ответ как SSE-канал.
	/// </summary>
	/// <param name="response">Ответ открытого соединения.</param>
	/// <param name="events">Поток событий генератора чата.</param>
	/// <param name="cancellationToken">Токен отмены запроса.</param>
	public static async Task WriteStreamAsync(
		HttpResponse response,
		IAsyncEnumerable<ChatStreamEvent> events,
		CancellationToken cancellationToken)
	{
		// Установка соединения SSE: тип содержимого, запрет кэширования и
		// выключение буферизации ответа, чтобы кадры уходили немедленно.
		response.ContentType = "text/event-stream; charset=utf-8";
		response.Headers.CacheControl = "no-cache";
		response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

		// Начало ответа — событие начала: SPA получает подтверждение
		// открытого канала до первого токена.
		// Traceability: openspec:http-api/transport#scenario-chat-tokens-stream-over-sse
		await WriteFrameAsync(response, ChatStreamEvents.Start(), cancellationToken);

		// Канал закрывается ровно одним терминальным событием: завершением
		// или ошибкой, без дублирования.
		var terminated = false;
		try
		{
			await foreach (var chatEvent in events.WithCancellation(cancellationToken))
			{
				// Начало принадлежит транспорту: повторные события начала
				// генератора в канал не проходят.
				if (chatEvent.Name == ChatStreamEvents.StartName)
					continue;
				if (chatEvent.Name == ChatStreamEvents.DoneName || chatEvent.Name == ChatStreamEvents.ErrorName)
					terminated = true;
				await WriteFrameAsync(response, chatEvent, cancellationToken);
			}

			// Тихое завершение генератора не оставляет канал открытым:
			// транспорт сам ставит терминальное событие завершения.
			if (terminated == false)
				await WriteFrameAsync(response, ChatStreamEvents.Done(), cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Клиент ушёл — доставлять события некому, канал закрывается тихо.
			throw;
		}
		catch (Exception ex)
		{
			// Сбой генерации становится терминальным событием ошибки того же
			// соединения: SPA показывает деградацию, история чата сохраняется.
			// Терминал уже отправлен — второй не пишется.
			// Отмена уже не важна — терминальный кадр пишется до конца.
			// Traceability: openspec:http-api/transport#scenario-chat-error-delivered-as-sse-event
			if (terminated == false)
				await WriteFrameAsync(response, ChatStreamEvents.Error(ex.Message), CancellationToken.None);
		}
	}

	/// <summary>
	/// Кадрирует одно событие чата в формат SSE и сбрасывает буфер клиенту.
	/// </summary>
	private static async Task WriteFrameAsync(
		HttpResponse response,
		ChatStreamEvent chatEvent,
		CancellationToken cancellationToken)
	{
		// Нагрузка без данных публикуется пустым JSON-объектом, чтобы клиент
		// мог единообразно разбирать data каждого кадра.
		var payload = chatEvent.Data is null ? "{}" : JsonSerializer.Serialize(chatEvent.Data, PayloadOptions);
		// Кадр SSE: имя события, однострочный JSON в data, пустая строка-разделитель.
		var frame = $"event: {chatEvent.Name}\ndata: {payload}\n\n";
		await response.Body.WriteAsync(Encoding.UTF8.GetBytes(frame), cancellationToken);
		await response.Body.FlushAsync(cancellationToken);
	}
}
