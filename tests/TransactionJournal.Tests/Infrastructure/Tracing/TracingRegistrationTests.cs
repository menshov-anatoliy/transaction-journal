namespace TransactionJournal.Tests.Infrastructure.Tracing;

using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using OpenTelemetry.Trace;
using TransactionJournal.Infrastructure.Tracing;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Проверки регистрации трассировки в composition root: проводка
/// AddAgentTracing в живом процессе не теряет трассу агента внутри
/// HTTP-запроса ASP.NET Core — родителем спанов там выступает
/// несэмплированная активность HttpRequestIn, и дефолтный сэмплер SDK
/// ParentBased(AlwaysOn) отбрасывал бы корневые спаны хода чата и прохода
/// подсказок целиком. Юнит-тесты с фейковым приёмником этот разрыв не
/// ловят: у них ambient-контекст пуст, поэтому проводка проверяется
/// отдельной регистрацией с настоящим провайдером SDK.
/// Traceability: openspec:ops/agent-tracing#requirement-trace-otel-two-surfaces
/// </summary>
[TestClass]
public class TracingRegistrationTests
{
	/// <summary>Каталог трасс текущей проверки.</summary>
	private string _tracesDirectory = null!;

	[TestInitialize]
	public void InitializeTracesDirectory()
	{
		// Каждая проверка работает со своим пустым каталогом трасс.
		_tracesDirectory = Path.Combine(Path.GetTempPath(), $"tracing-registration-tests-{Guid.NewGuid():N}");
	}

	[TestCleanup]
	public void CleanupTracesDirectory()
	{
		// Провайдер SDK закрывает поток своим Dispose; страховка удаления не мешает.
		try
		{
			if (Directory.Exists(_tracesDirectory))
			{
				Directory.Delete(_tracesDirectory, recursive: true);
			}
		}
		catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
		{
			// Занятый каталог не является исходом проверки — следующая проверка
			// получает собственный каталог.
		}
	}

	[TestMethod]
	[@Description("Спан агента пишется под несэмплированной активностью HTTP-запроса — всегда, без сэмплирования")]
	// Регрессия живого прогона: в HTTP-запросе Activity.Current —
	// несэмплированная HttpRequestIn, ParentBased-сэмплер по умолчанию гасил
	// всю трассу агента. Проводка AddAgentTracing обязана писать спан и при
	// таком родителе: без сэмплирования, согласно требованию.
	// Traceability: openspec:ops/agent-tracing#requirement-trace-otel-two-surfaces
	public void TryIfAgentSpanIsRecordedUnderUnsampledHttpRequestParent()
	{
		// Arrange: настоящая проводка AddAgentTracing и провайдер SDK из DI.
		var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
		var services = new ServiceCollection();
		services.AddAgentTracing(configuration, _tracesDirectory);
		using var provider = services.BuildServiceProvider();
		using var tracerProvider = provider.GetRequiredService<TracerProvider>();

		// Несэмплированный родитель — имитация HttpRequestIn без слушателя:
		// recorded = false, как у активности HTTP-запроса ASP.NET Core.
		var parent = new Activity("HttpRequestIn-imitation");
		parent.SetParentId(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.None);
		parent.Start();
		try
		{
			Assert.That(parent.Recorded, Is.False, "имитация родителя должна быть несэмплированной");

			// Act: корневой спан прохода стартует в контексте такого родителя.
			using var source = new ActivitySource("TransactionJournal.Hints");
			var span = source.StartActivity("hint_pass", ActivityKind.Internal);

			// Assert: спан существует и записан, несмотря на несэмплированного
			// родителя; связь с родителем сохранена контекстом W3C.
			Assert.That(span, Is.Not.Null);
			Assert.That(span!.Recorded, Is.True);
			Assert.That(span.TraceId, Is.EqualTo(parent.TraceId));
			span.Stop();
		}
		finally
		{
			parent.Stop();
		}

		// Assert: принудительный слив пакета кладёт строку спана в файл трасс;
		// относительный каталог трасс разрешается от корня содержимого.
		Assert.That(tracerProvider.ForceFlush(), Is.True);
		var file = Path.Combine(_tracesDirectory, "App_Data", "traces", "traces.jsonl");
		Assert.That(File.Exists(file), Is.True, "файл трасс обязан появиться после ForceFlush");

		// Экспортёр держит файл открытым — читаем с общим доступом на запись.
		using var shared = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new StreamReader(shared);
		var lines = new List<string>();
		while (reader.ReadLine() is { } line)
		{
			lines.Add(line);
		}

		Assert.That(lines.Where(line => line.Contains("hint_pass", StringComparison.Ordinal)), Is.Not.Empty);
	}
}
