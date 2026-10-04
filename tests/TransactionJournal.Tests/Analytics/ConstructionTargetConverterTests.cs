using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Analytics;
using TransactionJournal.Domain.Data;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Analytics;

/// <summary>
/// Проверки конвертера единиц риск/профит: введённые проценты переводятся
/// в USDT умножением на капитал, введённые USDT — в проценты делением на
/// капитал; без капитала вычисляемая единица остаётся невычисленной, а
/// незаданный параметр не даёт ни одной величины. Конвертер чистый — состояние
/// не хранит, каждая величина выводится из переданной пары и капитала.
/// Traceability: openspec:analytics/performance#requirement-risk-profit-unit-conversion
/// </summary>
[TestClass]
public class ConstructionTargetConverterTests
{
	[TestMethod]
	[Description("Проценты риска переводятся в USDT")]
	public void TryIfRiskPercentConvertsToUsdt()
	{
		// Act: риск 5% при выделенном капитале 3000 USDT.
		// Требование: чтение возвращает введённые проценты и вычисленную
		// USDT-величину: 5/100 × 3000 = 150.
		// Traceability: openspec:analytics/performance#scenario-risk-percent-to-usdt
		var amounts = ConstructionTargetConverter.Convert(5m, TargetUnit.Percent, 3000m);

		// Assert: введённые проценты первичны, USDT вычислена.
		Assert.That(amounts.Percent, Is.EqualTo(5m));
		Assert.That(amounts.Usdt, Is.EqualTo(150m));
	}

	[TestMethod]
	[Description("USDT-профит переводится в проценты")]
	public void TryIfProfitUsdtConvertsToPercent()
	{
		// Act: профит 300 USDT при выделенном капитале 3000 USDT.
		// Требование: чтение возвращает введённые USDT и вычисленные проценты:
		// 300/3000 × 100 = 10.
		// Traceability: openspec:analytics/performance#scenario-profit-usdt-to-percent
		var amounts = ConstructionTargetConverter.Convert(300m, TargetUnit.Usdt, 3000m);

		// Assert: введённые USDT первичны, проценты вычислены.
		Assert.That(amounts.Usdt, Is.EqualTo(300m));
		Assert.That(amounts.Percent, Is.EqualTo(10m));
	}

	[TestMethod]
	[Description("Без капитала вторая единица не вычисляется")]
	public void TryIfConversionNeedsCapital()
	{
		// Act: риск задан процентами при незаданном и при нулевом капитале.
		// Требование: незаданный или нулевой капитал оставляет незаполненную
		// единицу невычисленной — читающий слой возвращает только введённые
		// проценты без USDT-величины.
		// Traceability: openspec:analytics/performance#scenario-conversion-needs-capital
		var withoutCapital = ConstructionTargetConverter.Convert(5m, TargetUnit.Percent, null);
		var withZeroCapital = ConstructionTargetConverter.Convert(5m, TargetUnit.Percent, 0m);

		// Assert: введённые проценты на месте, USDT не вычислена в обоих случаях.
		Assert.That(withoutCapital.Percent, Is.EqualTo(5m));
		Assert.That(withoutCapital.Usdt, Is.Null);
		Assert.That(withZeroCapital.Percent, Is.EqualTo(5m));
		Assert.That(withZeroCapital.Usdt, Is.Null);

		// Симметричное правило для введённого USDT-значения: проценты не вычисляются.
		var usdtWithoutCapital = ConstructionTargetConverter.Convert(150m, TargetUnit.Usdt, null);
		Assert.That(usdtWithoutCapital.Usdt, Is.EqualTo(150m));
		Assert.That(usdtWithoutCapital.Percent, Is.Null);
	}

	[TestMethod]
	[Description("Незаданный параметр не даёт величин")]
	public void TryIfUnsetParameterGivesNoValues()
	{
		// Act: профит не введён — нет ни значения, ни единицы.
		// Требование: чтение не возвращает ни процентной, ни USDT-величины
		// отсутствующего параметра.
		// Traceability: openspec:analytics/performance#scenario-unset-param-no-values
		var noValue = ConstructionTargetConverter.Convert(null, TargetUnit.Percent, 3000m);
		var noUnit = ConstructionTargetConverter.Convert(300m, null, 3000m);
		var noPair = ConstructionTargetConverter.Convert(null, null, null);

		// Assert: во всех вариантах неполной пары обе величины отсутствуют.
		Assert.That(noValue.Percent, Is.Null);
		Assert.That(noValue.Usdt, Is.Null);
		Assert.That(noUnit.Percent, Is.Null);
		Assert.That(noUnit.Usdt, Is.Null);
		Assert.That(noPair.Percent, Is.Null);
		Assert.That(noPair.Usdt, Is.Null);
		Assert.That(noPair, Is.EqualTo(ConstructionTargetAmounts.None));
	}
}
