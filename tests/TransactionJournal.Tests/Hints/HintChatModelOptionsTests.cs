namespace TransactionJournal.Tests.Hints;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Hints;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Проверки модели изложения сводок подсказок: рабочая модель задаётся
/// подсекцией Llm:Hint общей секции провайдера, отсутствие или пустота
/// подсекции оставляет дефолт glm-5.3-flash, смена модели выполняется правкой
/// конфигурации без правки кода; общие параметры провайдера приходят уже
/// разрешёнными из LlmProviderSettings composition root.
/// </summary>
[TestClass]
public sealed class HintChatModelOptionsTests
{
	[TestMethod]
	[Description("Без подсекции Llm:Hint модель изложения работает на дефолте glm-5.3-flash")]
	public void TryIfSubsectionAbsentDefaultsToFlash()
	{
		// Arrange: общие параметры провайдера, уже разрешённые composition root.
		var provider = "zai";
		var baseUrl = "https://api.z.ai/api/paas/v4/";
		var apiKey = "key-1";

		// Act: разрешаем опции без подсекции модели.
		var options = HintChatModelOptions.Resolve(provider, baseUrl, apiKey, null);

		// Assert: модель — дефолт glm-5.3-flash, общие параметры перенесены как есть.
		Assert.That(options.Model, Is.EqualTo(HintChatModelOptions.DefaultModel));
		Assert.That(options.Provider, Is.EqualTo("zai"));
		Assert.That(options.BaseUrl, Is.EqualTo("https://api.z.ai/api/paas/v4/"));
		Assert.That(options.ApiKey, Is.EqualTo("key-1"));
	}

	[TestMethod]
	[Description("Смена модели конфигурацией Llm:Hint:Model выполняется без правки кода")]
	public void TryIfConfigSetsOtherModelThenResolved()
	{
		// Act: разрешаем опции с моделью из подсекции в окружении пробелов.
		var options = HintChatModelOptions.Resolve("zai", "https://api.z.ai/api/paas/v4/", "key-1", " glm-5.3-air ");

		// Assert: модель взята из конфигурации, пробелы сняты.
		// Traceability: openspec:config/llm-provider#scenario-llm-models-hint-switch
		Assert.That(options.Model, Is.EqualTo("glm-5.3-air"));
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("   ")]
	[Description("Пустая подсекция Llm:Hint даёт дефолт модели glm-5.3-flash")]
	public void TryIfBlankSubsectionThenDefaultModel(string? model)
	{
		// Act: разрешаем опции с пустым значением модели подсекции.
		var options = HintChatModelOptions.Resolve("zai", "https://api.z.ai/api/paas/v4/", "key-1", model);

		// Assert: пустая подсекция откатывается к дефолту glm-5.3-flash.
		// Traceability: openspec:config/llm-provider#scenario-llm-models-blank-defaults
		Assert.That(options.Model, Is.EqualTo(HintChatModelOptions.DefaultModel));
	}
}
