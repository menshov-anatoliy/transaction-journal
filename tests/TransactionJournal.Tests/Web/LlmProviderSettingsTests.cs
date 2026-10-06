namespace TransactionJournal.Tests.Web;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Проверки общих параметров LLM-провайдера: секция Llm задаёт провайдера,
/// OpenAI-совместимый эндпоинт и ключ доступа для всех потребителей; без
/// секции работает дефолт z.ai, у провайдера, отличного от z.ai, явный
/// эндпоинт обязателен, обе формы эндпоинта (со слэшем и без) эквивалентны.
/// </summary>
[TestClass]
public sealed class LlmProviderSettingsTests
{
	[TestMethod]
	[Description("Без секции Llm общие параметры работают на дефолтах: провайдер z.ai, адрес api.z.ai, ключ пуст")]
	public void TryIfSectionAbsentDefaultsToZai()
	{
		// Act: разрешаем общие параметры для полностью отсутствующей секции конфигурации.
		var settings = LlmProviderSettings.Resolve(null, null, null);

		// Assert: дефолт — провайдер z.ai, нормализованный адрес z.ai, ключ пуст.
		// Traceability: openspec:config/llm-provider#scenario-llm-provider-defaults
		Assert.That(settings.Provider, Is.EqualTo("zai"));
		Assert.That(settings.BaseUrl, Is.EqualTo("https://api.z.ai/api/paas/v4/"));
		Assert.That(settings.ApiKey, Is.Empty);
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("   ")]
	[ExpectedException(typeof(InvalidOperationException))]
	[Description("Сторонний провайдер без явного эндпоинта отклоняется вместо тихого отката на z.ai")]
	public void ThrowOnOtherProviderWithoutBaseUrl(string? baseUrl)
	{
		// Act: сторонний провайдер, эндпоинт не задан — Resolve обязан отказаться,
		// тихий откат на адрес z.ai замаскировал бы ошибку настройки и уводил бы
		// запросы к чужому сервису.
		// Traceability: openspec:config/llm-provider#scenario-llm-provider-foreign-requires-base-url
		LlmProviderSettings.Resolve("openai", baseUrl, "key-1");
	}

	[TestMethod]
	[DataRow("https://api.z.ai/api/paas/v4")]
	[DataRow("https://api.z.ai/api/paas/v4/")]
	[Description("Эндпоинт без завершающего слэша нормализуется к форме со слэшем")]
	public void TryIfBaseUrlWithoutSlashThenNormalized(string baseUrl)
	{
		// Act: разрешаем общие параметры с обеими формами эндпоинта.
		var settings = LlmProviderSettings.Resolve("zai", baseUrl, "key-1");

		// Assert: обе формы значения конфигурации дают один и тот же адрес.
		// Traceability: openspec:config/llm-provider#scenario-llm-provider-base-url-slash-normalized
		Assert.That(settings.BaseUrl, Is.EqualTo("https://api.z.ai/api/paas/v4/"));
	}

	[TestMethod]
	[DataRow(" zai ", " https://api.openai.com/v1/ ", " key-1 ")]
	[Description("Значения секции Llm триммируются: окружающие пробелы не попадают в параметры")]
	public void TryIfConfigValuesThenTrimmed(string provider, string baseUrl, string apiKey)
	{
		// Act: разрешаем общие параметры со значениями в окружении пробелов.
		var settings = LlmProviderSettings.Resolve(provider, baseUrl, apiKey);

		// Assert: пробелы сняты, значения взяты из конфигурации.
		Assert.That(settings.Provider, Is.EqualTo("zai"));
		Assert.That(settings.BaseUrl, Is.EqualTo("https://api.openai.com/v1/"));
		Assert.That(settings.ApiKey, Is.EqualTo("key-1"));
	}
}
