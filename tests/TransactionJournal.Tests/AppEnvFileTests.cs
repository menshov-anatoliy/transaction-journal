using NUnit.Framework;
using TransactionJournal;
using TransactionJournal.Bybit;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests;

/// <summary>
/// Проверки загрузчика локального файла .env: значения файла попадают в переменные
/// процесса и доезжают до поставщика учётных данных Bybit, системные переменные
/// сохраняют приоритет, а отсутствие файла не является ошибкой.
/// </summary>
[TestClass]
public class AppEnvFileTests
{
	/// <summary>Временный каталог с подставным .env, становящийся текущим на время теста.</summary>
	private string? _tempDirectory;

	/// <summary>Исходный текущий каталог процесса для восстановления в Cleanup.</summary>
	private string? _originalDirectory;

	[TestInitialize]
	public void Initialize()
	{
		// Подготовка изолированной среды: чистые переменные, свой временный каталог
		// и запомненный текущий каталог процесса — тесты меняют его переключением.
		ResetEnvironment();
		_originalDirectory = Environment.CurrentDirectory;
		_tempDirectory = Path.Combine(Path.GetTempPath(), "tj-env-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_tempDirectory);
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Восстановление среды в порядке, обратном подготовке: текущий каталог
		// возвращается до удаления временного каталога, затем чистятся переменные.
		if (_originalDirectory is not null)
		{
			Environment.CurrentDirectory = _originalDirectory;
		}

		if (_tempDirectory is not null && Directory.Exists(_tempDirectory))
		{
			Directory.Delete(_tempDirectory, recursive: true);
		}

		ResetEnvironment();
	}

	[TestMethod]
	[Description("Значения из .env становятся переменными процесса и доезжают до поставщика Bybit")]
	public void TryIfEnvFileValuesBecomeProcessVariables()
	{
		// Arrange: во временном каталоге создаётся .env с учётными данными Bybit,
		// в окружении процесса переменные не заданы — значения должны прийти из файла.
		// Traceability: openspec:config/env-file#scenario-env-values-become-process-vars
		File.WriteAllText(Path.Combine(_tempDirectory!, ".env"),
			"BYBIT_API_KEY=env-file-key\nBYBIT_API_SECRET=env-file-secret\n");
		Environment.CurrentDirectory = _tempDirectory!;

		// Act: загрузчик ищет .env от текущего каталога вверх и переносит значения в процесс.
		AppEnvFile.Load();

		// Assert: значения видны как переменные процесса, и через них поставщик
		// отдаёт учётные данные без настройки системных переменных машины.
		Assert.That(Environment.GetEnvironmentVariable("BYBIT_API_KEY"), Is.EqualTo("env-file-key"));
		Assert.That(Environment.GetEnvironmentVariable("BYBIT_API_SECRET"), Is.EqualTo("env-file-secret"));
		var credentials = new EnvironmentBybitCredentialsProvider().GetCredentials();
		Assert.That(credentials.ApiKey, Is.EqualTo("env-file-key"));
		Assert.That(credentials.ApiSecret, Is.EqualTo("env-file-secret"));
	}

	[TestMethod]
	[Description("Системное значение переменной не перезаписывается значением из .env")]
	public void TryIfSystemVariableWinsOverEnvFile()
	{
		// Arrange: ключ заранее задан в окружении, а .env содержит другое значение;
		// секрет в окружении не задан и должен прийти из файла — это доказывает,
		// что файл прочитан, но приоритет отдан системной переменной.
		// Traceability: openspec:config/env-file#scenario-system-var-wins-over-env-file
		Environment.SetEnvironmentVariable(EnvironmentBybitCredentialsProvider.ApiKeyVariableName, "system-key");
		File.WriteAllText(Path.Combine(_tempDirectory!, ".env"),
			"BYBIT_API_KEY=env-file-key\nBYBIT_API_SECRET=env-file-secret\n");
		Environment.CurrentDirectory = _tempDirectory!;

		// Act: загрузчик применяет NoClobber — уже заданные переменные не трогает.
		AppEnvFile.Load();

		// Assert: ключ остался системным, значение из файла для него проигнорировано.
		Assert.That(Environment.GetEnvironmentVariable(EnvironmentBybitCredentialsProvider.ApiKeyVariableName), Is.EqualTo("system-key"));
		Assert.That(Environment.GetEnvironmentVariable(EnvironmentBybitCredentialsProvider.ApiSecretVariableName), Is.EqualTo("env-file-secret"));
	}

	[TestMethod]
	[Description("Отсутствие .env не является ошибкой и не меняет переменные окружения")]
	public void TryIfMissingEnvFileKeepsVariablesUntouched()
	{
		// Arrange: временный каталог пуст, вверх по дереву от него .env нет —
		// переменные процесса остаются такими, какими их задало окружение.
		// Traceability: openspec:config/env-file#scenario-missing-env-file-is-normal
		Environment.CurrentDirectory = _tempDirectory!;

		// Act: загрузчик отрабатывает на отсутствующем файле без исключений.
		AppEnvFile.Load();

		// Assert: переменные не изменились и остались незаданными.
		Assert.That(Environment.GetEnvironmentVariable(EnvironmentBybitCredentialsProvider.ApiKeyVariableName), Is.Null);
		Assert.That(Environment.GetEnvironmentVariable(EnvironmentBybitCredentialsProvider.ApiSecretVariableName), Is.Null);
	}

	#region Помощники

	private static void ResetEnvironment()
	{
		// Чистим переменные, чтобы проверки не зависели от настроек машины
		// разработчика и от значений, оставшихся от предыдущих тестов.
		Environment.SetEnvironmentVariable(EnvironmentBybitCredentialsProvider.ApiKeyVariableName, null);
		Environment.SetEnvironmentVariable(EnvironmentBybitCredentialsProvider.ApiSecretVariableName, null);
	}

	#endregion
}
