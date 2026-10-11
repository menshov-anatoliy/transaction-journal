namespace TransactionJournal.Api.Agent;

using System.ClientModel;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TransactionJournal.Api.Sse;

/// <summary>
/// Отказ генерации превращается в терминальный кадр ошибки SSE-канала:
/// сырые исключения SDK, сети и парсера описываются человекочитаемо
/// (категория отказа и что сделать владельцу), а запись об отказе с
/// chatId и типом причины уходит в лог приложения. Отмена генерации
/// владельцем отказом не считается — она проходит наружу без error-кадра.
/// </summary>
// Traceability: openspec:http-api/transport#scenario-chat-error-cause-visible-in-ui
// Traceability: openspec:http-api/transport#scenario-chat-error-delivered-as-sse-event
public static class ChatStreamFailureGuard
{
	/// <summary>
	/// Оборачивает поток кадров чата: перехватывает отказ генерации,
	/// логирует его с chatId и типом причины и завершает канал кадром
	/// ошибки с человекочитаемым текстом вместо сырого исключения.
	/// </summary>
	/// <param name="source">Исходный поток кадров генератора чата.</param>
	/// <param name="logger">Лог приложения для записи отказа.</param>
	/// <param name="chatId">Идентификатор чата для записи в лог.</param>
	/// <param name="cancellationToken">Токен отмены генерации.</param>
	/// <returns>Поток кадров, завершённый событием ошибки при отказе.</returns>
	public static async IAsyncEnumerable<ChatStreamEvent> GuardAsync(
		IAsyncEnumerable<ChatStreamEvent> source,
		ILogger logger,
		long chatId,
		[EnumeratorCancellation] CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(logger);

		var frames = source.GetAsyncEnumerator(cancellationToken);
		try
		{
			while (true)
			{
				var step = await NextFrameAsync(frames, cancellationToken).ConfigureAwait(false);
				if (step.Failure is not null)
				{
					// Причина отказа уходит в лог с chatId и стабильным типом:
					// диагностика находит запись без разбора стека исключения.
					// Traceability: openspec:http-api/transport#scenario-chat-error-cause-visible-in-ui
					var description = ChatFailureDescriber.For(step.Failure);
					logger.LogError(step.Failure, "Отказ генерации ответа в чате {ChatId}, причина {CauseType}", chatId, description.CauseType);
					yield return ChatStreamEvents.Error(description.Message);
					yield break;
				}

				// Конец потока без отказа: терминальный кадр ставит транспорт.
				if (step.Frame is null)
					break;
				yield return step.Frame;
			}
		}
		finally
		{
			await frames.DisposeAsync().ConfigureAwait(false);
		}
	}

	/// <summary>Вычитывает очередной кадр, перехватывая отказ генерации.</summary>
	/// <param name="frames">Перечислитель потока кадров чата.</param>
	/// <param name="cancellationToken">Токен отмены генерации.</param>
	/// <returns>Очередной кадр либо перехваченное исключение отказа.</returns>
	private static async Task<(ChatStreamEvent? Frame, Exception? Failure)> NextFrameAsync(
		IAsyncEnumerator<ChatStreamEvent> frames,
		CancellationToken cancellationToken)
	{
		try
		{
			if (await frames.MoveNextAsync().ConfigureAwait(false) == false)
				return (null, null);
			return (frames.Current, null);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			// Владелец ушёл или отменил генерацию: отказом это не считается,
			// исключение уходит транспорту без error-кадра и записи в лог.
			throw;
		}
		catch (Exception failure)
		{
			return (null, failure);
		}
	}
}

/// <summary>
/// Описание причины отказа генерации: стабильный тип для поиска по логам
/// и человекочитаемый текст для владельца.
/// </summary>
/// <param name="CauseType">Стабильный тип причины отказа.</param>
/// <param name="Message">Человекочитаемый текст: категория отказа и что сделать.</param>
public sealed record ChatFailureDescription(string CauseType, string Message);

/// <summary>
/// Классификатор причин отказа генерации: сырые исключения провайдера,
/// сети и парсера превращаются в пары «категория отказа + совет
/// владельцу» — error-кадр читаем без расшифровки стеков, категория
/// живёт в логе диагностики.
/// </summary>
// Traceability: openspec:http-api/transport#scenario-chat-error-cause-visible-in-ui
public static class ChatFailureDescriber
{
	// Шаблон сообщения провайдера о несуществующей модели: имя модели
	// переносится в совет владельцу, чтобы он сразу видел, что исправить.
	private static readonly Regex MissingModelPattern =
		new(@"model `(?<model>[^`]+)` does not exist", RegexOptions.Compiled, TimeSpan.FromMilliseconds(200));

	/// <summary>Классифицирует исключение генерации в описание причины отказа.</summary>
	/// <param name="failure">Исключение, оборвавшее генерацию.</param>
	/// <returns>Категория причины и человекочитаемый текст для владельца.</returns>
	public static ChatFailureDescription For(Exception failure)
	{
		ArgumentNullException.ThrowIfNull(failure);

		// Цепочка разворачивается с учётом агрегатов повторов: первопричина
		// может лежать в любом из внутренних исключений обёртки SDK.
		var chain = EnumerateChain(failure).ToList();

		// Незастроенный ключ модели: текст заглушки уже человекочитаем,
		// классификация нужна стабильной категории в логе.
		if (chain.Any(exception => exception.Message == UnconfiguredChatClient.ErrorMessage))
			return new ChatFailureDescription("unconfigured-api-key", UnconfiguredChatClient.ErrorMessage);

		var providerError = chain.OfType<ClientResultException>().FirstOrDefault();

		// Модель не найдена: провайдер прислал отказ model_not_found —
		// владелец видит имя модели и что проверить в параметрах чата.
		if (providerError is not null && IsMissingModel(providerError.Message))
		{
			var model = MissingModelPattern.Match(providerError.Message) is { Success: true } match
				? match.Groups["model"].Value
				: null;
			return new ChatFailureDescription(
				"model-not-found",
				model is null
					? "Модель не найдена у провайдера: проверьте имя модели в параметрах чата."
					: $"Модель \"{model}\" не найдена у провайдера: проверьте имя модели в параметрах чата.");
		}

		// Провайдер недоступен: попытки соединения не удались — совет идёт
		// к адресу эндпоинта и сети, а не к стеку повторов.
		if (chain.Any(IsNetworkFailure))
			return new ChatFailureDescription(
				"provider-unreachable",
				"Провайдер модели недоступен: проверьте адрес Llm:BaseUrl и соединение с сетью.");

		// Провайдер отклонил запрос HTTP-статусом: адрес и соединение в
		// порядке, причина в настройках провайдера или самой модели.
		if (providerError is not null)
			return new ChatFailureDescription(
				"provider-rejected",
				providerError.Status > 0
					? $"Провайдер отклонил запрос модели (HTTP {providerError.Status}): проверьте настройки провайдера и повторите запрос."
					: "Провайдер отклонил запрос модели: проверьте настройки провайдера и повторите запрос.");

		// Поток ответа провайдера не разобрался: битый поток обновлений —
		// совет повторить запрос или сменить модель вместо стека парсера.
		if (chain.OfType<JsonException>().Any())
			return new ChatFailureDescription(
				"invalid-provider-response",
				"Провайдер вернул ответ в неожиданном формате: повторите запрос или смените модель.");

		// Неопознанная причина: общий совет вместо сырого текста исключения.
		return new ChatFailureDescription(
			"generation-failed",
			"Генерация ответа не удалась: повторите запрос позже.");
	}

	/// <summary>Модель не найдена: сообщение провайдера содержит код отказа.</summary>
	/// <param name="message">Сообщение исключения провайдера.</param>
	/// <returns>Сообщение распознано как отказ model_not_found.</returns>
	private static bool IsMissingModel(string message) =>
		message.Contains("model_not_found", StringComparison.Ordinal) ||
		MissingModelPattern.IsMatch(message);

	// Сетевой отказ: транспортные исключения либо накопитель повторов
	// System.ClientModel, склеивший попытки в «Retry failed after N tries».
	private static bool IsNetworkFailure(Exception exception) =>
		exception is HttpRequestException or SocketException ||
		exception.Message.Contains("Retry failed after", StringComparison.Ordinal);

	// Цепочка исключений разворачивается вглубь: агрегат повторов хранит
	// попытки списком, прочие обёртки — одиночным внутренним исключением.
	private static IEnumerable<Exception> EnumerateChain(Exception failure)
	{
		yield return failure;
		IEnumerable<Exception> children = failure is AggregateException aggregate
			? aggregate.InnerExceptions
			: failure.InnerException is null ? [] : [failure.InnerException];
		foreach (var child in children)
		{
			foreach (var nested in EnumerateChain(child))
				yield return nested;
		}
	}
}