using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TransactionJournal.Bybit;

/// <summary>
/// Настройки System.Text.Json для разбора ответов Bybit V5: биржа возвращает числа
/// строками ("16000") и иногда пустой строкой вместо отсутствующего значения,
/// а булевы признаки в отдельных эндпоинтах приходят строками "true"/"false".
/// Traceability: doc:docs/research/bybit-api.md#1-история-исполнения-сделок-unified-аккаунта-execution-list
/// </summary>
internal static class BybitJson
{
	/// <summary>Общие настройки десериализации типизированных ответов Bybit.</summary>
	public static readonly JsonSerializerOptions Options = new()
	{
		// Числа у Bybit часто приходят строками; разрешаем чтение числа из строки.
		NumberHandling = JsonNumberHandling.AllowReadingFromString,
		Converters =
		{
			new BooleanJsonConverter(),
			new NullableDecimalJsonConverter(),
		},
	};

	#region Конвертеры

	/// <summary>
	/// Булево значение Bybit: принимает настоящий boolean и строки "true"/"false",
	/// потому что разные эндпоинты кодируют один и тот же признак по-разному.
	/// </summary>
	internal sealed class BooleanJsonConverter : JsonConverter<bool>
	{
		/// <inheritdoc cref="JsonConverter{T}.Read" />
		public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			return reader.TokenType switch
			{
				JsonTokenType.True => true,
				JsonTokenType.False => false,
				JsonTokenType.String when bool.TryParse(reader.GetString(), out var value) => value,
				_ => throw new JsonException($"Неожидаемый токен {reader.TokenType} для булева значения Bybit."),
			};
		}

		/// <inheritdoc cref="JsonConverter{T}.Write" />
		public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
		{
			writer.WriteBooleanValue(value);
		}
	}

	/// <summary>
	/// Десятичное число Bybit: принимает число, строку с инвариантным десятичным
	/// разделителем и пустую строку как признак отсутствующего значения (null).
	/// </summary>
	internal sealed class NullableDecimalJsonConverter : JsonConverter<decimal?>
	{
		/// <inheritdoc cref="JsonConverter{T}.Read" />
		public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		{
			return reader.TokenType switch
			{
				JsonTokenType.Null => null,
				JsonTokenType.Number => reader.GetDecimal(),
				JsonTokenType.String when decimal.TryParse(
					reader.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) => value,
				// Пустая или нечисловая строка — значения нет; биржа так кодирует отсутствующие суммы.
				JsonTokenType.String => null,
				_ => throw new JsonException($"Неожидаемый токен {reader.TokenType} для десятичного числа Bybit."),
			};
		}

		/// <inheritdoc cref="JsonConverter{T}.Write" />
		public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
		{
			if (value is { } decimalValue)
			{
				writer.WriteNumberValue(decimalValue);
			}
			else
			{
				writer.WriteNullValue();
			}
		}
	}

	#endregion
}
