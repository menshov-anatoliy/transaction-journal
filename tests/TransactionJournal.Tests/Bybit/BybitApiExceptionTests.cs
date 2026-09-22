using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Bybit;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Bybit;

/// <summary>
/// Проверки распознавания пограничного отказа биржи за глубину хранения истории:
/// хелпер должен выделять отказ «раньше двух лет» среди прочих отказов параметров
/// с тем же retCode 10001 и не путать его с другими кодами ошибок биржи.
/// </summary>
[TestClass]
public class BybitApiExceptionTests
{
	[TestMethod]
	[Description("Хелпер распознаёт реальный текст отказа биржи за глубину хранения без учёта регистра")]
	public void TryIfHelperRecognizesRealBoundaryTextCaseInsensitive()
	{
		// Arrange: точный текст отказа биржи из практики первого backfill.
		var real = new BybitApiException(
			10001,
			"Can't query order earlier than 2 years, please check your params: startTime or endTime!");
		var mixedCase = new BybitApiException(10001, "Can't Query Order EARLIER THAN 2 YEARS, please check your params.");

		// Act
		var realMatches = BybitApiException.IsHistoryBoundaryError(real);
		var mixedCaseMatches = BybitApiException.IsHistoryBoundaryError(mixedCase);

		// Assert: и точный, и переведённый в верхний регистр текст распознаны, retMsg сохранён.
		Assert.That(realMatches, Is.True);
		Assert.That(mixedCaseMatches, Is.True);
		Assert.That(real.RetMsg, Does.Contain("earlier than 2 years"));
	}

	[TestMethod]
	[Description("Хелпер не срабатывает на другом сообщении с тем же кодом 10001")]
	public void TryIfHelperIgnoresOtherMessageWithParamsRetCode()
	{
		// Arrange: обычный отказ параметров без указания на глубину хранения.
		var other = new BybitApiException(10001, "params error: category invalid");

		// Act — Assert
		Assert.That(BybitApiException.IsHistoryBoundaryError(other), Is.False);
	}

	[TestMethod]
	[Description("Хелпер не срабатывает на retCode 10006 лимита частоты")]
	public void TryIfHelperIgnoresRateLimitRetCode()
	{
		// Arrange: отказ лимита частоты не имеет отношения к границе истории.
		var rateLimit = new BybitApiException(10006, "Too many visits!");

		// Act — Assert
		Assert.That(BybitApiException.IsHistoryBoundaryError(rateLimit), Is.False);
	}
}
