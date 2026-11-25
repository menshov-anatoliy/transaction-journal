namespace TransactionJournal.Tests.Web.Api;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Api.Constructions;
using TransactionJournal.Application.Analytics;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Юнит-проверки сериализатора контракта реального риска: стабильные строки
/// конечного и нерассчитанного риска и защита инварианта
/// «число только при конечном риске». Нарушенная пара read-модели не
/// сериализуется, а падает с явной ошибкой.
/// </summary>
[TestClass]
public sealed class RealRiskContractTests
{
	[TestMethod]
	[Description("Валидные пары состояние-число сериализуются стабильными строками контракта")]
	// Каждому состоянию риска отвечает своя строка JSON-контракта:
	// конечный риск несёт число, нерассчитанный риск идёт без числа.
	// Traceability: issue:#90
	public void TryIfSerializesValidStatusPairs()
	{
		// Act: сериализация всех валидных пар — статус и величина риска.
		var finite = RealRiskContract.SerializeStatus(RealRiskStatus.Finite, 150m);
		var unavailable = RealRiskContract.SerializeStatus(RealRiskStatus.Unavailable, null);

		// Assert: строки контракта стабильны.
		Assert.That(finite, Is.EqualTo("finite"));
		Assert.That(unavailable, Is.EqualTo("unavailable"));
	}

	[TestMethod]
	[ExpectedException(typeof(InvalidOperationException))]
	[Description("Конечный риск без числа не сериализуется")]
	// Инвариант «число только при finite»: пара finite + null не может
	// попасть в контракт — сериализатор падает с явной ошибкой.
	// Traceability: issue:#90
	public void ThrowOnFiniteStatusWithoutNumber()
	{
		// Act: сериализация нарушенной пары конечного риска.
		RealRiskContract.SerializeStatus(RealRiskStatus.Finite, null);
	}

	[TestMethod]
	[ExpectedException(typeof(InvalidOperationException))]
	[Description("Нерассчитанный риск с числом не сериализуется")]
	// Число за недоступным итогом дало бы потребителю ложную
	// величину риска: пара unavailable + число не сериализуется.
	// Traceability: issue:#90
	public void ThrowOnUnavailableStatusWithNumber()
	{
		// Act: сериализация нарушенной пары нерассчитанного риска.
		RealRiskContract.SerializeStatus(RealRiskStatus.Unavailable, 150m);
	}
}
