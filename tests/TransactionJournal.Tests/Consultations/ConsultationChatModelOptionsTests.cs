namespace TransactionJournal.Tests.Consultations;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Consultations;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Проверки конфигурации модели чата консультаций: секция
/// Consultations:ChatModel задаёт провайдера, модель, OpenAI-совместимый
/// эндпоинт и ключ; отсутствие секции оставляет дефолт z.ai GLM-5.3, смена
/// провайдера или модели выполняется правкой конфигурации без правки кода, у
/// стороннего провайдера явный эндпоинт обязателен.
/// </summary>
[TestClass]
public sealed class ConsultationChatModelOptionsTests
{
	[TestMethod]
	[Description("Без секции Consultations:ChatModel модель чата работает на дефолтах z.ai GLM-5.3")]
	public void TryIfSectionAbsentDefaultsToZaiGlm()
	{
		// Act: разрешаем опции для полностью отсутствующей секции конфигурации.
		var options = ConsultationChatModelOptions.Resolve(null, null, null, null);

		// Assert: дефолт — провайдер z.ai, модель GLM-5.3, эндпоинт z.ai, ключ пуст.
		// Traceability: openspec:consultations/tools#requirement-tools-chat-model-configurable
		Assert.That(options.Provider, Is.EqualTo("zai"));
		Assert.That(options.Model, Is.EqualTo(ConsultationChatModelOptions.DefaultModel));
		Assert.That(options.BaseUrl, Is.EqualTo(ConsultationChatModelOptions.ZaiBaseUrl));
		Assert.That(options.ApiKey, Is.Empty);
	}

	[TestMethod]
	[DataRow("openai", " gpt-5.2 ", " https://api.openai.com/v1/ ", " key-1 ")]
	[Description("Смена провайдера и модели выполняется правкой конфигурации без правки кода")]
	public void TryIfConfigNamesOtherProviderThenResolved(string provider, string model, string baseUrl, string apiKey)
	{
		// Act: разрешаем опции с явной секцией стороннего провайдера.
		var options = ConsultationChatModelOptions.Resolve(provider, model, baseUrl, apiKey);

		// Assert: значения берутся из конфигурации, окружающие пробелы триммируются.
		// Traceability: openspec:consultations/tools#scenario-tools-model-switch-config
		Assert.That(options.Provider, Is.EqualTo("openai"));
		Assert.That(options.Model, Is.EqualTo("gpt-5.2"));
		Assert.That(options.BaseUrl, Is.EqualTo("https://api.openai.com/v1/"));
		Assert.That(options.ApiKey, Is.EqualTo("key-1"));
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
		// Traceability: openspec:consultations/tools#scenario-tools-model-switch-config
		ConsultationChatModelOptions.Resolve("openai", "gpt-5.2", baseUrl, "key-1");
	}
}
