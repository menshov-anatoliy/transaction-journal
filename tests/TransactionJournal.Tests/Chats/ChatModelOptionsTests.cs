namespace TransactionJournal.Tests.Chats;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Chats;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Проверки модели чата агента: рабочая модель задаётся подсекцией
/// Llm:Chat общей секции провайдера, отсутствие или пустота подсекции
/// оставляет дефолт GLM-5.3, смена модели выполняется правкой конфигурации
/// без правки кода; общие параметры провайдера приходят уже разрешёнными из
/// LlmProviderSettings composition root.
/// </summary>
[TestClass]
public sealed class ChatModelOptionsTests
{
	[TestMethod]
	[Description("Без подсекции Llm:Chat модель чата работает на дефолте GLM-5.3")]
	public void TryIfSubsectionAbsentDefaultsToGlm()
	{
		// Arrange: общие параметры провайдера, уже разрешённые composition root.
		var provider = "zai";
		var baseUrl = "https://api.z.ai/api/paas/v4/";
		var apiKey = "key-1";

		// Act: разрешаем опции без подсекции модели.
		var options = ChatModelOptions.Resolve(provider, baseUrl, apiKey, null);

		// Assert: модель — дефолт GLM-5.3, общие параметры перенесены как есть.
		// Traceability: openspec:chats/sources#requirement-sources-model-is-chat-parameter
		Assert.That(options.Model, Is.EqualTo(ChatModelOptions.DefaultModel));
		Assert.That(options.Provider, Is.EqualTo("zai"));
		Assert.That(options.BaseUrl, Is.EqualTo("https://api.z.ai/api/paas/v4/"));
		Assert.That(options.ApiKey, Is.EqualTo("key-1"));
	}

	[TestMethod]
	[Description("Смена модели конфигурацией Llm:Chat:Model выполняется без правки кода")]
	public void TryIfConfigSetsOtherModelThenResolved()
	{
		// Act: разрешаем опции с моделью из подсекции в окружении пробелов.
		var options = ChatModelOptions.Resolve("zai", "https://api.z.ai/api/paas/v4/", "key-1", " glm-5.2 ");

		// Assert: модель взята из конфигурации, пробелы сняты.
		// Traceability: openspec:config/llm-provider#scenario-llm-models-chat-switch
		Assert.That(options.Model, Is.EqualTo("glm-5.2"));
	}

	[TestMethod]
	[DataRow(null)]
	[DataRow("   ")]
	[Description("Пустая подсекция Llm:Chat даёт дефолт модели GLM-5.3")]
	public void TryIfBlankSubsectionThenDefaultModel(string? model)
	{
		// Act: разрешаем опции с пустым значением модели подсекции.
		var options = ChatModelOptions.Resolve("zai", "https://api.z.ai/api/paas/v4/", "key-1", model);

		// Assert: пустая подсекция откатывается к дефолту GLM-5.3.
		// Traceability: openspec:config/llm-provider#scenario-llm-models-blank-defaults
		Assert.That(options.Model, Is.EqualTo(ChatModelOptions.DefaultModel));
	}
}

