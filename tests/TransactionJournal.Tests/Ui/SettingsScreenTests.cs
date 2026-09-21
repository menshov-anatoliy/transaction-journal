using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Bybit;
using TransactionJournal.Components.Pages;
using SettingsPage = TransactionJournal.Components.Pages.Settings;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ui;

/// <summary>
/// Проверки блока подключения экрана «Настройки»: API-ключ показан маскированно,
/// секрет не отображается нигде и не проходит в состояние компонента, экран
/// указывает на переменные окружения как место хранения секрета; без настроенного
/// ключа показывается явное состояние вместо маски.
/// Traceability: openspec:ui/screens#scenario-settings-secret-never-displayed
/// </summary>
[TestClass]
public class SettingsScreenTests
{
	private const string ApiKey = "abcdEFGH1234wxyz";
	private const string ApiSecret = "top-secret-value-42";

	private Bunit.TestContext _context = null!;

	[TestInitialize]
	public void Initialize()
	{
		_context = new Bunit.TestContext();

		// Поставщик учётных данных подменяется заглушкой: секрет известен проверке,
		// и она ищет его в разметке — настоящее чтение переменных окружения здесь
		// не участвует и глобальное состояние тестов не трогает.
		_context.Services.AddSingleton<IBybitCredentialsProvider>(
			new StubCredentials(ApiKey, ApiSecret));
		_context.Services.AddSingleton<SettingsReadModel>();
	}

	[TestCleanup]
	public void Cleanup()
	{
		_context.Dispose();
	}

	[TestMethod]
	[Description("Ключ показан маскированно, секрет не отображается нигде, указаны переменные окружения")]
	public void TryIfSecretIsNeverDisplayed()
	{
		// Act: пользователь открывает «Настройки» с настроенным ключом.
		var cut = _context.RenderComponent<SettingsPage>();

		// Assert: ключ виден только маской «первые четыре ······ последние четыре»,
		// полный ключ и секрет в разметке отсутствуют, а местом хранения секрета
		// названы переменные окружения.
		// Требование: секрет не отображается на экране настроек.
		// Traceability: openspec:ui/screens#scenario-settings-secret-never-displayed
		Assert.That(cut.Markup, Does.Contain("abcd······wxyz"));
		Assert.That(cut.Markup, Does.Not.Contain(ApiKey));
		Assert.That(cut.Markup, Does.Not.Contain(ApiSecret));
		Assert.That(cut.Markup, Does.Contain(EnvironmentBybitCredentialsProvider.ApiKeyVariableName));
		Assert.That(cut.Markup, Does.Contain(EnvironmentBybitCredentialsProvider.ApiSecretVariableName));
	}

	[TestMethod]
	[Description("Короткий ключ маскируется целиком и не выдаёт ни одного знака")]
	public void TryIfShortKeyIsMaskedEntirely()
	{
		// Arrange: ключ короче восьми знаков — маскировать по краям нечего.
		_context.Services.AddSingleton<IBybitCredentialsProvider>(
			new StubCredentials("abc", ApiSecret));

		// Act: пользователь открывает «Настройки».
		var cut = _context.RenderComponent<SettingsPage>();

		// Assert: вместо маски по краям — сплошное сокрытие, знаков ключа нет.
		Assert.That(cut.Markup, Does.Contain("······"));
		Assert.That(cut.Markup, Does.Not.Contain("abc"));
		Assert.That(cut.Markup, Does.Not.Contain(ApiSecret));
	}

	[TestMethod]
	[Description("Без настроенного ключа экран показывает явное состояние с именами переменных окружения")]
	public void TryIfUnconfiguredKeyShowsExplicitState()
	{
		// Arrange: переменные окружения не заданы — поставщик отказывает.
		_context.Services.AddSingleton<IBybitCredentialsProvider>(
			new ThrowingCredentials());

		// Act: пользователь открывает «Настройки».
		var cut = _context.RenderComponent<SettingsPage>();

		// Assert: вместо маски — явное сообщение с именами переменных окружения;
		// никаких значений ключа и секрета на экране нет.
		Assert.That(cut.Markup, Does.Contain("Ключ не настроен"));
		Assert.That(cut.Markup, Does.Contain(EnvironmentBybitCredentialsProvider.ApiKeyVariableName));
		Assert.That(cut.Markup, Does.Contain(EnvironmentBybitCredentialsProvider.ApiSecretVariableName));
		Assert.That(cut.Markup, Does.Not.Contain("······"));
		Assert.That(cut.Markup, Does.Not.Contain(ApiSecret));
	}

	/// <summary>Подменяет поставщика парой известных проверке значений ключа и секрета.</summary>
	private sealed class StubCredentials(string apiKey, string apiSecret) : IBybitCredentialsProvider
	{
		public BybitCredentials GetCredentials() => new(apiKey, apiSecret);
	}

	/// <summary>Подменяет отказ не настроенных переменных окружения.</summary>
	private sealed class ThrowingCredentials : IBybitCredentialsProvider
	{
		public BybitCredentials GetCredentials() =>
			throw new InvalidOperationException(
				$"Не задана переменная окружения {EnvironmentBybitCredentialsProvider.ApiKeyVariableName}.");
	}
}
