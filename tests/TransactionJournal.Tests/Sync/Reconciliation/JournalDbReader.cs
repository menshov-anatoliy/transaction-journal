using System.Globalization;
using System.Text.Json;

namespace TransactionJournal.Tests.Sync.Reconciliation;

/// <summary>
/// Читатель живой базы журнала в режиме только для чтения: прямые SQL-запросы
/// через Microsoft.Data.Sqlite со строкой подключения Mode=ReadOnly;Pooling=False
/// исключают любую скрытую запись и не блокируют WAL живой базы. Схема модели
/// приложения не используется: живая база может отставать от неё.
/// Traceability: openspec:sync/bybit-statement-reconciliation#requirement-one-off-diagnostic-run
/// </summary>
public sealed class JournalDbReader
{
	/// <summary>Шаблон строки подключения только для чтения без пула соединений.</summary>
	private const string ReadOnlyConnectionStringTemplate = "Data Source={0};Mode=ReadOnly;Pooling=False";

	/// <summary>Путь к файлу живой базы журнала.</summary>
	private readonly string _databasePath;

	/// <summary>
	/// Создаёт читатель живой базы журнала.
	/// </summary>
	/// <param name="databasePath">Путь к файлу journal.db.</param>
	public JournalDbReader(string databasePath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
		if (File.Exists(databasePath) == false)
		{
			throw new FileNotFoundException($"База журнала не найдена: {databasePath}", databasePath);
		}

		_databasePath = databasePath;
	}

	/// <summary>
	/// Открывает соединение к живой базе в режиме только для чтения:
	/// единственная каноническая точка строки подключения читателя.
	/// </summary>
	/// <returns>Открытое соединение; закрытие остаётся за вызывающим.</returns>
	public Microsoft.Data.Sqlite.SqliteConnection OpenReadOnlyConnection()
	{
		var connection = new Microsoft.Data.Sqlite.SqliteConnection(
			string.Format(CultureInfo.InvariantCulture, ReadOnlyConnectionStringTemplate, _databasePath));
		connection.Open();
		return connection;
	}

	/// <summary>
	/// Читает сырые записи исполнения и delivery-записи в границах диапазона дат
	/// строк выгрузки (включительно) и считает записи вне диапазона.
	/// </summary>
	/// <param name="fromMsInclusive">Нижняя граница, мс с эпохи Unix.</param>
	/// <param name="toMsInclusive">Верхняя граница, мс с эпохи Unix.</param>
	/// <returns>Снимок сырых записей журнала.</returns>
	public JournalRawData ReadInRange(long fromMsInclusive, long toMsInclusive)
	{
		using var connection = OpenReadOnlyConnection();
		return new JournalRawData(
			Executions: ReadExecutions(connection, fromMsInclusive, toMsInclusive, out var executionsOutside),
			Deliveries: ReadDeliveries(connection, fromMsInclusive, toMsInclusive, out var deliveriesOutside),
			FromMsInclusive: fromMsInclusive,
			ToMsInclusive: toMsInclusive,
			ExecutionsOutsideRange: executionsOutside,
			DeliveriesOutsideRange: deliveriesOutside);
	}

	#region Чтение таблиц

	/// <summary>
	/// Читает записи исполнения в диапазоне и считает записи вне диапазона.
	/// </summary>
	private static IReadOnlyList<JournalExecution> ReadExecutions(
		Microsoft.Data.Sqlite.SqliteConnection connection, long fromMsInclusive, long toMsInclusive, out int outsideRange)
	{
		var executions = new List<JournalExecution>();
		using (var command = connection.CreateCommand())
		{
			command.CommandText = """
				SELECT Id, ExecId, PayloadJson
				FROM RawExecutions
				WHERE ExecTimeMs >= $from AND ExecTimeMs <= $to
				ORDER BY ExecTimeMs, Id
				""";
			command.Parameters.AddWithValue("$from", fromMsInclusive);
			command.Parameters.AddWithValue("$to", toMsInclusive);
			using var reader = command.ExecuteReader();
			while (reader.Read())
			{
				var payload = JsonDocument.Parse(reader.GetString(2)).RootElement;
				var timeMs = GetInt64(payload, "execTime")
					?? throw new InvalidDataException($"Запись исполнения Id={reader.GetInt64(0)} не содержит execTime.");
				executions.Add(new JournalExecution(
					Id: reader.GetInt64(0),
					ExecId: reader.GetString(1),
					Symbol: GetString(payload, "symbol") ?? string.Empty,
					Side: (GetString(payload, "side") ?? string.Empty).ToUpperInvariant(),
					ExecType: GetString(payload, "execType") ?? string.Empty,
					Quantity: GetDecimal(payload, "execQty"),
					Price: GetDecimal(payload, "execPrice"),
					Fee: GetDecimal(payload, "execFee"),
					TimeUtc: DateTimeOffset.FromUnixTimeMilliseconds(timeMs).UtcDateTime));
			}
		}

		outsideRange = CountOutsideRange(connection, "RawExecutions", "ExecTimeMs", fromMsInclusive, toMsInclusive);
		return executions;
	}

	/// <summary>
	/// Читает delivery-записи в диапазоне и считает записи вне диапазона.
	/// </summary>
	private static IReadOnlyList<JournalDelivery> ReadDeliveries(
		Microsoft.Data.Sqlite.SqliteConnection connection, long fromMsInclusive, long toMsInclusive, out int outsideRange)
	{
		var deliveries = new List<JournalDelivery>();
		using (var command = connection.CreateCommand())
		{
			command.CommandText = """
				SELECT Id, DeliveryTimeMs, PayloadJson
				FROM RawDeliveries
				WHERE DeliveryTimeMs >= $from AND DeliveryTimeMs <= $to
				ORDER BY DeliveryTimeMs, Id
				""";
			command.Parameters.AddWithValue("$from", fromMsInclusive);
			command.Parameters.AddWithValue("$to", toMsInclusive);
			using var reader = command.ExecuteReader();
			while (reader.Read())
			{
				var payload = JsonDocument.Parse(reader.GetString(2)).RootElement;
				deliveries.Add(new JournalDelivery(
					Id: reader.GetInt64(0),
					Symbol: GetString(payload, "symbol") ?? string.Empty,
					Quantity: GetDecimal(payload, "position"),
					TimeUtc: DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)).UtcDateTime));
			}
		}

		outsideRange = CountOutsideRange(connection, "RawDeliveries", "DeliveryTimeMs", fromMsInclusive, toMsInclusive);
		return deliveries;
	}

	/// <summary>
	/// Считает записи таблицы вне границ диапазона для сводки исключённых.
	/// </summary>
	private static int CountOutsideRange(
		Microsoft.Data.Sqlite.SqliteConnection connection, string tableName, string timeColumn, long fromMsInclusive, long toMsInclusive)
	{
		// Имена таблицы и колонки приходят только из констант читателя — интерполяция безопасна.
		using var command = connection.CreateCommand();
		command.CommandText = $"SELECT COUNT(*) FROM {tableName} WHERE {timeColumn} < $from OR {timeColumn} > $to";
		command.Parameters.AddWithValue("$from", fromMsInclusive);
		command.Parameters.AddWithValue("$to", toMsInclusive);
		return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
	}

	#endregion

	#region Разбор PayloadJson

	/// <summary>
	/// Возвращает строковое поле PayloadJson или null, когда поле отсутствует.
	/// </summary>
	private static string? GetString(JsonElement element, string name)
	{
		return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
	}

	/// <summary>
	/// Возвращает числовое поле PayloadJson: биржа отдаёт числа строками,
	/// а пустая строка означает отсутствие значения.
	/// </summary>
	private static decimal? GetDecimal(JsonElement element, string name)
	{
		if (element.TryGetProperty(name, out var value) == false)
		{
			return null;
		}

		return value.ValueKind switch
		{
			JsonValueKind.String => decimal.TryParse(
				value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : null,
			JsonValueKind.Number => value.GetDecimal(),
			_ => null,
		};
	}

	/// <summary>
	/// Возвращает целочисленное поле PayloadJson или null, когда поле отсутствует или не число.
	/// </summary>
	private static long? GetInt64(JsonElement element, string name)
	{
		if (element.TryGetProperty(name, out var value) == false || value.ValueKind != JsonValueKind.Number)
		{
			return null;
		}

		return value.GetInt64();
	}

	#endregion
}
