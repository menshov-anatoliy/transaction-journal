using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Bybit;
using TransactionJournal.Sync;
using TransactionJournal.Tests.Bybit;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Sync;

/// <summary>
/// Проверки производственного шлюза истории: вызов execution list делегируется
/// подписанному клиенту Bybit без изменений, а ошибки валидации и транспорта
/// проходят через шлюз к вызывающей стороне.
/// </summary>
[TestClass]
public class BybitHistoryGatewayTests
{
	private const string TestBaseUrl = "http://bybit-test.local";

	[TestMethod]
	[Description("Шлюз делегирует запрос execution list клиенту Bybit и возвращает разобранную страницу")]
	public async Task TryIfGatewayDelegatesExecutionListToClient()
	{
		// Arrange: подписанный клиент на фиктивном транспорте с ответом из одной записи.
		var handler = new ScriptedHttpMessageHandler();
		handler.EnqueueJson(
			"""{"retCode":0,"retMsg":"OK","result":{"list":[{"symbol":"BTCUSDT","execId":"exec-1","side":"Buy","execTime":"1672214887232","execPrice":"10969.5","execQty":"0.001"}],"nextPageCursor":""}}""");
		var credentials = new BybitCredentials("test-api-key", "test-api-secret");
		var credentialsProvider = Mock.Of<IBybitCredentialsProvider>(
			provider => provider.GetCredentials() == credentials);
		var gateway = new BybitHistoryGateway(new BybitApiClient(
			new HttpClient(handler), credentialsProvider, new BybitClientOptions { BaseUrl = TestBaseUrl }));

		// Act
		var page = await gateway.GetExecutionListAsync(new BybitExecutionListQuery { Category = "linear" });

		// Assert: запрос ушёл к бирже подписанным GET с исходными параметрами, ответ разобран в страницу.
		Assert.That(handler.Requests.Single().RequestUri!.ToString(),
			Is.EqualTo($"{TestBaseUrl}/v5/execution/list?category=linear"));
		Assert.That(page.List.Single().ExecId, Is.EqualTo("exec-1"));
		Assert.That(page.HasNextPage, Is.False);
	}

	[TestMethod]
	[Description("Запрос без параметров отклоняется шлюзом до сетевого вызова")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullQuery()
	{
		// Arrange: клиент на фиктивном транспорте без заготовленных ответов.
		var handler = new ScriptedHttpMessageHandler();
		var credentials = new BybitCredentials("test-api-key", "test-api-secret");
		var credentialsProvider = Mock.Of<IBybitCredentialsProvider>(
			provider => provider.GetCredentials() == credentials);
		var gateway = new BybitHistoryGateway(new BybitApiClient(
			new HttpClient(handler), credentialsProvider, new BybitClientOptions { BaseUrl = TestBaseUrl }));

		// Act — пустой запрос прерывается валидацией до отправки.
		try
		{
			gateway.GetExecutionListAsync(null!).GetAwaiter().GetResult();
		}
		catch (ArgumentNullException)
		{
			// Assert: сетевых вызовов не было.
			Assert.That(handler.Requests, Is.Empty);
			throw;
		}
	}

	[TestMethod]
	[Description("Шлюз без клиента Bybit отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullClient()
	{
		// Arrange — Act: шлюз без подписанного клиента бессмыслен.
		new BybitHistoryGateway(null!);
	}
}
