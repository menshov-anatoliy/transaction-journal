namespace TransactionJournal.Tests.Infrastructure.Tracing;

using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Infrastructure.Tracing;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Проверки чтения секции Tracing журнала трасс агентского харнеса: отсутствие
/// секции даёт дефолты кода (detailed, App_Data/traces, 10 МБ, 5 архивов, OTLP
/// выключен), заданные значения всех ключей проходят в опции без правки кода,
/// регистр уровня детализации игнорируется, а неизвестное значение отвергается
/// исключением — молчаливая подмена уровня скрыла бы потерю событий аргументов.
/// Traceability: openspec:config/tracing#requirement-tracing-config-section
/// Traceability: openspec:config/tracing#scenario-tracing-config-defaults
/// Traceability: openspec:config/tracing#scenario-tracing-config-switch-without-code
/// </summary>
[TestClass]
public class TracingOptionsReaderTests
{
	[TestMethod]
	[Description("Отсутствие секции Tracing даёт опциям журнала трасс дефолты кода")]
	// Сценарий «Дефолты без секции»: приложение по умолчанию пишет трассы в
	// App_Data/traces на уровне detailed, ротация при 10 МБ, 5 архивов, OTLP выключен.
	// Traceability: openspec:config/tracing#scenario-tracing-config-defaults
	public void TryIfMissingTracingSectionFallsBackToDefaults()
	{
		// Arrange: конфигурация без секции Tracing — обычный случай приложения.
		var configuration = new ConfigurationBuilder().Build();

		// Act
		var options = TracingOptionsReader.Read(configuration);

		// Assert: все параметры приняли дефолты, приёмник OTLP выключен.
		Assert.That(options.Detail, Is.EqualTo(TracingDetail.Detailed));
		Assert.That(options.Dir, Is.EqualTo("App_Data/traces"));
		Assert.That(options.MaxFileBytes, Is.EqualTo(10_485_760L));
		Assert.That(options.KeepFiles, Is.EqualTo(5));
		Assert.That(options.OtlpEndpoint, Is.Null);
	}

	[TestMethod]
	[Description("Заданные значения всех ключей секции Tracing проходят в опции")]
	// Сценарий «Смена уровня и лимитов без кода»: правка конфигурации меняет
	// уровень детализации, каталог и лимиты ротации без правки кода.
	// Traceability: openspec:config/tracing#scenario-tracing-config-switch-without-code
	public void TryIfConfiguredValuesReachOptions()
	{
		// Arrange: все пять ключей секции заданы явно.
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[TracingOptionsReader.DetailKey] = "basic",
				[TracingOptionsReader.DirectoryKey] = "App_Data/custom-traces",
				[TracingOptionsReader.MaxFileBytesKey] = "1048576",
				[TracingOptionsReader.KeepFilesKey] = "3",
				[TracingOptionsReader.OtlpEndpointKey] = "http://localhost:4317",
			})
			.Build();

		// Act
		var options = TracingOptionsReader.Read(configuration);

		// Assert: каждое значение конфигурации попало в соответствующее свойство.
		Assert.That(options.Detail, Is.EqualTo(TracingDetail.Basic));
		Assert.That(options.Dir, Is.EqualTo("App_Data/custom-traces"));
		Assert.That(options.MaxFileBytes, Is.EqualTo(1_048_576L));
		Assert.That(options.KeepFiles, Is.EqualTo(3));
		Assert.That(options.OtlpEndpoint, Is.EqualTo("http://localhost:4317"));
	}

	[TestMethod]
	[DataRow("basic", TracingDetail.Basic)]
	[DataRow("BASIC", TracingDetail.Basic)]
	[DataRow("Detailed", TracingDetail.Detailed)]
	[DataRow("DETAILED", TracingDetail.Detailed)]
	[DataRow(" detailed ", TracingDetail.Detailed)]
	[Description("Уровень детализации читается без учёта регистра и окружающих пробелов")]
	public void TryIfDetailParsingIgnoresCaseAndSpaces(string raw, TracingDetail expected)
	{
		// Arrange: значение уровня задано в произвольном регистре.
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[TracingOptionsReader.DetailKey] = raw,
			})
			.Build();

		// Act
		var options = TracingOptionsReader.Read(configuration);

		// Assert: регистр и пробелы не мешают распознаванию уровня.
		Assert.That(options.Detail, Is.EqualTo(expected), raw);
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("")]
	[DataRow("   ")]
	[Description("Пустое значение уровня детализации откатывается к detailed")]
	// Пустой ключ не считается заданным уровнем: работает дефолт detailed.
	public void TryIfEmptyDetailFallsBackToDetailed(string? raw)
	{
		// Arrange: ключ уровня присутствует, но пустой.
		var values = new Dictionary<string, string?>();
		if (raw is not null)
		{
			values[TracingOptionsReader.DetailKey] = raw;
		}

		var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

		// Act
		var options = TracingOptionsReader.Read(configuration);

		// Assert: дефолтный уровень — detailed.
		Assert.That(options.Detail, Is.EqualTo(TracingDetail.Detailed));
	}

	[TestMethod]
	[DataRow("verbose")]
	[DataRow("podrobno")]
	[DataRow("2")]
	[Description("Неизвестное значение уровня детализации отвергается исключением")]
	// Отрицательный сценарий: молчаливая подмена неизвестного уровня скрыла бы
	// потерю событий аргументов тулов, поэтому читатель обязан отказать.
	public void ThrowOnUnknownDetailValue(string raw)
	{
		// Arrange: уровень задан строкой вне допустимого набора.
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[TracingOptionsReader.DetailKey] = raw,
			})
			.Build();

		// Act
		var failure = Assert.Throws<InvalidOperationException>(() => TracingOptionsReader.Read(configuration));

		// Assert: сообщение называет ключ и допустимые значения.
		Assert.That(failure, Is.Not.Null);
		Assert.That(failure!.Message, Does.Contain(TracingOptionsReader.DetailKey));
		Assert.That(failure.Message, Does.Contain("basic"));
		Assert.That(failure.Message, Does.Contain("detailed"));
	}

	[TestMethod]
	[DataRow("abc", "abc")]
	[DataRow("0", "-1")]
	[DataRow("-10", "-7")]
	[Description("Некорректные лимиты ротации откатываются к дефолтам кода")]
	public void TryIfInvalidLimitsFallBackToDefaults(string rawMaxFileBytes, string rawKeepFiles)
	{
		// Arrange: предел размера нечисловой, нулевой или отрицательный, число
		// архивов нечисловое или отрицательное.
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[TracingOptionsReader.MaxFileBytesKey] = rawMaxFileBytes,
				[TracingOptionsReader.KeepFilesKey] = rawKeepFiles,
			})
			.Build();

		// Act
		var options = TracingOptionsReader.Read(configuration);

		// Assert: неверные лимиты не способны отключить ротацию — работают дефолты.
		Assert.That(options.MaxFileBytes, Is.EqualTo(TracingOptionsReader.DefaultMaxFileBytes), rawMaxFileBytes);
		Assert.That(options.KeepFiles, Is.EqualTo(TracingOptionsReader.DefaultKeepFiles), rawKeepFiles);
	}

	[TestMethod]
	[DataRow("0")]
	[Description("KeepFiles=0 — допустимая конфигурация без хранения архивов")]
	// Ноль архивов — осмысленный выбор: ротация происходит, но архивы не
	// накапливаются; ноль не откатывается к дефолту.
	public void TryIfZeroKeepFilesIsAccepted(string rawKeepFiles)
	{
		// Arrange: число хранимых архивов явно нулевое.
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[TracingOptionsReader.KeepFilesKey] = rawKeepFiles,
			})
			.Build();

		// Act
		var options = TracingOptionsReader.Read(configuration);

		// Assert: ноль принят как «архивы не хранить».
		Assert.That(options.KeepFiles, Is.EqualTo(0));
	}
}
