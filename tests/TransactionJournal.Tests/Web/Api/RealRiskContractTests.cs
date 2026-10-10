namespace TransactionJournal.Tests.Web.Api;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Api.Constructions;
using TransactionJournal.Application.Analytics;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Юнит-проверки сериализатора контракта реального риска: стабильные строки
/// конечного, неограниченного и нерассчитанного риска и защита инварианта
/// «число только при конечном риске» — нарушенная пара read-модели не
/// сериализуется, а падает с явной ошибкой.
/// </summary>
[TestClass]
public sealed class RealRiskContractTests
{
	[TestMethod]
	[Description("Валидные пары состояние-число сериализуются стабильными строками контракта")]
	// Каждому состоянию риска отвечает своя строка JSON-контракта:
	// конечный риск несёт число, неограниченный хвост и неполные данные идут без числа.
	// Traceability: openspec:analytics/performance#requirement-real-risk-worst-at-expiry
	public void TryIfSerializesValidStatusPairs()
	{
		// Act: сериализация всех валидных пар.
		var finite = RealRiskContract.SerializeStatus(RealRiskStatus.Finite, 150m);
		var unbounded = RealRiskContract.SerializeStatus(RealRiskStatus.Unbounded, null);
		var unavailable = RealRiskContract.SerializeStatus(RealRiskStatus.Unavailable, null);

		// Assert: строки контракта стабильны.
		Assert.That(finite, Is.EqualTo("finite"));
		Assert.That(unbounded, Is.EqualTo("unbounded"));
		Assert.That(unavailable, Is.EqualTo("unavailable"));
	}

	[TestMethod]
	[ExpectedException(typeof(InvalidOperationException))]
	[Description("Конечный риск без числа не сериализуется")]
	// Инвариант «число только при finite»: пара finite + null не может
	// попасть в контракт — сериализатор падает с явной ошибкой.
	// Traceability: openspec:analytics/performance#scenario-real-risk-unbounded-is-null
	// Traceability: change:show-unbounded-finresult-risk/design#d1
	public void ThrowOnFiniteStatusWithoutNumber()
	{
		// Act: сериализация нарушенной пары конечного риска.
		RealRiskContract.SerializeStatus(RealRiskStatus.Finite, null);
	}

	[TestMethod]
	[ExpectedException(typeof(InvalidOperationException))]
	[Description("Неограниченный риск с числом не сериализуется")]
	// Число за неограниченным худшим случаем исказило бы индикатор:
	// пара unbounded + величина не сериализуется, сериализатор падает.
	// Traceability: openspec:analytics/performance#scenario-real-risk-unbounded-is-null
	// Traceability: change:show-unbounded-finresult-risk/design#d1
	public void ThrowOnUnboundedStatusWithNumber()
	{
		// Act: сериализация нарушенной пары неограниченного риска.
		RealRiskContract.SerializeStatus(RealRiskStatus.Unbounded, 150m);
	}
}
