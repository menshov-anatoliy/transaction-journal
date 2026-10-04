namespace TransactionJournal.Tests.Consultations;

using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Consultations;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Проверки инструкций агента консультаций: встроенный дефолт несёт все
/// обязательные принципы поведения ассистента, валидный файл инструкций
/// переопределяет дефолт, а отсутствие или битость файла чат не ломает —
/// работает встроенный дефолт.
/// Traceability: openspec:consultations/context#requirement-context-agent-instructions-file
/// </summary>
[TestClass]
public class ConsultationInstructionsTests
{
	/// <summary>Относительный путь проекта Web в решении.</summary>
	private const string WebProjectPath = @"src\TransactionJournal\TransactionJournal.csproj";

	/// <summary>Относительный путь деплоимого файла инструкций в репозитории.</summary>
	private const string PromptFilePath = "consultation-prompt.md";

	[TestMethod]
	[Description("Встроенный дефолт инструкций несёт все обязательные принципы")]
	// Контракт состава дефолтных инструкций: сценарии «если/то» без прогнозов
	// цены, цитирование id карточек корпуса, маркировка «вне корпуса правил»,
	// read-only план управления markdown-текстом и пост-мортем закрытых
	// конструкций присутствуют в тексте дефолта.
	// Traceability: openspec:consultations/context#requirement-context-scenario-conduct
	// Traceability: openspec:consultations/context#requirement-context-postmortem-mode
	public void TryIfDefaultInstructionsCarryMandatoryPrinciples()
	{
		// Arrange: встроенный минимальный дефолт инструкций.
		var instructions = ConsultationInstructions.DefaultInstructions;

		// Act: состав дефолта не вычисляется — проверяются его элементы.

		// Assert: каждый обязательный принцип выражен в тексте дефолта;
		// переносы строк схлопываются — состав проверяется без привязки к
		// ширине строк исходника.
		var flat = string.Join(' ', instructions.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
		Assert.That(flat, Does.Contain("если/то"));
		Assert.That(flat, Does.Contain("прогноз").IgnoreCase);
		Assert.That(flat, Does.Contain("read_rule_card"));
		Assert.That(flat, Does.Contain("вне корпуса правил"));
		Assert.That(flat, Does.Contain("markdown"));
		Assert.That(flat, Does.Contain("пост-мортем").IgnoreCase);
		Assert.That(flat, Does.Contain("работа над ошибками").IgnoreCase);
	}

	[TestMethod]
	[Description("Валидный файл инструкций переопределяет встроенный дефолт")]
	// Проверяем сценарий переопределения: непустой consultation-prompt.md рядом
	// с rules/ используется ассистентом вместо встроенного дефолта.
	// Traceability: openspec:consultations/context#scenario-context-instructions-override
	public async Task TryIfValidFileOverridesBuiltInDefault()
	{
		// Arrange: временный каталог с валидным файлом инструкций владельца.
		var dir = CreateTempDir();
		try
		{
			var path = Path.Combine(dir, ConsultationInstructions.DefaultFileName);
			await File.WriteAllTextAsync(path, "СВОИ ИНСТРУКЦИИ ВЛАДЕЛЬЦА: отвечай только цитатами корпуса");
			var instructions = new ConsultationInstructions(path);

			// Act: читаем инструкции агента.
			var text = instructions.Read();

			// Assert: ассистенту передан текст файла, а не встроенный дефолт.
			Assert.That(text, Is.EqualTo("СВОИ ИНСТРУКЦИИ ВЛАДЕЛЬЦА: отвечай только цитатами корпуса"));
		}
		finally
		{
			DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Отсутствующий файл инструкций не ломает чат — работает встроенный дефолт")]
	// Проверяем сценарий отсутствия файла: пути с несуществующим файлом
	// соответствует встроенный дефолт, исключение не поднимается.
	// Traceability: openspec:consultations/context#scenario-context-instructions-missing-ok
	public void TryIfMissingFileFallsBackToBuiltInDefault()
	{
		// Arrange: путь файла инструкций, которого не существует.
		var path = Path.Combine(CreateTempDir(), ConsultationInstructions.DefaultFileName);
		var instructions = new ConsultationInstructions(path);

		// Act: читаем инструкции агента.
		var text = instructions.Read();

		// Assert: чат продолжает работу на встроенном дефолте.
		Assert.That(text, Is.EqualTo(ConsultationInstructions.DefaultInstructions));
	}

	[TestMethod]
	[DataRow("")]
	[DataRow("   ")]
	[Description("Пустой или пробельный файл инструкций не ломает чат — работает встроенный дефолт")]
	// Проверяем сценарий битости файла: пустое или пробельное содержимое
	// не считается валидными инструкциями, ассистент получает встроенный дефолт.
	// Traceability: openspec:consultations/context#scenario-context-instructions-missing-ok
	public async Task TryIfEmptyFileFallsBackToBuiltInDefault(string content)
	{
		// Arrange: временный каталог с битым файлом инструкций.
		var dir = CreateTempDir();
		try
		{
			var path = Path.Combine(dir, ConsultationInstructions.DefaultFileName);
			await File.WriteAllTextAsync(path, content);
			var instructions = new ConsultationInstructions(path);

			// Act: читаем инструкции агента.
			var text = instructions.Read();

			// Assert: пустой файл равнозначен отсутствующему — работает дефолт.
			Assert.That(text, Is.EqualTo(ConsultationInstructions.DefaultInstructions));
		}
		finally
		{
			DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Нечитаемый файл инструкций не ломает чат — работает встроенный дефолт")]
	// Проверяем сценарий нечитаемости: путь на каталог вместо файла даёт
	// ошибку чтения, которая откатывается на встроенный дефолт без исключения.
	// Traceability: openspec:consultations/context#scenario-context-instructions-missing-ok
	public void TryIfUnreadableFileFallsBackToBuiltInDefault()
	{
		// Arrange: на месте файла инструкций находится каталог.
		var dir = CreateTempDir();
		try
		{
			var instructions = new ConsultationInstructions(dir);

			// Act: читаем инструкции агента.
			var text = instructions.Read();

			// Assert: нечитаемость файла оставляет чат на встроенном дефолте.
			Assert.That(text, Is.EqualTo(ConsultationInstructions.DefaultInstructions));
		}
		finally
		{
			DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Деплоимый файл инструкций лежит в корне решения и копируется рядом с rules/")]
	// Проверяем дефолтное размещение: consultation-prompt.md находится рядом с
	// каталогом rules/ в репозитории, а проект Web копирует его в BaseDirectory
	// при сборке — файл доступен для правки владельцем без пересборки кода.
	// Traceability: openspec:consultations/context#requirement-context-agent-instructions-file
	public void TryIfDeployablePromptFileSitsNextToRules()
	{
		// Arrange: корень решения с каталогом rules и проектом Web.
		var root = FindSolutionRoot().FullName;
		var projectText = File.ReadAllText(Path.Combine(root, WebProjectPath));

		// Act: проверяем наличие файла рядом с rules и правила деплоя в csproj.

		// Assert: файл лежит рядом с rules/, csproj копирует его в корень вывода.
		Assert.That(File.Exists(Path.Combine(root, PromptFilePath)), Is.True, "Файл consultation-prompt.md отсутствует рядом с rules/ в корне решения.");
		StringAssert.Contains(projectText, @"..\..\consultation-prompt.md", "Проект Web не деплоит consultation-prompt.md.");
		StringAssert.Contains(projectText, "CopyToOutputDirectory", "Деплой consultation-prompt.md должен копировать файл в вывод сборки.");
	}

	[TestMethod]
	[ExpectedException(typeof(ArgumentNullException))]
	[Description("Null-путь файла инструкций отклоняется")]
	// Проверяем негативный контракт конструктора: null-путь отклоняется
	// аргумент-null-исключением до любых чтений файла.
	public void ThrowOnNullPath()
	{
		// Arrange: источник инструкций без пути файла.

		// Act / Assert: создание поднимает ArgumentNullException.
		_ = new ConsultationInstructions(null!);
	}

	[TestMethod]
	[ExpectedException(typeof(ArgumentException))]
	[DataRow("")]
	[DataRow(" ")]
	[Description("Пустой или пробельный путь файла инструкций отклоняется")]
	// Проверяем негативный контракт конструктора: пустой и пробельный путь
	// отклоняются аргумент-исключением до любых чтений файла.
	public void ThrowOnNullOrWhitespacePath(string path)
	{
		// Arrange: источник инструкций без пути файла.

		// Act / Assert: создание поднимает ArgumentException.
		_ = new ConsultationInstructions(path);
	}

	#region Помощники

	/// <summary>Создаёт уникальный временный каталог для проверки.</summary>
	private static string CreateTempDir()
	{
		var dir = Path.Combine(Path.GetTempPath(), $"consultation-instructions-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(dir);
		return dir;
	}

	/// <summary>Удаляет временный каталог со всем содержимым.</summary>
	private static void DeleteDir(string dir)
	{
		if (Directory.Exists(dir))
		{
			Directory.Delete(dir, recursive: true);
		}
	}

	/// <summary>Находит корень решения поиском TransactionJournal.sln вверх по каталогам.</summary>
	private static DirectoryInfo FindSolutionRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && dir.GetFiles("TransactionJournal.sln").Length == 0)
		{
			dir = dir.Parent;
		}

		Assert.That(dir, Is.Not.Null, "Корень решения с TransactionJournal.sln не найден вверх по каталогам.");
		return dir;
	}

	#endregion
}
