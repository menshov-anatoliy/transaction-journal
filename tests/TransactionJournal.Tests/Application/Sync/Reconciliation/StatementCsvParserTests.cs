using Microsoft.VisualStudio.TestTools.UnitTesting;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using NUnit.Framework;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Application.Sync.Reconciliation;

/// <summary>
/// Проверки парсера CSV-выгрузки Bybit AssetChangeDetails на мини-фрагментах:
/// пропуск мета-строки «UID:», заголовок со второй строки, «--» как отсутствие
/// значения, чтение всех частей файлов каталога выгрузки.
/// </summary>
[TestClass]
public class StatementCsvParserTests
{
	/// <summary>Мини-фрагмент UTA-файла: торговая строка и строка фандинга с «--».</summary>
	private const string UtaFragment = """
		UID: 531298197,Company Name: ,Country:
		Uid,Currency,Contract,Type,Direction,Quantity,Position,Filled Price,Funding,Fee Paid,Cash Flow,Change,Wallet Balance,Action,Time(UTC)
		531298197,USDT,XAUTUSDT,TRADE,SELL,0.02000000000000000000,0.02000000000000000000,4356.00000000000000000000,0.00000000000000000000,-0.08712000000000000000,1.03750000000000000000,0.95038000000000000000,306.66408947000000000000,CLOSE,2026-09-22 19:11:48
		531298197,USDT,XAUTUSDT,SETTLEMENT,BUY,--,0.02000000000000000000,--,-0.00436130000000000000,0.00000000000000000000,0.00000000000000000000,-0.00436130000000000000,306.65972817000000000000,SETTLEMENT,2026-09-22 20:00:00
		""";

	/// <summary>Мини-фрагмент второй части UTA-файла для проверки объединения частей.</summary>
	private const string UtaSecondPartFragment = """
		UID: 531298197,Company Name: ,Country:
		Uid,Currency,Contract,Type,Direction,Quantity,Position,Filled Price,Funding,Fee Paid,Cash Flow,Change,Wallet Balance,Action,Time(UTC)
		531298197,USDT,ETHUSDT,TRADE,BUY,0.05000000000000000000,0.05000000000000000000,2510.00000000000000000000,0.00000000000000000000,-0.04518000000000000000,0.00000000000000000000,-0.04518000000000000000,305.41564469000000000000,OPEN,2026-09-18 09:03:24
		""";

	/// <summary>Мини-фрагмент fund-файла: строка Earn с fund-набором колонок.</summary>
	private const string FundFragment = """
		UID: 531298197,Company Name: ,Country:
		Uid,Date & Time(UTC),Coin,QTY,Type,Account Balance,Description
		531298197,2026-09-22 00:41:47,USDT,0.103000000000000000,Earn,1.272700000000000000,Easy Earn | Flexible Interest Distribution
		""";

	[TestMethod]
	[Description("Мини-фрагмент UTA-файла разбирается: тип, сторона, количество, цена, комиссия и время торговой строки корректны")]
	public void TryIfUtaTradeRowAttributesParsed()
	{
		// Arrange: мини-фрагмент UTA-файла с мета-строкой, заголовком и торговой строкой.
		var path = WriteTempFile("AssetChangeDetails_uta_fragment.csv", UtaFragment);
		try
		{
			// Act
			var rows = StatementCsvParser.ParseFile(path, StatementFileKind.Uta);

			// Assert: мета-строка и заголовок пропущены, атрибуты торговой строки разобраны.
			// Traceability: change:reconcile-bybit-statement/design#d4
			Assert.That(rows, Has.Count.EqualTo(2));
			var trade = rows[0];
			Assert.That(trade.FileKind, Is.EqualTo(StatementFileKind.Uta));
			Assert.That(trade.Type, Is.EqualTo("TRADE"));
			Assert.That(trade.Direction, Is.EqualTo("SELL"));
			Assert.That(trade.Contract, Is.EqualTo("XAUTUSDT"));
			Assert.That(trade.Currency, Is.EqualTo("USDT"));
			Assert.That(trade.Quantity, Is.EqualTo(0.02m));
			Assert.That(trade.FilledPrice, Is.EqualTo(4356m));
			Assert.That(trade.FeePaid, Is.EqualTo(-0.08712m));
			Assert.That(trade.TimeUtc, Is.EqualTo(new DateTime(2026, 9, 22, 19, 11, 48, DateTimeKind.Utc)));
			Assert.That(trade.SourceFile, Does.EndWith("AssetChangeDetails_uta_fragment.csv"));
			Assert.That(trade.LineNumber, Is.EqualTo(3));
		}
		finally
		{
			File.Delete(path);
		}
	}

	[TestMethod]
	[Description("Маркер «--» в числовых колонках трактуется как отсутствие значения")]
	public void TryIfDoubleDashParsedAsMissingValue()
	{
		// Arrange: строка фандинга UTA-файла с «--» в количестве и цене.
		var path = WriteTempFile("AssetChangeDetails_uta_fragment.csv", UtaFragment);
		try
		{
			// Act
			var rows = StatementCsvParser.ParseFile(path, StatementFileKind.Uta);

			// Assert: у строки SETTLEMENT количество и цена отсутствуют.
			// Traceability: change:reconcile-bybit-statement/design#d4
			var settlement = rows[1];
			Assert.That(settlement.Type, Is.EqualTo("SETTLEMENT"));
			Assert.That(settlement.Quantity, Is.Null);
			Assert.That(settlement.FilledPrice, Is.Null);
			Assert.That(settlement.FeePaid, Is.EqualTo(0m));
		}
		finally
		{
			File.Delete(path);
		}
	}

	[TestMethod]
	[Description("Каталог выгрузки объединяет все части UTA- и fund-файлов с корректными атрибутами fund-строк")]
	public void TryIfDirectoryMergesAllFileParts()
	{
		// Arrange: каталог с двумя частями UTA-файла и одним fund-файлом.
		var directory = CreateTempDirectory();
		try
		{
			WriteTempFile(Path.Combine(directory, "AssetChangeDetails_uta_531298197_20260101_20260922_1.csv"), UtaSecondPartFragment);
			WriteTempFile(Path.Combine(directory, "AssetChangeDetails_uta_531298197_20260101_20260922_0.csv"), UtaFragment);
			WriteTempFile(Path.Combine(directory, "AssetChangeDetails_fund_531298197_20260101_20260922_0.csv"), FundFragment);

			// Act
			var rows = StatementCsvParser.ParseDirectory(directory);

			// Assert: строки всех трёх частей собраны, fund-строка разобрана по своим колонкам.
			// Traceability: change:reconcile-bybit-statement/design#d4
			Assert.That(rows, Has.Count.EqualTo(4));
			var fund = rows.Single(row => row.FileKind == StatementFileKind.Fund);
			Assert.That(fund.Type, Is.EqualTo("Earn"));
			Assert.That(fund.Currency, Is.EqualTo("USDT"));
			Assert.That(fund.Quantity, Is.EqualTo(0.103m));
			Assert.That(fund.TimeUtc, Is.EqualTo(new DateTime(2026, 9, 22, 0, 41, 47, DateTimeKind.Utc)));
			Assert.That(fund.Contract, Is.Null);
			Assert.That(fund.Direction, Is.Null);
			Assert.That(fund.FilledPrice, Is.Null);
			Assert.That(fund.FeePaid, Is.Null);
			Assert.That(rows.Count(row => row.FileKind == StatementFileKind.Uta), Is.EqualTo(3));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[TestMethod]
	[ExpectedException(typeof(FileNotFoundException))]
	[Description("Отсутствующий файл выгрузки даёт ошибку, а не пустой результат")]
	public void ThrowOnStatementFileMissing()
	{
		// Arrange: путь к файлу, которого не существует.
		var path = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.csv");

		// Act
		StatementCsvParser.ParseFile(path, StatementFileKind.Uta);

		// Assert: ожидается FileNotFoundException (атрибут ExpectedException).
	}

	[TestMethod]
	[ExpectedException(typeof(InvalidDataException))]
	[Description("Заголовок без обязательных колонок даёт ошибку с перечнем недостающих колонок")]
	public void ThrowOnHeaderMissingRequiredColumns()
	{
		// Arrange: файл с заголовком без колонок количества и времени.
		const string fragment = """
			UID: 531298197,Company Name: ,Country:
			Uid,Currency,Contract,Type,Direction
			531298197,USDT,XAUTUSDT,TRADE,BUY
			""";
		var path = WriteTempFile("AssetChangeDetails_uta_fragment.csv", fragment);
		try
		{
			// Act
			StatementCsvParser.ParseFile(path, StatementFileKind.Uta);

			// Assert: ожидается InvalidDataException (атрибут ExpectedException).
		}
		finally
		{
			File.Delete(path);
		}
	}

	[TestMethod]
	[ExpectedException(typeof(InvalidDataException))]
	[Description("Нечисловое значение числовой колонки даёт ошибку с локализацией строки")]
	public void ThrowOnMalformedNumber()
	{
		// Arrange: строка с нечисловым количеством.
		const string fragment = """
			UID: 531298197,Company Name: ,Country:
			Uid,Currency,Contract,Type,Direction,Quantity,Position,Filled Price,Funding,Fee Paid,Cash Flow,Change,Wallet Balance,Action,Time(UTC)
			531298197,USDT,XAUTUSDT,TRADE,SELL,abc,0.02,4356,0,-0.08,0,0,0,CLOSE,2026-09-22 19:11:48
			""";
		var path = WriteTempFile("AssetChangeDetails_uta_fragment.csv", fragment);
		try
		{
			// Act
			StatementCsvParser.ParseFile(path, StatementFileKind.Uta);

			// Assert: ожидается InvalidDataException (атрибут ExpectedException).
		}
		finally
		{
			File.Delete(path);
		}
	}

	/// <summary>
	/// Записывает мини-фрагмент во временный файл.
	/// </summary>
	/// <param name="fileName">Имя файла.</param>
	/// <param name="content">Содержимое CSV.</param>
	/// <returns>Путь к временному файлу.</returns>
	private static string WriteTempFile(string fileName, string content)
	{
		var path = fileName.Contains(Path.DirectorySeparatorChar)
			? fileName
			: Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-{fileName}");
		File.WriteAllText(path, content.TrimStart());
		return path;
	}

	/// <summary>
	/// Создаёт пустой временный каталог.
	/// </summary>
	/// <returns>Путь к временному каталогу.</returns>
	private static string CreateTempDirectory()
	{
		var directory = Path.Combine(Path.GetTempPath(), $"recon-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		return directory;
	}
}
