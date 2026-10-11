namespace TransactionJournal.Tests.Web.Api;

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Api.Agent;
using TransactionJournal.Api.Sse;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Юнит-проверки guard'а отказов генерации чата: сырые исключения
/// провайдера, сети и парсера доставляются владельцу человекочитаемым
/// error-кадром SSE-канала (категория отказа и что сделать), а запись
/// об отказе с chatId и типом причины уходит в лог. Отмена генерации
/// владельцем отказом не считается.
/// </summary>
[TestClass]
public sealed class ChatStreamFailureGuardTests
{
	// Фраза заглушки незастроенного ключа: классифицируется guard'ом как
	// «ключ не настроен» и возвращается владельцу без изменений.
	private const string UnconfiguredApiKeyMessage =
		"API-ключ модели не настроен: задайте Llm:ApiKey в appsettings или переменной окружения.";

	[TestMethod]
	[Description("Сетевой отказ провайдера доставляется человекочитаемым error-кадром и логируется с chatId и причиной")]
	// Недоступный BaseUrl приводит к HttpRequestException внутри конвейера:
	// владелец видит категорию отказа и что проверить, а не стек исключения.
	// Traceability: openspec:http-api/transport#scenario-chat-error-cause-visible-in-ui
	// Traceability: openspec:http-api/transport#scenario-chat-error-delivered-as-sse-event
	public async Task TryIfHttpRequestExceptionDescribesUnreachableProvider()
	{
		// Arrange: поток с токеном и сетевым отказом провайдера.
		var logger = new RecordingLogger();
		var frames = ChatStreamFailureGuard.GuardAsync(
			FailWithTokenFrame(new HttpRequestException("Connection refused (127.0.0.1:59998)")),
			logger,
			42);

		// Act: вычитываем канал до отказа.
		var received = await CollectAsync(frames);

		// Assert: токен дошёл, error-кадр человекочитаем, отказ в логе.
		Assert.That(received, Has.Count.EqualTo(2));
		Assert.That(received[0].Name, Is.EqualTo(ChatStreamEvents.TokenName));
		Assert.That(received[1].Name, Is.EqualTo(ChatStreamEvents.ErrorName));
		Assert.That(AssertPayload<ChatErrorPayload>(received[1]).Message, Does.Contain("Провайдер модели недоступен"));
		Assert.That(AssertPayload<ChatErrorPayload>(received[1]).Message, Does.Contain("Llm:BaseUrl"));
		var entry = AssertSingleErrorEntry(logger);
		Assert.That(entry.Exception, Is.InstanceOf<HttpRequestException>());
		Assert.That(entry.Message, Does.Contain("42"));
		Assert.That(entry.Message, Does.Contain("provider-unreachable"));
	}

	[TestMethod]
	[Description("Накопитель повторов System.ClientModel распознаётся как недоступность провайдера")]
	// Исчерпание повторов приходит агрегатом «Retry failed after N tries»:
	// первопричина-сеть вытаскивается из внутренних исключений агрегата.
	// Traceability: openspec:http-api/transport#scenario-chat-error-cause-visible-in-ui
	public async Task TryIfRetryAggregateExceptionDescribesUnreachableProvider()
	{
		// Arrange: агрегат повторов поверх сетевых исключений попыток.
		var logger = new RecordingLogger();
		var aggregate = new AggregateException(
			"Retry failed after 4 tries.",
			new HttpRequestException("No connection could be made because the target machine actively refused it."),
			new HttpRequestException("No connection could be made because the target machine actively refused it."));
		var frames = ChatStreamFailureGuard.GuardAsync(ThrowingFrames(aggregate), logger, 7);

		// Act: вычитываем канал до отказа.
		var received = await CollectAsync(frames);

		// Assert: текст ведёт к адресу провайдера, категория в логе стабильна.
		Assert.That(received, Has.Count.EqualTo(1));
		Assert.That(received[0].Name, Is.EqualTo(ChatStreamEvents.ErrorName));
		Assert.That(AssertPayload<ChatErrorPayload>(received[0]).Message, Does.Contain("Провайдер модели недоступен"));
		var entry = AssertSingleErrorEntry(logger);
		Assert.That(entry.Exception, Is.SameAs(aggregate));
		Assert.That(entry.Message, Does.Contain("provider-unreachable"));
	}

	[TestMethod]
	[DataRow("Error code: 404 - {\"error\": {\"code\": \"model_not_found\", \"message\": \"The model `glm-9.9` does not exist\"}}")]
	[DataRow("The model `glm-9.9` does not exist")]
	[Description("Отказ model_not_found показывает владельцу имя несуществующей модели")]
	// Несуществующая модель — ошибка выбора владельца: текст называет
	// модель и что проверить, а не пересказывает ответ провайдера.
	// Traceability: openspec:http-api/transport#scenario-chat-error-cause-visible-in-ui
	public async Task TryIfMissingModelDescribesModelName(string providerMessage)
	{
		// Arrange: отказ провайдера о несуществующей модели.
		var logger = new RecordingLogger();
		var frames = ChatStreamFailureGuard.GuardAsync(
			ThrowingFrames(new ClientResultException(providerMessage)),
			logger,
			11);

		// Act: вычитываем канал до отказа.
		var received = await CollectAsync(frames);

		// Assert: имя модели из ответа провайдера в человеческом тексте.
		Assert.That(received[0].Name, Is.EqualTo(ChatStreamEvents.ErrorName));
		Assert.That(AssertPayload<ChatErrorPayload>(received[0]).Message, Is.EqualTo("Модель \"glm-9.9\" не найдена у провайдера: проверьте имя модели в параметрах чата."));
		Assert.That(AssertSingleErrorEntry(logger).Message, Does.Contain("model-not-found"));
	}

	[TestMethod]
	[Description("Отказ провайдера без известной причины называет HTTP-статус ответа")]
	// Прочий HTTP-отказ провайдера (лимиты, квота, 500): владелец видит
	// статус и что делать, соединение и модель категорийно отделены.
	// Traceability: openspec:http-api/transport#scenario-chat-error-cause-visible-in-ui
	public async Task TryIfProviderRejectionDescribesHttpStatus()
	{
		// Arrange: отказ провайдера с HTTP-статусом 503.
		var logger = new RecordingLogger();
		var response = new FakePipelineResponse(503);
		var frames = ChatStreamFailureGuard.GuardAsync(
			ThrowingFrames(new ClientResultException("Service temporarily unavailable.", response)),
			logger,
			13);

		// Act: вычитываем канал до отказа.
		var received = await CollectAsync(frames);

		// Assert: статус присутствует в человекочитаемом тексте.
		Assert.That(received[0].Name, Is.EqualTo(ChatStreamEvents.ErrorName));
		Assert.That(AssertPayload<ChatErrorPayload>(received[0]).Message, Is.EqualTo("Провайдер отклонил запрос модели (HTTP 503): проверьте настройки провайдера и повторите запрос."));
		Assert.That(AssertSingleErrorEntry(logger).Message, Does.Contain("provider-rejected"));
	}

	[TestMethod]
	[Description("Битый поток ответа провайдера доставляется советом повторить запрос, а не стеком парсера")]
	// Исключение генерации посреди стрима (JsonException парсера) —
	// владельцу не нужен разбор формата, нужен путь действия.
	// Traceability: openspec:http-api/transport#scenario-chat-error-cause-visible-in-ui
	public async Task TryIfJsonExceptionDescribesInvalidProviderResponse()
	{
		// Arrange: поток с токеном и отказом парсера ответа провайдера.
		var logger = new RecordingLogger();
		var frames = ChatStreamFailureGuard.GuardAsync(
			FailWithTokenFrame(new System.Text.Json.JsonException("'{' is an invalid start of a property name. Path: $")),
			logger,
			21);

		// Act: вычитываем канал до отказа.
		var received = await CollectAsync(frames);

		// Assert: токен дошёл, текст говорит о формате ответа провайдера.
		Assert.That(received, Has.Count.EqualTo(2));
		Assert.That(received[1].Name, Is.EqualTo(ChatStreamEvents.ErrorName));
		Assert.That(AssertPayload<ChatErrorPayload>(received[1]).Message, Is.EqualTo("Провайдер вернул ответ в неожиданном формате: повторите запрос или смените модель."));
		Assert.That(AssertSingleErrorEntry(logger).Message, Does.Contain("invalid-provider-response"));
	}

	[TestMethod]
	[Description("Незастроенный ключ модели возвращается владельцу фразой заглушки без искажений")]
	// Текст заглушки UnconfiguredChatClient уже человекочитаем: guard
	// сохраняет его и ставит стабильную категорию в лог.
	// Traceability: openspec:http-api/transport#scenario-chat-error-cause-visible-in-ui
	public async Task TryIfUnconfiguredApiKeyMessageIsPreserved()
	{
		// Arrange: конвейер бросает фразу заглушки незастроенного ключа.
		var logger = new RecordingLogger();
		var frames = ChatStreamFailureGuard.GuardAsync(
			ThrowingFrames(new InvalidOperationException(UnconfiguredApiKeyMessage)),
			logger,
			33);

		// Act: вычитываем канал до отказа.
		var received = await CollectAsync(frames);

		// Assert: фраза заглушки не переписана, категория в логе.
		Assert.That(received, Has.Count.EqualTo(1));
		Assert.That(received[0].Name, Is.EqualTo(ChatStreamEvents.ErrorName));
		Assert.That(AssertPayload<ChatErrorPayload>(received[0]).Message, Is.EqualTo(UnconfiguredApiKeyMessage));
		Assert.That(AssertSingleErrorEntry(logger).Message, Does.Contain("unconfigured-api-key"));
	}

	[TestMethod]
	[Description("Неопознанное исключение доставляется общей человекочитаемой формулировкой без сырого текста")]
	// Страховка от незнакомых причин: владелец видит нейтральный совет,
	// сырой стек уходит только в лог.
	// Traceability: openspec:http-api/transport#scenario-chat-error-cause-visible-in-ui
	public async Task TryIfUnknownFailureDescribesFallback()
	{
		// Arrange: незнакомая причина отказа.
		var logger = new RecordingLogger();
		var frames = ChatStreamFailureGuard.GuardAsync(
			ThrowingFrames(new InvalidOperationException("Неожиданный внутренний сбой конвейера")),
			logger,
			55);

		// Act: вычитываем канал до отказа.
		var received = await CollectAsync(frames);

		// Assert: сырой текст исключения не попал в канал.
		Assert.That(received[0].Name, Is.EqualTo(ChatStreamEvents.ErrorName));
		var message = AssertPayload<ChatErrorPayload>(received[0]).Message;
		Assert.That(message, Does.Contain("Генерация ответа не удалась"));
		Assert.That(message, Does.Not.Contain("Неожиданный внутренний сбой"));
		var entry = AssertSingleErrorEntry(logger);
		Assert.That(entry.Message, Does.Contain("generation-failed"));
		Assert.That(entry.Exception!.Message, Does.Contain("Неожиданный внутренний сбой"));
	}

	[TestMethod]
	[Description("Отмена генерации владельцем не завершает канал error-кадром и не пишет отказ в лог")]
	// OperationCanceledException при отмене токена — не отказ генерации:
	// исключение проходит наружу транспорту без error-кадра и записи в лог.
	// Traceability: openspec:http-api/transport#scenario-chat-error-delivered-as-sse-event
	public void ThrowOnOwnerCancellationPassesThroughWithoutErrorFrame()
	{
		// Arrange: заранее отменённый токен владельца.
		using var source = new CancellationTokenSource();
		source.Cancel();
		var logger = new RecordingLogger();

		// Act: вычитывание канала при отменённой генерации.
		var frames = ChatStreamFailureGuard.GuardAsync(CancelledFrames(source.Token), logger, 77, source.Token);

		// Assert: отмена пробрасывается (включая производные типы), error-кадра
		// и записи в лог нет.
		Assert.That(
			Assert.CatchAsync<OperationCanceledException>(async () => await CollectAsync(frames)),
			Is.InstanceOf<OperationCanceledException>());
		Assert.That(logger.Entries, Is.Empty);
	}

	/// <summary>Поток, отдающий один токен-кадр и падающий заданным отказом.</summary>
	private static async IAsyncEnumerable<ChatStreamEvent> FailWithTokenFrame(
		Exception failure,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await Task.Yield();
		yield return ChatStreamEvents.Token("Привет");
		throw failure;
	}

	/// <summary>Поток, который сразу падает заданным отказом.</summary>
	private static async IAsyncEnumerable<ChatStreamEvent> ThrowingFrames(
		Exception failure,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		await Task.Yield();
		if (failure is not null)
			throw failure;
		yield break;
	}

	/// <summary>Поток, ожидающий отмены токена владельца.</summary>
	private static async IAsyncEnumerable<ChatStreamEvent> CancelledFrames(
		CancellationToken ownerToken,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		var linked = CancellationTokenSource.CreateLinkedTokenSource(ownerToken, cancellationToken);
		await Task.Delay(System.Threading.Timeout.InfiniteTimeSpan, linked.Token);
		yield return ChatStreamEvents.Token("недостижимо");
	}

	/// <summary>Проверяет тип нагрузки кадра и возвращает её.</summary>
	private static TPayload AssertPayload<TPayload>(ChatStreamEvent frame)
		where TPayload : class
	{
		Assert.That(frame.Data, Is.InstanceOf<TPayload>());
		return (TPayload)frame.Data!;
	}

	/// <summary>Вычитывает весь поток кадров в список.</summary>
	private static async Task<List<ChatStreamEvent>> CollectAsync(
		IAsyncEnumerable<ChatStreamEvent> frames)
	{
		var received = new List<ChatStreamEvent>();
		await foreach (var frame in frames)
			received.Add(frame);
		return received;
	}

	/// <summary>Проверяет единственную запись отказа в логе и возвращает её.</summary>
	private static (LogLevel Level, Exception? Exception, string Message) AssertSingleErrorEntry(RecordingLogger logger)
	{
		Assert.That(logger.Entries, Has.Count.EqualTo(1));
		var entry = logger.Entries[0];
		Assert.That(entry.Level, Is.EqualTo(LogLevel.Error));
		Assert.That(entry.Exception, Is.Not.Null);
		return entry;
	}

	/// <summary>Фиктивный ответ провайдера для ClientResultException с HTTP-статусом.</summary>
	private sealed class FakePipelineResponse : PipelineResponse
	{
		public FakePipelineResponse(int status)
		{
			Status = status;
			Content = BinaryData.FromString("Service temporarily unavailable.");
		}

		public override int Status { get; }

		public override string ReasonPhrase => "Service Unavailable";

		protected override PipelineResponseHeaders HeadersCore { get; }

		public override Stream? ContentStream { get; set; }

		public override bool IsError => true;

		public override BinaryData Content { get; }

		public override BinaryData BufferContent(CancellationToken cancellationToken) => Content;

		public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken) =>
			ValueTask.FromResult(Content);

		public override void Dispose()
		{
		}
	}

	/// <summary>Лог-двойка, запоминающая записи отказа вместо записи в консоль.</summary>
	private sealed class RecordingLogger : ILogger
	{
		public List<(LogLevel Level, Exception? Exception, string Message)> Entries { get; } = new();

		public IDisposable? BeginScope<TState>(TState state)
			where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(
			LogLevel logLevel,
			EventId eventId,
			TState state,
			Exception? exception,
			Func<TState, Exception?, string> formatter) =>
			Entries.Add((logLevel, exception, formatter(state, exception)));
	}
}