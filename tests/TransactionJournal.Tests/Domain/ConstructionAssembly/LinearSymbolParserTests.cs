using NUnit.Framework;
using TransactionJournal.Domain.ConstructionAssembly;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain.ConstructionAssembly;

/// <summary>
/// Проверки разбора linear-символа: базовый актив получается отбрасыванием
/// хвостового суффикса USDT и сопоставляется с опционными активами точно.
/// </summary>
[TestClass]
public class LinearSymbolParserTests
{
	[TestMethod]
	[DataRow("ETHUSDT", "ETH")]
	[DataRow("BTCUSDT", "BTC")]
	[DataRow("SOLUSDT", "SOL")]
	[Description("Базовый актив linear-символа получается отбрасыванием суффикса USDT")]
	// Разбор linear-символа из истории: актив нужен для сопоставления с опционными конструкциями.
	// Traceability: change:add-construction-auto-assembly/tasks#1-2
	public void TryIfBaseCoinDropsUsdtSuffix(string symbol, string expectedBaseCoin)
	{
		// Act: разбираем символ фьючерса.
		var parsed = LinearSymbolParser.TryParseBaseCoin(symbol, out var baseCoin);

		// Assert: актив выделен без суффикса.
		Assert.That(parsed, Is.True);
		Assert.That(baseCoin, Is.EqualTo(expectedBaseCoin));
	}

	[TestMethod]
	[DataRow("ETHBTC")]
	[DataRow("USDT")]
	[DataRow("")]
	[DataRow(" ")]
	[DataRow(null)]
	[Description("Символ без суффикса USDT, без актива или пустой не разбирается")]
	public void TryIfForeignSymbolsAreRejected(string? symbol)
	{
		// Act: разбираем символ вне формата linear-фьючерса журнала.
		var parsed = LinearSymbolParser.TryParseBaseCoin(symbol, out var baseCoin);

		// Assert: разбор не проходит, актив не определён.
		Assert.That(parsed, Is.False);
		Assert.That(baseCoin, Is.Null);
	}
}
