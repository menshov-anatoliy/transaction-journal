using System.Net.Http;
using NUnit.Framework;
using TransactionJournal.Bybit;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Bybit;

/// <summary>
/// Проверки dev-поставщика учётных данных Bybit: ключ и секрет читаются из
/// переменных окружения, ключ подставляется в подписываемый запрос, а отсутствие
/// переменных даёт понятную ошибку конфигурации.
/// </summary>
[TestClass]
public class EnvironmentBybitCredentialsProviderTests
{
	[TestInitialize]
	public void Initialize()
	{
		ResetEnvironment();
	}

	[TestCleanup]
	public void Cleanup()
	{
		ResetEnvironment();
	}

	[TestMethod]
	[DataRow("bybit-key-1", "bybit-secret-1")]
	[DataRow("bybit-key-2", "bybit-secret-2")]
	[Description("Ключ из переменных окружения подставляется в заголовок запроса Bybit")]
	public void TryIfEnvironmentApiKeyIsSubstitutedIntoRequest(string apiKey, string apiSecret)
	{
		// Arrange: dev-поставщик читает пару ключ/секрет из переменных окружения —
		// временное решение до фиксации места хранения секрета в тикете #4.
		// Traceability: change:add-bybit-sync/design#d6
		Environment.SetEnvironmentVariable(EnvironmentBybitCredentialsProvider.ApiKeyVariableName, apiKey);
		Environment.SetEnvironmentVariable(EnvironmentBybitCredentialsProvider.ApiSecretVariableName, apiSecret);
		var provider = new EnvironmentBybitCredentialsProvider();

		// Act: получаем учётные данные и подставляем ключ в запрос так, как это будет делать клиент Bybit.
		var credentials = provider.GetCredentials();
		var request = new HttpRequestMessage(
			HttpMethod.Get, "https://api.bybit.com/v5/execution/list?category=linear");
		request.Headers.Add("X-BAPI-API-KEY", credentials.ApiKey);

		// Assert: в заголовок попадает именно ключ из окружения; секрет по сети не передаётся
		// и остаётся материалом для HMAC-подписи.
		Assert.That(request.Headers.GetValues("X-BAPI-API-KEY").Single(), Is.EqualTo(apiKey));
		Assert.That(credentials.ApiSecret, Is.EqualTo(apiSecret));
	}

	[TestMethod]
	[DataRow(null, null)]
	[DataRow("", "  ")]
	[DataRow("bybit-key", null)]
	[Description("Отсутствие или пустота переменных окружения с ключом — ошибка конфигурации")]
	[ExpectedException(typeof(InvalidOperationException))]
	public void ThrowOnMissingEnvironmentCredentials(string? apiKey, string? apiSecret)
	{
		// Arrange: любые пропуски в переменных окружения должны прерывать работу
		// понятной ошибкой, а не пустым ключом в запросе.
		if (apiKey is not null)
		{
			Environment.SetEnvironmentVariable(EnvironmentBybitCredentialsProvider.ApiKeyVariableName, apiKey);
		}

		if (apiSecret is not null)
		{
			Environment.SetEnvironmentVariable(EnvironmentBybitCredentialsProvider.ApiSecretVariableName, apiSecret);
		}

		var provider = new EnvironmentBybitCredentialsProvider();

		// Act — отсутствие ключа или секрета вызывает исключение.
		provider.GetCredentials();
	}

	#region Помощники

	private static void ResetEnvironment()
	{
		// Чистим переменные окружения, чтобы проверки не зависели от настроек машины разработчика.
		Environment.SetEnvironmentVariable(EnvironmentBybitCredentialsProvider.ApiKeyVariableName, null);
		Environment.SetEnvironmentVariable(EnvironmentBybitCredentialsProvider.ApiSecretVariableName, null);
	}

	#endregion
}
