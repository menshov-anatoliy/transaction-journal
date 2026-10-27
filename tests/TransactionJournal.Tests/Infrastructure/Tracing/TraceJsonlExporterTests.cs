namespace TransactionJournal.Tests.Infrastructure.Tracing;

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using OpenTelemetry;
using TransactionJournal.Infrastructure.Tracing;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Проверки локального приёмника трасс <see cref="TraceJsonlExporter"/>: строка
/// JSONL самодостаточна для grep (поле service в каждой строке), имена атрибутов
/// сохраняются оригинальными — с точками gen_ai.* и tj.*, события пишутся
/// массивом объектов {ts, name, attrs}; ротация по размеру создаёт архивы с
/// однозначной меткой, хранит лимит архивов и продолжает запись; сбой записи
/// деградирует в диагностику и не бросает исключений наружу.
/// Traceability: openspec:ops/agent-tracing#requirement-trace-jsonl-sink
/// </summary>
[TestClass]
public class TraceJsonlExporterTests
{
	/// <summary>Тестовый источник спанов: имя проверяется в поле source строки.</summary>
	private static readonly ActivitySource TestSource = new("TransactionJournal.Tests.Tracing");

	/// <summary>Каталог трасс текущей проверки.</summary>
	private string _tracesDirectory = null!;

	[TestInitialize]
	public void InitializeTracesDirectory()
	{
		// Каждая проверка работает со своим пустым каталогом трасс.
		_tracesDirectory = Path.Combine(Path.GetTempPath(), $"tracing-tests-{Guid.NewGuid():N}");
	}

	[TestCleanup]
	public void CleanupTracesDirectory()
	{
		// Экспортёр закрывает поток своим Dispose, но страховка удаления не мешает.
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
	[Description("Строка трассы самодостаточна: все поля на месте, имена атрибутов оригинальные")]
	// Сценарий «Строка самодостаточна для grep»: одна строка несёт все поля спана,
	// включая service; имена атрибутов с точками gen_ai.* и tj.* сохраняются,
	// события пишутся массивом объектов {ts, name, attrs}, у корневого спана
	// parent_span_id — null.
	// Traceability: openspec:ops/agent-tracing#scenario-trace-jsonl-line-greppable
	public void TryIfSpanLineIsGreppableWithAllFields()
	{
		// Arrange: корневой спан с доменными, gen_ai-атрибутами и событием тулов.
		using var exporter = CreateExporter();
		var activity = CreateActivity("chat_turn", configure: span =>
		{
			span.SetTag("tj.chat.id", 42L);
			span.SetTag("gen_ai.response.model", "glm-5.3");
			span.SetTag("gen_ai.usage.input_tokens", 15);
			span.AddEvent(new ActivityEvent(
				"tj.tool.args",
				DateTimeOffset.UtcNow,
				new ActivityTagsCollection { ["json"] = "{\"constructionId\":7}" }));
		});

		// Act
		exporter.Export(new Batch<Activity>(activity));

		// Assert: разобранная строка содержит все поля в ожидаемом виде.
		var line = ReadSingleLine();
		using var document = JsonDocument.Parse(line);
		var root = document.RootElement;
		Assert.That(root.GetProperty("service").GetString(), Is.EqualTo("transaction-journal"));
		Assert.That(root.GetProperty("source").GetString(), Is.EqualTo(TestSource.Name));
		Assert.That(root.GetProperty("name").GetString(), Is.EqualTo("chat_turn"));
		Assert.That(root.GetProperty("kind").GetString(), Is.EqualTo("Internal"));
		Assert.That(root.GetProperty("trace_id").GetString(), Does.Match("^[0-9a-f]{32}$"));
		Assert.That(root.GetProperty("span_id").GetString(), Does.Match("^[0-9a-f]{16}$"));
		Assert.That(root.GetProperty("parent_span_id").ValueKind, Is.EqualTo(JsonValueKind.Null));
		Assert.That(root.GetProperty("duration_ms").ValueKind, Is.EqualTo(JsonValueKind.Number));
		Assert.That(root.GetProperty("status").GetString(), Is.EqualTo("unset"));
		Assert.That(root.GetProperty("status_desc").ValueKind, Is.EqualTo(JsonValueKind.Null));
		Assert.That(
			DateTimeOffset.TryParse(
				root.GetProperty("ts").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
			Is.True);

		// Имена атрибутов оригинальные — точки в gen_ai.* и tj.* не теряются.
		var attrs = root.GetProperty("attrs");
		Assert.That(attrs.GetProperty("tj.chat.id").GetInt64(), Is.EqualTo(42));
		Assert.That(attrs.GetProperty("gen_ai.response.model").GetString(), Is.EqualTo("glm-5.3"));
		Assert.That(attrs.GetProperty("gen_ai.usage.input_tokens").GetInt32(), Is.EqualTo(15));

		// Событие — объект {ts, name, attrs}.
		var events = root.GetProperty("events");
		Assert.That(events.GetArrayLength(), Is.EqualTo(1));
		var toolArgs = events[0];
		Assert.That(toolArgs.GetProperty("name").GetString(), Is.EqualTo("tj.tool.args"));
		Assert.That(toolArgs.GetProperty("attrs").GetProperty("json").GetString(), Is.EqualTo("{\"constructionId\":7}"));
		Assert.That(toolArgs.GetProperty("ts").GetString(), Does.Contain("T"));

		// Поле service присутствует в сырой строке — поиск по trace id самодостаточен.
		Assert.That(line, Does.Contain("\"service\":\"transaction-journal\""));
	}

	[TestMethod]
	[Description("Каждая строка несёт service, дочерний спан ссылается на родителя")]
	// Grep-самодостаточность проверяется на нескольких строках: service есть в
	// каждой; дочерний спан трассы ссылается на родителя и общий trace id.
	// Traceability: openspec:ops/agent-tracing#scenario-trace-jsonl-line-greppable
	public void TryIfEveryLineCarriesServiceAndChildReferencesParent()
	{
		// Arrange: корневой спан прохода и дочерний спан фазы котировок.
		using var exporter = CreateExporter();
		var parent = CreateActivity("hint_pass");
		var child = CreateActivity("quotes", parent: parent, kind: ActivityKind.Client);

		// Act: каждый спан уходит отдельной партией.
		exporter.Export(new Batch<Activity>(parent));
		exporter.Export(new Batch<Activity>(child));

		// Assert: обе строки самодостаточны, связь родитель-потомок сохранена.
		var lines = ReadLines();
		Assert.That(lines, Has.Length.EqualTo(2));
		Assert.That(lines, Has.All.Contains("\"service\":\"transaction-journal\""));

		var documents = lines
			.Select(line => JsonDocument.Parse(line))
			.Select(document => document.RootElement)
			.ToList();
		var parentElement = documents.Single(element => element.GetProperty("span_id").GetString() == parent.SpanId.ToString());
		var childElement = documents.Single(element => element.GetProperty("span_id").GetString() == child.SpanId.ToString());

		Assert.That(parentElement.GetProperty("parent_span_id").ValueKind, Is.EqualTo(JsonValueKind.Null));
		Assert.That(childElement.GetProperty("parent_span_id").GetString(), Is.EqualTo(parent.SpanId.ToString()));
		Assert.That(childElement.GetProperty("trace_id").GetString(), Is.EqualTo(parent.TraceId.ToString()));
		Assert.That(childElement.GetProperty("kind").GetString(), Is.EqualTo("Client"));
	}

	[TestMethod]
	[Description("Ротация создаёт архив, хранит лимит архивов и продолжает запись")]
	// Сценарий «Ротация сохраняет архивы и продолжает запись»: при превышении
	// предела файл переименовывается в traces-{timestamp}.jsonl, запись
	// продолжается в новый traces.jsonl, архивов остаётся не более KeepFiles.
	// Traceability: openspec:ops/agent-tracing#scenario-trace-jsonl-rotation
	public void TryIfRotationCreatesArchiveAndContinuesWriting()
	{
		// Arrange: крошечный предел размера (300 байт) и лимит два архива —
		// четыре спана порождают три ротации с удалением старейшего архива.
		using var exporter = CreateExporter(maxFileBytes: 300, keepFiles: 2);
		var activities = Enumerable
			.Range(1, 4)
			.Select(index => CreateActivity($"span-{index}"))
			.ToList();
		var traceIds = activities.Select(activity => activity.TraceId.ToString()).ToList();

		// Act: каждый спан уходит отдельной партией.
		foreach (var activity in activities)
		{
			exporter.Export(new Batch<Activity>(activity));
		}

		// Assert: ровно два архива, новый файл содержит только последний спан.
		var archives = Directory.GetFiles(_tracesDirectory, TraceJsonlExporter.ArchiveSearchPattern);
		Assert.That(archives, Has.Length.EqualTo(2));

		var mainLines = ReadLines();
		Assert.That(mainLines, Has.Length.EqualTo(1));
		Assert.That(mainLines[0], Does.Contain(traceIds[3]));

		// Старейший архив удалён лимитом хранения, остальные два спана — в архивах.
		var archiveLines = archives
			.SelectMany(File.ReadAllLines)
			.Where(line => line.Length > 0)
			.ToList();
		Assert.That(archiveLines, Has.Count.EqualTo(2));
		Assert.That(archiveLines.Any(line => line.Contains(traceIds[0])), Is.False);
		Assert.That(archiveLines.Any(line => line.Contains(traceIds[1])), Is.True);
		Assert.That(archiveLines.Any(line => line.Contains(traceIds[2])), Is.True);

		// Имя архива несёт однозначную метку времени.
		Assert.That(archives, Has.All.Matches<string>(path =>
			Regex.IsMatch(Path.GetFileName(path), @"^traces-\d{8}-\d{6}-\d{3}(\-\d+)?\.jsonl$")));
	}

	[TestMethod]
	[Description("Сбой записи деградирует в диагностику и не ломает экспорт")]
	// Сценарий «Сбой записи не ломает агента»: путь каталога занят существующим
	// файлом, Export не бросает исключений, сбой фиксируется диагностикой,
	// повторная партия подтверждает работоспособность экспортёра.
	// Traceability: openspec:ops/agent-tracing#scenario-trace-jsonl-write-failure-degrades
	public void TryIfWriteFailureDegradesToDiagnosticAndStaysOperational()
	{
		// Arrange: путь каталога занят существующим файлом — запись обречена.
		var blockerPath = Path.Combine(Path.GetTempPath(), $"tracing-tests-blocker-{Guid.NewGuid():N}");
		File.WriteAllText(blockerPath, "препятствие на пути каталога трасс");
		using var exporter = new TraceJsonlExporter(
			new TracingOptions { Dir = blockerPath }, "transaction-journal");
		var activity = CreateActivity("chat_turn");
		var listener = new CollectingTraceListener();
		Trace.Listeners.Add(listener);
		try
		{
			// Act: две партии подряд — вторая идёт после сбоя первой.
			exporter.Export(new Batch<Activity>(activity));
			exporter.Export(new Batch<Activity>(activity));

			// Assert: исключений нет (выполнение дошло сюда), сбой виден в диагностике.
			Assert.That(
				string.Join(Environment.NewLine, listener.Messages),
				Does.Contain("Не удалось записать строку трассы"));
		}
		finally
		{
			Trace.Listeners.Remove(listener);
			listener.Dispose();
			File.Delete(blockerPath);
		}
	}

	#region Вспомогательные методы

	/// <summary>Экспортёр над каталогом трасс текущей проверки с заданными лимитами.</summary>
	private TraceJsonlExporter CreateExporter(long maxFileBytes = 10_485_760, int keepFiles = 5) =>
		new(new TracingOptions { Dir = _tracesDirectory, MaxFileBytes = maxFileBytes, KeepFiles = keepFiles }, "transaction-journal");

	/// <summary>Путь основного файла трасс текущей проверки.</summary>
	private string MainFilePath => Path.Combine(_tracesDirectory, TraceJsonlExporter.MainFileName);

	/// <summary>Читает единственную непустую строку файла трасс.</summary>
	private string ReadSingleLine()
	{
		var lines = ReadLines();
		Assert.That(lines, Has.Length.EqualTo(1));
		return lines[0];
	}

	/// <summary>Читает непустые строки основного файла трасс.</summary>
	private string[] ReadLines()
	{
		// Файл удерживается экспортёром на запись: читаем с общим доступом,
		// как это делает grep по живому журналу трасс.
		using var stream = new FileStream(MainFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
		using var reader = new StreamReader(stream);
		var lines = new List<string>();
		while (reader.ReadLine() is { } line)
		{
			if (line.Length > 0)
			{
				lines.Add(line);
			}
		}

		return [.. lines];
	}

	/// <summary>
	/// Создаёт завершённый спан тестового источника: слушатель ActivitySource
	/// подключается на время создания, чтобы StartActivity вернул активность.
	/// </summary>
	private static Activity CreateActivity(
		string name,
		Action<Activity>? configure = null,
		Activity? parent = null,
		ActivityKind kind = ActivityKind.Internal)
	{
		using var listener = new ActivityListener
		{
			ShouldListenTo = _ => true,
			Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
		};
		ActivitySource.AddActivityListener(listener);

		var activity = parent is null
			? TestSource.StartActivity(name, kind)
			: TestSource.StartActivity(
				name, kind, new ActivityContext(parent.TraceId, parent.SpanId, ActivityTraceFlags.Recorded));
		Assert.That(activity, Is.Not.Null);
		configure?.Invoke(activity!);
		activity!.Stop();
		return activity;
	}

	#endregion

	/// <summary>Слушатель диагностики, собирающий сообщения для проверок сбоя записи.</summary>
	private sealed class CollectingTraceListener : TraceListener
	{
		/// <summary>Собранные сообщения диагностики.</summary>
		public List<string> Messages { get; } = [];

		public override void Write(string? message)
		{
			if (message is not null)
			{
				Messages.Add(message);
			}
		}

		public override void WriteLine(string? message) => Write(message);
	}
}
