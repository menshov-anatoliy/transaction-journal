using System.Net.Http;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using TransactionJournal.Infrastructure.Bybit;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Infrastructure.Bybit;

/// <summary>
/// Проверки конфигурационного поставщика учётных данных Bybit: ключ и секрет
/// читаются из IConfiguration, куда их приносит локальный файл секретов
/// appsettings.Local.json; отсутствие или пустота значений даёт понятную
/// ошибку конфигурации, а local-файл перекрывает системные переменные.
/// </summary>
[TestClass]
public class ConfigurationBybitCredentialsProviderTests
{
	[TestMethod]
	[DataRow("bybit-key-1", "bybit-secret-1")]
	[DataRow("bybit-key-2", "bybit-secret-2")]
	[Description("Ключ и секрет из конфигурации доезжают до GetCredentials и в запрос Bybit")]
	public void TryIfConfiguredApiKeyReachesConsumer(string apiKey, string apiSecret)
	{
		// Arrange: поставщик читает пару ключ/секрет из IConfiguration по ключам
		// Bybit:ApiKey / Bybit:ApiSecret — как их приносит appsettings.Local.json;
		// системные переменные окружения в проверке не участвуют.
		// Traceability: openspec:config/local-secrets#scenario-local-secrets-reach-consumers
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[BybitCredentialsConfig.ApiKeyConfigKey] = apiKey,
				[BybitCredentialsConfig.ApiSecretConfigKey] = apiSecret,
			})
			.Build();
		var provider = new ConfigurationBybitCredentialsProvider(configuration);

		// Act: получаем учётные данные и подставляем ключ в запрос так, как это делает клиент Bybit.
		var credentials = provider.GetCredentials();
		var request = new HttpRequestMessage(
			HttpMethod.Get, "https://api.bybit.com/v5/execution/list?category=linear");
		request.Headers.Add("X-BAPI-API-KEY", credentials.ApiKey);

		// Assert: в заголовок попадает именно ключ из конфигурации; секрет по сети
		// не передаётся и остаётся материалом для HMAC-подписи.
		Assert.That(request.Headers.GetValues("X-BAPI-API-KEY").Single(), Is.EqualTo(apiKey));
		Assert.That(credentials.ApiSecret, Is.EqualTo(apiSecret));
	}

	[TestMethod]
	[DataRow(null, null)]
	[DataRow("", "  ")]
	[DataRow("bybit-key", null)]
	[DataRow(null, "bybit-secret")]
	[Description("Отсутствие или пустота ключа/секрета в конфигурации — ошибка с именем файла и ключа")]
	public void ThrowOnMissingConfigurationCredentials(string? apiKey, string? apiSecret)
	{
		// Arrange: незастроенный local-файл — штатная ситуация, поэтому ошибка
		// конфигурации возникает только у потребителя в момент обращения к секрету.
		// Traceability: openspec:config/local-secrets#scenario-local-secrets-missing-file-normal
		var values = new Dictionary<string, string?>();
		if (apiKey is not null)
		{
			values[BybitCredentialsConfig.ApiKeyConfigKey] = apiKey;
		}

		if (apiSecret is not null)
		{
			values[BybitCredentialsConfig.ApiSecretConfigKey] = apiSecret;
		}

		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(values)
			.Build();
		var provider = new ConfigurationBybitCredentialsProvider(configuration);

		// Act: обращение к незастроенному ключу прерывает работу понятной ошибкой.
		var exception = Assert.Throws<InvalidOperationException>(() => provider.GetCredentials());

		// Assert: сообщение ошибки называет и файл, и конфигурационный ключ —
		// владелец сразу видит, куда вписать значение.
		Assert.That(exception!.Message, Does.Contain(BybitCredentialsConfig.LocalFileName));
		Assert.That(exception.Message, Does.Contain(string.IsNullOrWhiteSpace(apiKey)
			? BybitCredentialsConfig.ApiKeyConfigKey
			: BybitCredentialsConfig.ApiSecretConfigKey));
	}

	[TestMethod]
	[Description("Local-файл перекрывает системную переменную Bybit__ApiKey на композиции провайдеров Program.cs")]
	public void TryIfLocalFileOverridesSystemVariables()
	{
		// Arrange: во временном каталоге создаётся appsettings.Local.json, та же пара
		// задана системными переменными вида Bybit__ApiKey, а конфигурация собрана
		// той же композицией провайдеров, что в Program.cs: env-переменные, затем
		// local-файл последним провайдером.
		// Traceability: openspec:config/local-secrets#scenario-local-secrets-override-system-vars
		var tempDirectory = Path.Combine(
			Path.GetTempPath(), "tj-local-secrets-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(tempDirectory);
		var originalApiKey = Environment.GetEnvironmentVariable("Bybit__ApiKey");
		var originalApiSecret = Environment.GetEnvironmentVariable("Bybit__ApiSecret");
		try
		{
			File.WriteAllText(
				Path.Combine(tempDirectory, BybitCredentialsConfig.LocalFileName),
				$$"""
				{
				  "{{BybitCredentialsConfig.SectionName}}": {
				    "ApiKey": "local-file-key",
				    "ApiSecret": "local-file-secret"
				  }
				}
				""");
			Environment.SetEnvironmentVariable("Bybit__ApiKey", "system-key");
			Environment.SetEnvironmentVariable("Bybit__ApiSecret", "system-secret");

			var configuration = new ConfigurationBuilder()
				.AddEnvironmentVariables()
				.AddJsonFile(Path.Combine(tempDirectory, BybitCredentialsConfig.LocalFileName))
				.Build();
			var provider = new ConfigurationBybitCredentialsProvider(configuration);

			// Act: получаем учётные данные.
			var credentials = provider.GetCredentials();

			// Assert: применяются значения из local-файла, а не системные переменные.
			Assert.That(credentials.ApiKey, Is.EqualTo("local-file-key"));
			Assert.That(credentials.ApiSecret, Is.EqualTo("local-file-secret"));
		}
		finally
		{
			Directory.Delete(tempDirectory, recursive: true);
			Environment.SetEnvironmentVariable("Bybit__ApiKey", originalApiKey);
			Environment.SetEnvironmentVariable("Bybit__ApiSecret", originalApiSecret);
		}
	}
}
