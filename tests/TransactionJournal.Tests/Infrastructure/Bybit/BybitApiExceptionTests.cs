using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Infrastructure.Bybit;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Infrastructure.Bybit;

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

	[TestMethod]
	[Description("Хелпер распознаёт отказ «контракт недоступен» по реальному тексту биржи")]
	public void TryIfHelperRecognizesRealContractUnavailableText()
	{
		// Arrange: точный текст отказа биржи из практики синхронизации делистнутой доски.
		// Требование: отказ 110023 «The contract is not available for trades» при запросе
		// окна области распознаётся как недоступность контракта.
		// Traceability: openspec:sync/bybit-history#requirement-contract-unavailable-area-skip
		var real = new BybitApiException(110023, "The contract is not available for trades");

		// Act — Assert
		Assert.That(BybitApiException.IsContractUnavailableError(real), Is.True);
	}

	[TestMethod]
	[Description("Хелпер не срабатывает на другом коде с тем же текстом отказа")]
	public void TryIfHelperIgnoresOtherRetCodeWithSameText()
	{
		// Arrange: тот же текст отказа, но другой код — распознавание ведётся только
		// по retCode 110023, текст сообщения биржи не участвует.
		var otherCode = new BybitApiException(10001, "The contract is not available for trades");

		// Act — Assert
		Assert.That(BybitApiException.IsContractUnavailableError(otherCode), Is.False);
	}

	[TestMethod]
	[Description("Хелпер не зависит от формулировки retMsg: другой текст с кодом 110023 распознаётся")]
	public void TryIfHelperMatchesOtherWordingWithContractUnavailableRetCode()
	{
		// Arrange: биржа переиспользует код 110023 с другим текстом — формулировка
		// из официальной таблицы кодов о позиции «только закрытие».
		// Требование: распознавание не зависит от формулировки retMsg.
		// Traceability: openspec:sync/bybit-history#requirement-contract-unavailable-area-skip
		var officialWording = new BybitApiException(
			110023, "Currently you can only reduce your position on this contract");

		// Act — Assert
		Assert.That(BybitApiException.IsContractUnavailableError(officialWording), Is.True);
	}
}
