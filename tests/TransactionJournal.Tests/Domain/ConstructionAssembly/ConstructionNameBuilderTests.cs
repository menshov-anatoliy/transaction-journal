using NUnit.Framework;
using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Domain.Materialization;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain.ConstructionAssembly;

/// <summary>
/// Проверки производного именования конструкций v2 из состава живых ног с
/// количествами: стреддл и стреддл с акцентом вверх/вниз N:M, стренгл и
/// стренгл с акцентом, колл-спред и пут-спред с соотношением, направленные
/// CALL и PUT; имя считается только по ненулевым ногам на примерах символов
/// из истории.
/// </summary>
[TestClass]
public class ConstructionNameBuilderTests
{
	/// <summary>Нога конструкции с количеством для построения имени.</summary>
	private static PlannedLeg Leg(string symbol, string baseCoin, int year, int month, int day, double strike, OptionType type, double quantity) => new()
	{
		Symbol = symbol,
		Strike = (decimal)strike,
		BoardExpiryDate = new DateTime(year, month, day),
		Type = type,
		Quantity = (decimal)quantity,
	};

	[TestMethod]
	[Description("Имена симметричных стреддла и колл-спреда совпадают с примерами из истории")]
	// Имя строится из живых ног по шаблону «{Актив} {вид} {доска} {страйки}»;
	// равные размеры ног соотношение N:M не добавляют.
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#requirement-derived-construction-naming
	public void TryIfNameMatchesHistoryExamples()
	{
		// Arrange: стреддл ETH 1600 и колл-спред BTC 85000/120000 с равными размерами ног.
		var straddleLegs = new[]
		{
			Leg("ETH-25SEP26-1600-C-USDT", "ETH", 2026, 9, 25, 1600, OptionType.Call, 1d),
			Leg("ETH-25SEP26-1600-P-USDT", "ETH", 2026, 9, 25, 1600, OptionType.Put, 1d),
		};
		var callSpreadLegs = new[]
		{
			Leg("BTC-25DEC26-120000-C-USDT", "BTC", 2026, 12, 25, 120000, OptionType.Call, -1d),
			Leg("BTC-25DEC26-85000-C-USDT", "BTC", 2026, 12, 25, 85000, OptionType.Call, 1d),
		};

		// Act: строим имена.
		var straddleName = ConstructionNameBuilder.BuildName("ETH", straddleLegs);
		var callSpreadName = ConstructionNameBuilder.BuildName("BTC", callSpreadLegs);

		// Assert: имена воспроизводят контрольные примеры; страйки упорядочены по возрастанию.
		Assert.That(straddleName, Is.EqualTo("ETH стреддл 25SEP26 1600"));
		Assert.That(callSpreadName, Is.EqualTo("BTC колл-спред 25DEC26 85000/120000"));
	}

	[TestMethod]
	[DataRow("ETH", 2026, 9, 25, 1800d, OptionType.Call, 1d, 2100d, OptionType.Put, 1d, "ETH стренгл 25SEP26 1800/2100")]
	[DataRow("ETH", 2026, 9, 25, 1600d, OptionType.Put, 1d, 2100d, OptionType.Put, 1d, "ETH пут-спред 25SEP26 1600/2100")]
	[DataRow("ETH", 2026, 9, 25, 2100d, OptionType.Call, 1d, 2100d, OptionType.Call, 1d, "ETH направленная CALL 25SEP26 2100")]
	[DataRow("ETH", 2026, 9, 25, 2100d, OptionType.Put, 1d, 2100d, OptionType.Put, 1d, "ETH направленная PUT 25SEP26 2100")]
	[DataRow("ETH", 2026, 12, 25, 1600d, OptionType.Call, 1d, 1600d, OptionType.Put, 1d, "ETH стреддл 25DEC26 1600")]
	[Description("Вид конструкции выводится из состава ног: стренгл, пут-спред, направленные CALL и PUT")]
	public void TryIfKindIsDerivedFromLegComposition(
		string baseCoin,
		int year,
		int month,
		int day,
		double firstStrike,
		OptionType firstType,
		double firstQuantity,
		double secondStrike,
		OptionType secondType,
		double secondQuantity,
		string expectedName)
	{
		// Arrange: две живые ноги на одной доске.
		var legs = new[]
		{
			Leg("symbol-1", baseCoin, year, month, day, firstStrike, firstType, firstQuantity),
			Leg("symbol-2", baseCoin, year, month, day, secondStrike, secondType, secondQuantity),
		};

		// Act: строим имя.
		var name = ConstructionNameBuilder.BuildName(baseCoin, legs);

		// Assert: имя соответствует виду и составу ног.
		Assert.That(name, Is.EqualTo(expectedName));
	}

	[TestMethod]
	[DataRow(2d, 1d, "ETH стреддл с акцентом вверх 2:1 25SEP26 1600")]
	[DataRow(1d, 2d, "ETH стреддл с акцентом вниз 2:1 25SEP26 1600")]
	[DataRow(4d, 2d, "ETH стреддл с акцентом вверх 2:1 25SEP26 1600")]
	[DataRow(3d, 6d, "ETH стреддл с акцентом вниз 2:1 25SEP26 1600")]
	[DataRow(6d, 4d, "ETH стреддл с акцентом вверх 3:2 25SEP26 1600")]
	[DataRow(1.5d, 1d, "ETH стреддл с акцентом вверх 1.5:1 25SEP26 1600")]
	[Description("Акцент стреддла отражает соотношение размеров и направление по доминирующей ноге")]
	// Направление акцента — по доминирующей ноге («вверх» — колл, «вниз» — пут),
	// соотношение N:M — доминирующий размер к подчинённому, целые размеры
	// сокращаются по НОД, дробные выводятся как есть.
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#scenario-accent-reflects-ratio-and-direction
	public void TryIfStraddleAccentReflectsRatioAndDirection(
		double callQuantity,
		double putQuantity,
		string expectedName)
	{
		// Arrange: колл и пут одного страйка и доски с неравными размерами.
		var legs = new[]
		{
			Leg("ETH-25SEP26-1600-C-USDT", "ETH", 2026, 9, 25, 1600, OptionType.Call, callQuantity),
			Leg("ETH-25SEP26-1600-P-USDT", "ETH", 2026, 9, 25, 1600, OptionType.Put, putQuantity),
		};

		// Act: строим имя.
		var name = ConstructionNameBuilder.BuildName("ETH", legs);

		// Assert: имя содержит вид с акцентом и сокращённым соотношением.
		Assert.That(name, Is.EqualTo(expectedName));
	}

	[TestMethod]
	[Description("Стренгл с неравными размерами получает акцент по доминирующей ноге")]
	// Акцент работает и для стренгла: колл+пут разных страйков, направление —
	// по доминирующей ноге.
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#requirement-derived-construction-naming
	public void TryIfStrangleAccentReflectsDominantLeg()
	{
		// Arrange: колл 1800 размером 1 и пут 2100 размером 2.
		var legs = new[]
		{
			Leg("ETH-25SEP26-1800-C-USDT", "ETH", 2026, 9, 25, 1800, OptionType.Call, 1d),
			Leg("ETH-25SEP26-2100-P-USDT", "ETH", 2026, 9, 25, 2100, OptionType.Put, 2d),
		};

		// Act: строим имя.
		var name = ConstructionNameBuilder.BuildName("ETH", legs);

		// Assert: вид — стренгл с акцентом вниз 2:1, страйки по возрастанию.
		Assert.That(name, Is.EqualTo("ETH стренгл с акцентом вниз 2:1 25SEP26 1800/2100"));
	}

	[TestMethod]
	[DataRow(1d, 2d, "BTC колл-спред 1:2 25DEC26 85000/120000")]
	[DataRow(2d, 4d, "BTC колл-спред 1:2 25DEC26 85000/120000")]
	[DataRow(2d, -1d, "BTC колл-спред 2:1 25DEC26 85000/120000")]
	[Description("Спред с неравными размерами ног показывает соотношение в порядке возрастания страйков")]
	// Соотношение спреда считается по суммарным размерам ног каждого страйка
	// по модулю и перечисляется в порядке страйков; целые размеры сокращаются по НОД.
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#requirement-derived-construction-naming
	public void TryIfSpreadRatioShownInStrikeOrderAndReducedByGcd(
		double firstStrikeQuantity,
		double secondStrikeQuantity,
		string expectedName)
	{
		// Arrange: колл-спред 85000/120000 с неравными размерами ног.
		var legs = new[]
		{
			Leg("BTC-25DEC26-85000-C-USDT", "BTC", 2026, 12, 25, 85000, OptionType.Call, firstStrikeQuantity),
			Leg("BTC-25DEC26-120000-C-USDT", "BTC", 2026, 12, 25, 120000, OptionType.Call, secondStrikeQuantity),
		};

		// Act: строим имя.
		var name = ConstructionNameBuilder.BuildName("BTC", legs);

		// Assert: соотношение стоит после вида и перед сегментом доски.
		Assert.That(name, Is.EqualTo(expectedName));
	}

	[TestMethod]
	[Description("Ноги на разных досках перечисляются по возрастанию экспирации с группировкой страйков")]
	public void TryIfMultiBoardLegsGroupedByBoard()
	{
		// Arrange: колл и пут одного страйка на разных досках с равными размерами — стренгл по составу ног.
		var legs = new[]
		{
			Leg("ETH-25DEC26-1600-C-USDT", "ETH", 2026, 12, 25, 1600, OptionType.Call, 1d),
			Leg("ETH-25SEP26-1600-P-USDT", "ETH", 2026, 9, 25, 1600, OptionType.Put, 1d),
		};

		// Act: строим имя.
		var name = ConstructionNameBuilder.BuildName("ETH", legs);

		// Assert: ближняя доска идёт первой, страйки сгруппированы по доскам.
		Assert.That(name, Is.EqualTo("ETH стренгл 25SEP26 1600/25DEC26 1600"));
	}

	[TestMethod]
	[Description("Имя вычисляется только по ненулевым ногам: нулевые и взаимно погасившиеся игнорируются")]
	// Имя производно от живых опционных ног: нога с нулевым остатком и пара ног
	// одного символа, погасившиеся суммой, смысл конструкции не меняют.
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#requirement-derived-construction-naming
	public void TryIfNameIgnoresZeroAndNettedLegs()
	{
		// Arrange: стреддл 1600 и обнулённый колл 2100; плюс колл 2500, схлопнувшийся из +0.5 и -0.5.
		var legs = new[]
		{
			Leg("ETH-25SEP26-1600-C-USDT", "ETH", 2026, 9, 25, 1600, OptionType.Call, 1d),
			Leg("ETH-25SEP26-1600-P-USDT", "ETH", 2026, 9, 25, 1600, OptionType.Put, 1d),
			Leg("ETH-25SEP26-2100-C-USDT", "ETH", 2026, 9, 25, 2100, OptionType.Call, 0d),
			Leg("ETH-25SEP26-2500-C-USDT", "ETH", 2026, 9, 25, 2500, OptionType.Call, 0.5d),
			Leg("ETH-25SEP26-2500-C-USDT", "ETH", 2026, 9, 25, 2500, OptionType.Call, -0.5d),
		};

		// Act: строим имя.
		var name = ConstructionNameBuilder.BuildName("ETH", legs);

		// Assert: имя выводится только из живых ног стреддла 1600.
		Assert.That(name, Is.EqualTo("ETH стреддл 25SEP26 1600"));
	}

	[TestMethod]
	[Description("Повторные исполнения одного символа схлопываются суммой количеств")]
	public void TryIfDuplicateSymbolFillsCollapseIntoOneLeg()
	{
		// Arrange: два частичных исполнения колла по 0.5 и пут размером 1.
		var legs = new[]
		{
			Leg("ETH-25SEP26-1600-C-USDT", "ETH", 2026, 9, 25, 1600, OptionType.Call, 0.5d),
			Leg("ETH-25SEP26-1600-C-USDT", "ETH", 2026, 9, 25, 1600, OptionType.Call, 0.5d),
			Leg("ETH-25SEP26-1600-P-USDT", "ETH", 2026, 9, 25, 1600, OptionType.Put, 1d),
		};

		// Act: строим имя.
		var name = ConstructionNameBuilder.BuildName("ETH", legs);

		// Assert: суммарные размеры 1:1 дают симметричный стреддл без акцента.
		Assert.That(name, Is.EqualTo("ETH стреддл 25SEP26 1600"));
	}

	[TestMethod]
	[Description("Построение имени без ног отклоняется")]
	[ExpectedException(typeof(ArgumentException))]
	public void ThrowOnBuildNameWithoutLegs()
	{
		// Arrange: пустой набор ног.
		var legs = new List<PlannedLeg>();

		// Act: строим имя без ног.
		ConstructionNameBuilder.BuildName("ETH", legs);

		// Assert: ожидается ArgumentException — проверяется атрибутом.
	}

	[TestMethod]
	[Description("Построение имени при полном обнулении ног отклоняется: сохранение имени — обязанность сборки")]
	[ExpectedException(typeof(ArgumentException))]
	// При полном обнулении ног имя не выводится: вызывающий (сборка) обязан
	// сохранить последнее производное имя конструкции.
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#requirement-derived-construction-naming
	public void ThrowOnBuildNameWithAllZeroLegs()
	{
		// Arrange: ноги стреддла, полностью погашенные сделками.
		var legs = new[]
		{
			Leg("ETH-25SEP26-1600-C-USDT", "ETH", 2026, 9, 25, 1600, OptionType.Call, 0d),
			Leg("ETH-25SEP26-1600-P-USDT", "ETH", 2026, 9, 25, 1600, OptionType.Put, 0d),
		};

		// Act: строим имя без живых ног.
		ConstructionNameBuilder.BuildName("ETH", legs);

		// Assert: ожидается ArgumentException — проверяется атрибутом.
	}
}
