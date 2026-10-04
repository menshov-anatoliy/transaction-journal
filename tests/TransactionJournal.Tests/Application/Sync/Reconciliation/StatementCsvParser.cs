using System.Globalization;
using System.Text;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;

namespace TransactionJournal.Tests.Application.Sync.Reconciliation;

/// <summary>
/// Парсер CSV-выгрузки Bybit AssetChangeDetails: пропускает мета-строку «UID:»,
/// читает заголовок со второй строки, трактует «--» как отсутствие значения
/// и объединяет все части UTA- и fund-файлов каталога выгрузки.
/// </summary>
public static class StatementCsvParser
{
	/// <summary>Начало мета-строки первой строки выгрузки: «UID: 531298197, ...».</summary>
	private const string MetaLinePrefix = "UID:";

	/// <summary>Маска имён UTA-файлов каталога выгрузки.</summary>
	private const string UtaSearchPattern = "AssetChangeDetails_uta_*.csv";

	/// <summary>Маска имён fund-файлов каталога выгрузки.</summary>
	private const string FundSearchPattern = "AssetChangeDetails_fund_*.csv";

	/// <summary>Маркер отсутствия значения в ячейке выгрузки.</summary>
	private const string MissingValueMarker = "--";

	#region Чтение выгрузки

	/// <summary>
	/// Читает все части UTA- и fund-файлов каталога выгрузки и объединяет их строки:
	/// биржа может резать выгрузку на части («_0», «_1», ...), для сверки важен полный набор.
	/// </summary>
	/// <param name="directory">Каталог с файлами выгрузки.</param>
	/// <returns>Строки всех файлов выгрузки.</returns>
	public static IReadOnlyList<StatementRow> ParseDirectory(string directory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		var rows = new List<StatementRow>();
		rows.AddRange(ParseFiles(directory, UtaSearchPattern, StatementFileKind.Uta));
		rows.AddRange(ParseFiles(directory, FundSearchPattern, StatementFileKind.Fund));
		return rows;
	}

	/// <summary>
	/// Разбирает один файл выгрузки.
	/// </summary>
	/// <param name="path">Путь к файлу CSV.</param>
	/// <param name="kind">Какой файл выгрузки читается: UTA или fund.</param>
	/// <returns>Строки файла.</returns>
	public static IReadOnlyList<StatementRow> ParseFile(string path, StatementFileKind kind)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		if (File.Exists(path) == false)
		{
			throw new FileNotFoundException($"Файл выгрузки не найден: {path}", path);
		}

		return ParseLines(File.ReadAllLines(path), kind, Path.GetFileName(path));
	}

	#endregion

	#region Разбор строк файла

	/// <summary>
	/// Разбирает уже прочитанные строки одного файла выгрузки.
	/// </summary>
	/// <param name="lines">Строки файла.</param>
	/// <param name="kind">Какой файл выгрузки читается.</param>
	/// <param name="sourceFile">Имя файла для локализации строк в отчёте.</param>
	/// <returns>Строки выгрузки.</returns>
	private static IReadOnlyList<StatementRow> ParseLines(string[] lines, StatementFileKind kind, string sourceFile)
	{
		// Первая строка выгрузки — мета-строка «UID: ...», данных она не несёт;
		// заголовок колонок идёт сразу за ней.
		var firstIndex = lines.Length > 0 && lines[0].TrimStart().StartsWith(MetaLinePrefix, StringComparison.Ordinal)
			? 1
			: 0;
		if (lines.Length <= firstIndex)
		{
			return [];
		}

		var map = ResolveColumns(SplitCsvLine(lines[firstIndex]), sourceFile);
		var rows = new List<StatementRow>();
		for (var lineIndex = firstIndex + 1; lineIndex < lines.Length; lineIndex++)
		{
			var line = lines[lineIndex];
			if (string.IsNullOrWhiteSpace(line))
			{
				continue;
			}

			var fields = SplitCsvLine(line);
			var lineNumber = lineIndex + 1;
			rows.Add(new StatementRow
			{
				FileKind = kind,
				SourceFile = sourceFile,
				LineNumber = lineNumber,
				Type = RequiredField(fields, map.Type, "Type", sourceFile, lineNumber),
				Currency = RequiredField(fields, map.Currency, "Currency/Coin", sourceFile, lineNumber),
				Contract = OptionalField(fields, map.Contract),
				Direction = OptionalField(fields, map.Direction),
				Quantity = OptionalDecimal(fields, map.Quantity, "Quantity/QTY", sourceFile, lineNumber),
				FilledPrice = OptionalDecimal(fields, map.FilledPrice, "Filled Price", sourceFile, lineNumber),
				FeePaid = OptionalDecimal(fields, map.FeePaid, "Fee Paid", sourceFile, lineNumber),
				TimeUtc = ParseTime(RequiredField(fields, map.Time, "Time(UTC)", sourceFile, lineNumber), sourceFile, lineNumber),
			});
		}

		return rows;
	}

	/// <summary>
	/// Читает все файлы одного вида выгрузки каталога, упорядочивая части по имени.
	/// </summary>
	/// <param name="directory">Каталог с файлами выгрузки.</param>
	/// <param name="searchPattern">Маска имён файлов.</param>
	/// <param name="kind">Какой файл выгрузки читается.</param>
	/// <returns>Строки всех частей.</returns>
	private static IEnumerable<StatementRow> ParseFiles(string directory, string searchPattern, StatementFileKind kind)
	{
		if (Directory.Exists(directory) == false)
		{
			yield break;
		}

		var files = Directory.EnumerateFiles(directory, searchPattern).OrderBy(file => file, StringComparer.Ordinal);
		foreach (var file in files)
		{
			foreach (var row in ParseFile(file, kind))
			{
				yield return row;
			}
		}
	}

	#endregion

	#region Колонки заголовка

	/// <summary>Индексы колонок заголовка, нужных для разбора строк.</summary>
	/// <param name="Type">Колонка типа строки.</param>
	/// <param name="Currency">Колонка валюты (UTA «Currency», fund «Coin»).</param>
	/// <param name="Quantity">Колонка количества (UTA «Quantity», fund «QTY»).</param>
	/// <param name="Time">Колонка времени (UTA «Time(UTC)», fund «Date &amp; Time(UTC)»).</param>
	/// <param name="Contract">Колонка инструмента UTA-файла; в fund-файле отсутствует.</param>
	/// <param name="Direction">Колонка стороны UTA-файла; в fund-файле отсутствует.</param>
	/// <param name="FilledPrice">Колонка цены исполнения UTA-файла; в fund-файле отсутствует.</param>
	/// <param name="FeePaid">Колонка комиссии UTA-файла; в fund-файле отсутствует.</param>
	private sealed record ColumnMap(
		int Type,
		int Currency,
		int Quantity,
		int Time,
		int Contract,
		int Direction,
		int FilledPrice,
		int FeePaid);

	/// <summary>
	/// Сопоставляет колонки заголовка по именам: UTA- и fund-файлы различаются
	/// набором и названиями колонок, разбор идёт по имени, а не по позиции.
	/// </summary>
	/// <param name="header">Поля строки заголовка.</param>
	/// <param name="sourceFile">Имя файла для сообщения об ошибке.</param>
	/// <returns>Карта колонок.</returns>
	private static ColumnMap ResolveColumns(IReadOnlyList<string> header, string sourceFile)
	{
		// Колонка ищется по любому из допустимых имён без учёта регистра.
		int IndexOf(params string[] names)
		{
			for (var i = 0; i < header.Count; i++)
			{
				foreach (var name in names)
				{
					if (string.Equals(header[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
					{
						return i;
					}
				}
			}

			return -1;
		}

		var map = new ColumnMap(
			Type: IndexOf("Type"),
			Currency: IndexOf("Currency", "Coin"),
			Quantity: IndexOf("Quantity", "QTY"),
			Time: IndexOf("Time(UTC)", "Date & Time(UTC)"),
			Contract: IndexOf("Contract"),
			Direction: IndexOf("Direction"),
			FilledPrice: IndexOf("Filled Price"),
			FeePaid: IndexOf("Fee Paid"));

		var missing = new List<string>();
		if (map.Type < 0)
		{
			missing.Add("Type");
		}

		if (map.Currency < 0)
		{
			missing.Add("Currency/Coin");
		}

		if (map.Quantity < 0)
		{
			missing.Add("Quantity/QTY");
		}

		if (map.Time < 0)
		{
			missing.Add("Time(UTC)");
		}

		if (missing.Count > 0)
		{
			throw new InvalidDataException($"В заголовке выгрузки {sourceFile} нет обязательных колонок: {string.Join(", ", missing)}.");
		}

		return map;
	}

	#endregion

	#region Значения полей

	/// <summary>
	/// Возвращает обязательное текстовое поле строки.
	/// </summary>
	/// <param name="fields">Поля строки.</param>
	/// <param name="index">Индекс колонки.</param>
	/// <param name="columnName">Имя колонки для сообщения об ошибке.</param>
	/// <param name="sourceFile">Имя файла для сообщения об ошибке.</param>
	/// <param name="lineNumber">Номер строки для сообщения об ошибке.</param>
	/// <returns>Значение поля.</returns>
	private static string RequiredField(IReadOnlyList<string> fields, int index, string columnName, string sourceFile, int lineNumber)
	{
		if (index < 0 || index >= fields.Count)
		{
			throw new InvalidDataException($"Строка {lineNumber} файла {sourceFile} не содержит значение колонки {columnName}.");
		}

		return fields[index].Trim();
	}

	/// <summary>
	/// Возвращает необязательное текстовое поле: «--» и пустота означают отсутствие значения.
	/// </summary>
	/// <param name="fields">Поля строки.</param>
	/// <param name="index">Индекс колонки; -1 означает, что колонки в файле нет.</param>
	/// <returns>Значение поля или null.</returns>
	private static string? OptionalField(IReadOnlyList<string> fields, int index)
	{
		if (index < 0 || index >= fields.Count)
		{
			return null;
		}

		var value = fields[index].Trim();
		return value == MissingValueMarker || value.Length == 0 ? null : value;
	}

	/// <summary>
	/// Возвращает необязательное числовое поле: «--» и пустота означают отсутствие значения.
	/// </summary>
	/// <param name="fields">Поля строки.</param>
	/// <param name="index">Индекс колонки; -1 означает, что колонки в файле нет.</param>
	/// <param name="columnName">Имя колонки для сообщения об ошибке.</param>
	/// <param name="sourceFile">Имя файла для сообщения об ошибке.</param>
	/// <param name="lineNumber">Номер строки для сообщения об ошибке.</param>
	/// <returns>Числовое значение или null.</returns>
	private static decimal? OptionalDecimal(IReadOnlyList<string> fields, int index, string columnName, string sourceFile, int lineNumber)
	{
		var text = index < 0 || index >= fields.Count ? null : OptionalField(fields, index);
		if (text == null)
		{
			return null;
		}

		if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) == false)
		{
			throw new InvalidDataException($"Строка {lineNumber} файла {sourceFile} содержит нечисловое значение колонки {columnName}: {text}.");
		}

		return value;
	}

	/// <summary>
	/// Разбирает время строки выгрузки в UTC: формат «yyyy-MM-dd HH:mm:ss»
	/// без указания зоны, поэтому он трактуется как UTC явно.
	/// </summary>
	/// <param name="text">Текстовое значение времени.</param>
	/// <param name="sourceFile">Имя файла для сообщения об ошибке.</param>
	/// <param name="lineNumber">Номер строки для сообщения об ошибке.</param>
	/// <returns>Время строки в UTC.</returns>
	private static DateTime ParseTime(string text, string sourceFile, int lineNumber)
	{
		if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value) == false)
		{
			throw new InvalidDataException($"Строка {lineNumber} файла {sourceFile} содержит неразбираемое время: {text}.");
		}

		return value;
	}

	/// <summary>
	/// Разрезает строку CSV на поля: кавычки сохраняют запятые внутри поля,
	/// удвоенные кавычки экранируют кавычку.
	/// </summary>
	/// <param name="line">Строка CSV.</param>
	/// <returns>Поля строки.</returns>
	private static List<string> SplitCsvLine(string line)
	{
		var fields = new List<string>();
		var builder = new StringBuilder();
		var inQuotes = false;
		for (var i = 0; i < line.Length; i++)
		{
			var ch = line[i];
			if (inQuotes)
			{
				if (ch == '"')
				{
					if (i + 1 < line.Length && line[i + 1] == '"')
					{
						builder.Append('"');
						i++;
					}
					else
					{
						inQuotes = false;
					}
				}
				else
				{
					builder.Append(ch);
				}
			}
			else if (ch == '"')
			{
				inQuotes = true;
			}
			else if (ch == ',')
			{
				fields.Add(builder.ToString());
				builder.Clear();
			}
			else
			{
				builder.Append(ch);
			}
		}

		fields.Add(builder.ToString());
		return fields;
	}

	#endregion
}
