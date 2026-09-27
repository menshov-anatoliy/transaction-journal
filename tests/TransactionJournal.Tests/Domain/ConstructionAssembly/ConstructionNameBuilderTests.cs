using NUnit.Framework;
using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain.ConstructionAssembly;

/// <summary>
/// Проверки детерминированного именования конструкций из состава открывающего
/// окна: стреддл, стренгл, колл-спред, пут-спред, CALL и PUT на примерах
/// символов из истории.
/// </summary>
[TestClass]
public class ConstructionNameBuilderTests
{
	/// <summary>Разобранная нога открывающего окна для построения имени.</summary>
	private static OptionSymbolParts Leg(string symbol, string baseCoin, int year, int month, int day, double strike, OptionType type) => new()
	{
		BaseCoin = baseCoin,
		ExpiryDate = new DateTime(year, month, day),
		Strike = (decimal)strike,
		Type = type,
	};

	[TestMethod]
	[Description("Имена стреддла и колл-спреда совпадают с примерами из истории")]
	// Имя строится из состава открывающих ног по шаблону «{Актив} {вид} {доска} {страйки}».
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#requirement-full-rebuild-semantics
	public void TryIfNameMatchesHistoryExamples()
	{
		// Arrange: стреддл ETH 1600 и колл-спред BTC 85000/120000.
		var straddleLegs = new[]
		{
			Leg("ETH-25SEP26-1600-C-USDT", "ETH", 2026, 9, 25, 1600, OptionType.Call),
			Leg("ETH-25SEP26-1600-P-USDT", "ETH", 2026, 9, 25, 1600, OptionType.Put),
		};
		var callSpreadLegs = new[]
		{
			Leg("BTC-25DEC26-120000-C-USDT", "BTC", 2026, 12, 25, 120000, OptionType.Call),
			Leg("BTC-25DEC26-85000-C-USDT", "BTC", 2026, 12, 25, 85000, OptionType.Call),
		};

		// Act: строим имена.
		var straddleName = ConstructionNameBuilder.BuildName("ETH", straddleLegs);
		var callSpreadName = ConstructionNameBuilder.BuildName("BTC", callSpreadLegs);

		// Assert: имена воспроизводят контрольные примеры; страйки упорядочены по возрастанию.
		Assert.That(straddleName, Is.EqualTo("ETH стреддл 25SEP26 1600"));
		Assert.That(callSpreadName, Is.EqualTo("BTC колл-спред 25DEC26 85000/120000"));
	}

	[TestMethod]
	[DataRow("ETH", 2026, 9, 25, 1800d, OptionType.Call, 2100d, OptionType.Put, "ETH стренгл 25SEP26 1800/2100")]
	[DataRow("ETH", 2026, 9, 25, 1600d, OptionType.Put, 2100d, OptionType.Put, "ETH пут-спред 25SEP26 1600/2100")]
	[DataRow("ETH", 2026, 9, 25, 2100d, OptionType.Call, 2100d, OptionType.Call, "ETH CALL 25SEP26 2100")]
	[DataRow("ETH", 2026, 9, 25, 2100d, OptionType.Put, 2100d, OptionType.Put, "ETH PUT 25SEP26 2100")]
	[DataRow("ETH", 2026, 12, 25, 1600d, OptionType.Call, 1600d, OptionType.Put, "ETH стреддл 25DEC26 1600")]
	[Description("Вид конструкции выводится из состава ног: стренгл, пут-спред, направленные CALL и PUT")]
	public void TryIfKindIsDerivedFromLegComposition(
		string baseCoin,
		int year,
		int month,
		int day,
		double firstStrike,
		OptionType firstType,
		double secondStrike,
		OptionType secondType,
		string expectedName)
	{
		// Arrange: две ноги открывающего окна на одной доске.
		var legs = new[]
		{
			Leg("symbol-1", baseCoin, year, month, day, firstStrike, firstType),
			Leg("symbol-2", baseCoin, year, month, day, secondStrike, secondType),
		};

		// Act: строим имя.
		var name = ConstructionNameBuilder.BuildName(baseCoin, legs);

		// Assert: имя соответствует виду и составу ног.
		Assert.That(name, Is.EqualTo(expectedName));
	}

	[TestMethod]
	[Description("Ноги на разных досках перечисляются по возрастанию экспирации с группировкой страйков")]
	public void TryIfMultiBoardLegsGroupedByBoard()
	{
		// Arrange: колл и пут одного страйка на разных досках — стренгл по составу ног.
		var legs = new[]
		{
			Leg("ETH-25DEC26-1600-C-USDT", "ETH", 2026, 12, 25, 1600, OptionType.Call),
			Leg("ETH-25SEP26-1600-P-USDT", "ETH", 2026, 9, 25, 1600, OptionType.Put),
		};

		// Act: строим имя.
		var name = ConstructionNameBuilder.BuildName("ETH", legs);

		// Assert: ближняя доска идёт первой, страйки сгруппированы по доскам.
		Assert.That(name, Is.EqualTo("ETH стренгл 25SEP26 1600/25DEC26 1600"));
	}

	[TestMethod]
	[Description("Построение имени без ног отклоняется")]
	[ExpectedException(typeof(ArgumentException))]
	public void ThrowOnBuildNameWithoutLegs()
	{
		// Arrange: пустой набор ног открывающего окна.
		var legs = new List<TransactionJournal.Materialization.OptionSymbolParts>();

		// Act: строим имя без ног.
		ConstructionNameBuilder.BuildName("ETH", legs);

		// Assert: ожидается ArgumentException — проверяется атрибутом.
	}
}
