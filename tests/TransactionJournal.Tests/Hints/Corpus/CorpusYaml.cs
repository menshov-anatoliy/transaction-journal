namespace TransactionJournal.Tests.Hints.Corpus;

using System.Text;

/// <summary>
/// Помощник тестов загрузчика корпуса: создаёт временный каталог карточек и
/// собирает валидные YAML-карточки схемы корпуса с точечными подменами полей.
/// </summary>
internal static class CorpusYaml
{
	/// <summary>Создаёт уникальный пустой каталог корпуса для одного теста.</summary>
	public static string TempDir()
	{
		var dir = Path.Combine(Path.GetTempPath(), "hints-corpus-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		return dir;
	}

	/// <summary>Записывает карточку в каталог корпуса под заданным именем файла.</summary>
	public static void Write(string dir, string fileName, string yaml)
		=> File.WriteAllText(Path.Combine(dir, fileName), yaml, new UTF8Encoding(false));

	/// <summary>Удаляет временный каталог корпуса со всем содержимым.</summary>
	public static void DeleteDir(string dir)
	{
		if (Directory.Exists(dir))
		{
			Directory.Delete(dir, recursive: true);
		}
	}

	/// <summary>
	/// Собирает валидную карточку схемы корпуса: машинный ключ подменяется
	/// параметром (null — implementation: null), статус и конфликтное объявление —
	/// опционально; retired-блок добавляется вместе с причиной снятия.
	/// </summary>
	public static string Card(
		string id,
		string? implementation = null,
		string status = "active",
		IReadOnlyList<string>? conflictsWith = null,
		string? retiredReason = null)
	{
		var sb = new StringBuilder();
		sb.AppendLine($"id: {id}");
		sb.AppendLine($"title: Правило {id}");
		sb.AppendLine("character: risk-mode");
		sb.AppendLine("technique: null");
		sb.AppendLine("clarity: crisp");
		sb.AppendLine("scope: open-constructions");
		sb.AppendLine($"status: {status}");
		sb.AppendLine("thresholds: []");
		sb.AppendLine("trigger:");
		sb.AppendLine($"  description: Тестовое условие правила {id}.");
		sb.AppendLine($"  implementation: {implementation ?? "null"}");
		sb.AppendLine("action:");
		sb.AppendLine($"  description: Тестовое действие правила {id}.");
		sb.AppendLine("  hintTemplate: null");
		if (conflictsWith is { Count: > 0 })
		{
			sb.AppendLine("conflicts_with:");
			foreach (var other in conflictsWith)
			{
				sb.AppendLine($"  - {other}");
			}
		}

		if (retiredReason is not null)
		{
			sb.AppendLine("retired:");
			sb.AppendLine($"  reason: {retiredReason}");
		}

		sb.AppendLine("sources:");
		sb.AppendLine("  - tag: ТЕСТ");
		sb.AppendLine("    file: \"Тесты/Тест.md\"");
		sb.AppendLine("    quotes:");
		sb.AppendLine($"      - '«Цитата-доказательство {id}»'");
		return sb.ToString();
	}
}
