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
/// «число только при конечном риске» для обеих величин — величины риска и
/// знакового худшего исхода. Нарушенная тройка read-модели не сериализуется,
/// а падает с явной ошибкой.
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
		// Act: сериализация всех валидных троек — статус, риск и худший исход.
		var finite = RealRiskContract.SerializeStatus(RealRiskStatus.Finite, 150m, -89.75m);
		var unbounded = RealRiskContract.SerializeStatus(RealRiskStatus.Unbounded, null, null);
		var unavailable = RealRiskContract.SerializeStatus(RealRiskStatus.Unavailable, null, null);

		// Assert: строки контракта стабильны.
		Assert.That(finite, Is.EqualTo("finite"));
		Assert.That(unbounded, Is.EqualTo("unbounded"));
		Assert.That(unavailable, Is.EqualTo("unavailable"));
	}

	[TestMethod]
	[Description("Худший исход без числа сериализуется статусом: unbounded и unavailable несут null")]
	// Состояния без величины переиспользуют статус реального риска:
	// неограниченный хвост и неполные данные идут без числа худшего
	// исхода, потребителю достаточно статуса состояния.
	// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
	// Traceability: openspec:analytics/performance#scenario-worst-case-null-with-status
	public void TryIfWorstCaseIsNullWithNonFiniteStatus()
	{
		// Act: сериализация состояний без числа худшего исхода.
		var unbounded = RealRiskContract.SerializeStatus(RealRiskStatus.Unbounded, null, null);
		var unavailable = RealRiskContract.SerializeStatus(RealRiskStatus.Unavailable, null, null);

		// Assert: статус состояния проходит рядом с null худшего исхода.
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
		// Act: сериализация нарушенной пары конечного риска (худший исход валиден).
		RealRiskContract.SerializeStatus(RealRiskStatus.Finite, null, -89.75m);
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
		RealRiskContract.SerializeStatus(RealRiskStatus.Unbounded, 150m, null);
	}

	[TestMethod]
	[ExpectedException(typeof(InvalidOperationException))]
	[Description("Конечный риск без числа худшего исхода не сериализуется")]
	// Инвариант «число только при finite» переиспользуется худшим исходом:
	// конечное состояние без знаковой величины ломает пару индикатора
	// (риск есть, худший исход потерян) — сериализатор падает.
	// Traceability: openspec:analytics/performance#requirement-worst-case-outcome-metric
	// Traceability: change:rework-finresult-border-worst-case/design#d1
	public void ThrowOnFiniteStatusWithoutWorstCase()
	{
		// Act: сериализация конечного риска без величины худшего исхода.
		RealRiskContract.SerializeStatus(RealRiskStatus.Finite, 150m, null);
	}

	[TestMethod]
	[ExpectedException(typeof(InvalidOperationException))]
	[Description("Неограниченный риск с числом худшего исхода не сериализуется")]
	// Худший исход не может быть числом там, где сам риск неограничен:
	// число за статусом unbounded исказило бы границу индикатора.
	// Traceability: openspec:analytics/performance#scenario-worst-case-null-with-status
	// Traceability: change:rework-finresult-border-worst-case/design#d1
	public void ThrowOnUnboundedStatusWithWorstCase()
	{
		// Act: сериализация неограниченного риска с числом худшего исхода.
		RealRiskContract.SerializeStatus(RealRiskStatus.Unbounded, null, -89.75m);
	}
}
