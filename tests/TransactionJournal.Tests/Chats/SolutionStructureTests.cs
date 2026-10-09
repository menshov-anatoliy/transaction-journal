namespace TransactionJournal.Tests.Chats;

using System.Xml.Linq;

/// <summary>
/// Структурные тесты проекта окружения Chats: состав проектов решения и
/// направление зависимостей проверяются по файлам решения, без сборки.
/// </summary>
[TestClass]
public class SolutionStructureTests
{
	/// <summary>Относительный путь проекта Chats в решении.</summary>
	private const string ChatsProjectPath = @"src\TransactionJournal.Chats\TransactionJournal.Chats.csproj";

	/// <summary>Относительный путь проекта Hints в решении.</summary>
	private const string HintsProjectPath = @"src\TransactionJournal.Hints\TransactionJournal.Hints.csproj";

	/// <summary>Относительный путь проекта Domain в решении.</summary>
	private const string DomainProjectPath = @"src\TransactionJournal.Domain\TransactionJournal.Domain.csproj";

	/// <summary>
	/// Полный состав проектов решения: пять базовых проектов DDD-слоёв
	/// (Domain, Application, Infrastructure, Web и Tests) плюс проекты
	/// окружений Hints и Chats.
	/// </summary>
	private static readonly string[] SolutionProjects =
	[
		DomainProjectPath,
		@"src\TransactionJournal.Application\TransactionJournal.Application.csproj",
		@"src\TransactionJournal.Infrastructure\TransactionJournal.Infrastructure.csproj",
		@"src\TransactionJournal\TransactionJournal.csproj",
		HintsProjectPath,
		ChatsProjectPath,
		@"tests\TransactionJournal.Tests\TransactionJournal.Tests.csproj",
	];

	[TestMethod]
	[Description("Решение состоит из проектов DDD-слоёв и окружений, включая Chats")]
	// Состав решения фиксирован: пять базовых проектов DDD-слоёв и два проекта
	// окружений; чат агента живёт в собственном проекте, а не в слоях.
	// Traceability: openspec:architecture/solution-structure#requirement-solution-five-projects
	public void SolutionConsistsOfLayerAndEnvironmentProjects()
	{
		var solutionText = File.ReadAllText(Path.Combine(FindSolutionRoot().FullName, "TransactionJournal.sln"));

		var actualProjects = ReadSolutionProjects(solutionText);

		CollectionAssert.AreEquivalent(
			SolutionProjects,
			actualProjects,
			"Состав проектов решения изменился: ожидались базовые проекты DDD-слоёв плюс окружения Hints и Chats.");
	}

	[TestMethod]
	[Description("Проект Chats ссылается только на домен")]
	// Окружение чата агента зависит только от домена: порты объявлены
	// в самом проекте, адаптеры живут в Infrastructure и composition root.
	// Traceability: openspec:architecture/solution-structure#requirement-dependencies-point-inward
	public void ChatsReferencesOnlyDomain()
	{
		var references = ReadProjectReferences(Path.Combine(FindSolutionRoot().FullName, ChatsProjectPath));

		CollectionAssert.AreEquivalent(
			new[] { @"..\TransactionJournal.Domain\TransactionJournal.Domain.csproj" },
			references,
			"Проект Chats должен ссылаться только на TransactionJournal.Domain.");
	}

	[TestMethod]
	[Description("Домен не ссылается на проект чатов")]
	// Чат — запись окружения: домен журнала не знает ни о проекте Chats,
	// ни о его типах; направление зависимости только внутрь слоёв.
	// Traceability: openspec:chats/history#requirement-chat-environment-record
	// Traceability: openspec:chats/history#scenario-chat-domain-agnostic
	public void DomainDoesNotReferenceChats()
	{
		var references = ReadProjectReferences(Path.Combine(FindSolutionRoot().FullName, DomainProjectPath));

		CollectionAssert.DoesNotContain(
			references,
			@"..\TransactionJournal.Chats\TransactionJournal.Chats.csproj",
			"Домен не должен ссылаться на проект чатов: чат — запись окружения.");
	}

	[TestMethod]
	[Description("Окружения не связаны между собой: Chats не ссылается на Hints и наоборот")]
	// Доступ к корпусу правил идёт через собственный порт Chats с
	// адаптером в composition root — прямой связи проектов окружений нет.
	// Traceability: openspec:architecture/solution-structure#scenario-environments-not-linked
	public void EnvironmentProjectsAreNotLinked()
	{
		var root = FindSolutionRoot().FullName;
		var chatsReferences = ReadProjectReferences(Path.Combine(root, ChatsProjectPath));
		var hintsReferences = ReadProjectReferences(Path.Combine(root, HintsProjectPath));

		CollectionAssert.DoesNotContain(
			chatsReferences,
			HintsProjectPath,
			"Chats не должен ссылаться на Hints: корпус подключается собственным портом.");
		CollectionAssert.DoesNotContain(
			hintsReferences,
			ChatsProjectPath,
			"Hints не должен ссылаться на Chats: окружения независимы друг от друга.");
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

	/// <summary>Читает пути csproj-проектов из файла решения.</summary>
	private static List<string> ReadSolutionProjects(string solutionText) =>
		solutionText
			.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
			.Where(line => line.StartsWith("Project(", StringComparison.Ordinal) && line.Contains(".csproj"))
			.Select(line => line.Split(',')[1].Trim().Trim('"').Replace('/', '\\'))
			.ToList();

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
