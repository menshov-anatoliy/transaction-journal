namespace TransactionJournal.Chats;

using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

/// <summary>
/// Захват аргументов тулов для операционной трассы: обёртка AIFunction в
/// момент исполнения тула добавляет на текущую активность — встроенный спан
/// execute_tool {name} — событие tj.tool.args с JSON аргументов вызова.
/// Событие metadata-only в пределах компакта: JSON усекается до 2048
/// символов с маркером усечения, а сериализация устойчива — значение, которое
/// не сериализуется, деградирует в ToString, и сбой сериализации не ломает
/// вызов тула. Полные тексты сообщений модели не пишутся никогда:
/// EnableSensitiveData всегда false — задан в конвейере ChatAgent.
// Traceability: openspec:ops/agent-tracing#requirement-trace-metadata-only
// Traceability: openspec:ops/agent-tracing#scenario-trace-tool-args-capped
/// </summary>
internal static class ChatToolArgsCapture
{
	/// <summary>
	/// Оборачивает реестр инструментов захватом аргументов; на уровне basic
	/// события tj.tool.args не добавляются вовсе — функции возвращаются как
	/// есть, поэтому JSONL и OTLP видят один и тот же состав трассы.
	// Traceability: openspec:ops/agent-tracing#scenario-trace-basic-without-tool-args
	// Traceability: openspec:ops/agent-tracing#scenario-trace-detailed-with-tool-args
	/// </summary>
	/// <param name="functions">Реестр read-only инструментов чата.</param>
	/// <param name="detail">Уровень детализации трассы.</param>
	/// <returns>Функции с захватом аргументов на detailed; исходный список — на basic.</returns>
	public static IReadOnlyList<AIFunction> Wrap(IReadOnlyList<AIFunction> functions, ChatTracingDetail detail) =>
		detail == ChatTracingDetail.Detailed && functions.Count > 0
			? functions.Select(function => new ArgsCapturingFunction(function)).ToList()
			: functions;

	/// <summary>
	/// Декоратор тула: перехватывает исполнение и пишет событие tj.tool.args
	/// на Activity.Current — в конвейере ChatAgent это спан execute_tool
	/// {name} (проверено эмпирически), без слушателя трассировки события нет.
	/// </summary>
	private sealed class ArgsCapturingFunction(AIFunction inner) : AIFunction
	{
		/// <summary>Имя события трассы с аргументами вызова тула.</summary>
		public const string EventName = "tj.tool.args";

		/// <summary>Атрибут события: JSON аргументов вызова.</summary>
		public const string ArgsJsonKey = "tj.tool.args.json";

		/// <summary>Атрибут события: признак усечения JSON до предела.</summary>
		public const string TruncatedKey = "tj.tool.args.truncated";

		/// <summary>Предел длины JSON аргументов в символах — 2 КБ.</summary>
		public const int MaxJsonLength = 2048;

		/// <summary>Маркер усечения, дописываемый к обрезанному JSON.</summary>
		public const string TruncationMarker = "…[truncated]";

		public override string Name => inner.Name;

		public override string Description => inner.Description;

		public override JsonElement JsonSchema => inner.JsonSchema;

		public override JsonElement? ReturnJsonSchema => inner.ReturnJsonSchema;

		public override IReadOnlyDictionary<string, object?> AdditionalProperties => inner.AdditionalProperties;

		public override MethodInfo? UnderlyingMethod => inner.UnderlyingMethod;

		public override JsonSerializerOptions JsonSerializerOptions => inner.JsonSerializerOptions;

		protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
		{
			// Событие пишется до вызова функции — в момент исполнения тула;
			// сбой сериализации не должен доходить до самой функции.
			RecordArguments(Activity.Current, arguments);
			return inner.InvokeAsync(arguments, cancellationToken);
		}

		/// <summary>Пишет событие с JSON аргументов на спан исполнения; без активности — события нет.</summary>
		private void RecordArguments(Activity? span, AIFunctionArguments arguments)
		{
			if (span is null)
			{
				return;
			}

			var json = TrySerializeArguments(arguments);
			if (json is null)
			{
				return;
			}

			var truncated = json.Length > MaxJsonLength;
			if (truncated)
			{
				// Усечение до предела с маркером; хвостовой старший суррогат
				// обрезанной пары срезается, чтобы не оставлять символ без пары.
				var head = json[..(MaxJsonLength - TruncationMarker.Length)];
				if (char.IsHighSurrogate(head[^1]) && (head.Length < 2 || char.IsLowSurrogate(head[^2]) == false))
				{
					head = head[..^1];
				}

				json = head + TruncationMarker;
			}

			span.AddEvent(new ActivityEvent(
				EventName,
				DateTimeOffset.Now,
				new ActivityTagsCollection
				{
					[ArgsJsonKey] = json,
					[TruncatedKey] = truncated,
				}));
		}

		/// <summary>
		/// Устойчивая сериализация аргументов: несериализуемое значение
		/// деградирует в ToString; полный сбой сборки JSON оставляет событие
		/// без аргументов, но не ломает вызов тула.
		/// </summary>
		private static string? TrySerializeArguments(AIFunctionArguments arguments)
		{
			try
			{
				var builder = new StringBuilder("{");
				var first = true;
				foreach (var (name, value) in arguments)
				{
					if (first == false)
					{
						builder.Append(',');
					}

					first = false;
					builder.Append(JsonSerializer.Serialize(name));
					builder.Append(':');
					builder.Append(SerializeValue(value));
				}

				builder.Append('}');
				return builder.ToString();
			}
			catch (Exception)
			{
				return null;
			}
		}

		/// <summary>Значение аргумента в JSON; сбой сериализации значения деградирует в ToString.</summary>
		private static string SerializeValue(object? value)
		{
			try
			{
				return JsonSerializer.Serialize(value);
			}
			catch (Exception)
			{
				return JsonSerializer.Serialize(value?.ToString());
			}
		}
	}
}
