namespace TransactionJournal.Tests.Consultations;

using System.Xml.Linq;

/// <summary>
/// Структурные тесты проекта окружения Consultations: состав ссылок solution
/// и направление зависимостей проверяются по файлам решения, без сборки.
/// </summary>
[TestClass]
public class SolutionStructureTests
{
	/// <summary>Относительный путь проекта Consultations в решении.</summary>
	private const string ConsultationsProjectPath = @"src\TransactionJournal.Consultations\TransactionJournal.Consultations.csproj";

	/// <summary>Относительный путь проекта Hints в решении.</summary>
	private const string HintsProjectPath = @"src\TransactionJournal.Hints\TransactionJournal.Hints.csproj";

	[TestMethod]
	[Description("Проект Consultations включён в решение")]
	public void SolutionIncludesConsultationsProject()
	{
		var solutionText = File.ReadAllText(Path.Combine(FindSolutionRoot().FullName, "TransactionJournal.sln"));

		StringAssert.Contains(solutionText, @"src\TransactionJournal.Consultations\TransactionJournal.Consultations.csproj", "Проект Consultations отсутствует в файле решения.");
	}

	[TestMethod]
	[Description("Проект Consultations ссылается только на домен")]
	// Окружение чата консультаций зависит только от домена: порты объявлены
	// в самом проекте, адаптеры живут в Infrastructure и composition root.
	// Traceability: openspec:architecture/solution-structure#scenario-consultations-own-environment-project
	public void ConsultationsReferencesOnlyDomain()
	{
		var references = ReadProjectReferences(Path.Combine(FindSolutionRoot().FullName, ConsultationsProjectPath));

		CollectionAssert.AreEquivalent(
			new[] { @"..\TransactionJournal.Domain\TransactionJournal.Domain.csproj" },
			references,
			"Проект Consultations должен ссылаться только на TransactionJournal.Domain.");
	}

	[TestMethod]
	[Description("Окружения не связаны между собой: Consultations не ссылается на Hints и наоборот")]
	// Доступ к корпусу правил идёт через собственный порт Consultations с
	// адаптером в composition root — прямой связи проектов окружений нет.
	// Traceability: openspec:architecture/solution-structure#scenario-environments-not-linked
	public void EnvironmentProjectsAreNotLinked()
	{
		var root = FindSolutionRoot().FullName;
		var consultationsReferences = ReadProjectReferences(Path.Combine(root, ConsultationsProjectPath));
		var hintsReferences = ReadProjectReferences(Path.Combine(root, HintsProjectPath));

		CollectionAssert.DoesNotContain(
			consultationsReferences,
			HintsProjectPath,
			"Consultations не должен ссылаться на Hints: корпус подключается собственным портом.");
		CollectionAssert.DoesNotContain(
			hintsReferences,
			ConsultationsProjectPath,
			"Hints не должен ссылаться на Consultations: окружения независимы друг от друга.");
	}

	/// <summary>Находит корень решения поиском TransactionJournal.sln вверх по каталогам.</summary>
	private static DirectoryInfo FindSolutionRoot()
	{
		var dir = new DirectoryInfo(AppContext.BaseDirectory);
		while (dir != null && dir.GetFiles("TransactionJournal.sln").Length == 0)
		{
			dir = dir.Parent;
		}

		Assert.IsNotNull(dir, "Корень решения с TransactionJournal.sln не найден вверх по каталогам.");
		return dir;
	}

	/// <summary>Читает Include всех ProjectReference из файла проекта.</summary>
	private static List<string> ReadProjectReferences(string projectPath)
	{
		var project = XDocument.Load(projectPath);
		return project
			.Descendants("ProjectReference")
			.Select(element => element.Attribute("Include")?.Value)
			.Where(value => value != null)
			.Select(value => value!.Replace('/', '\\'))
			.ToList();
	}
}
