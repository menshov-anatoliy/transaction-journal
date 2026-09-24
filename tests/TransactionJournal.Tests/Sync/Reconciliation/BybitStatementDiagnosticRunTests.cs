using NUnit.Framework;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Sync.Reconciliation;

/// <summary>
/// Разовый диагностический прогон сверки: живая база журнала сопоставляется
/// с CSV-выгрузкой Bybit AssetChangeDetails из каталога примеров только на
/// чтение; прогон пропускается без живых данных, при расхождениях падает
/// с полным отчётом.
/// Traceability: openspec:sync/bybit-statement-reconciliation#requirement-one-off-diagnostic-run
/// </summary>
[TestClass]
public class BybitStatementDiagnosticRunTests
{
	/// <summary>
	/// Контекст теста для вывода полного отчёта сверки в лог прогона:
	/// тип квалифицирован полностью, чтобы не конфликтовать с NUnit TestContext.
	/// </summary>
	public Microsoft.VisualStudio.TestTools.UnitTesting.TestContext? TestContext { get; set; }

	[TestMethod]
	[Description("Разовая сверка живой базы журнала с CSV-выгрузкой Bybit AssetChangeDetails проходит без расхождений")]
	public void TryIfLiveDataReconcilesWithBybitStatement()
	{
		// Arrange: разрешение живых данных машины — базы журнала и каталога выгрузки.
		var paths = ReconciliationPaths.ResolveFromTestAssembly();

		// Отсутствие живых данных пропускает прогон без ошибки: сверка предназначена
		// для локальной диагностической машины, а не для любого окружения. Вызов
		// квалифицирован полностью — NUnit-алиас Assert раннер MSTest пропуском не считает.
		// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-live-data-missing-run-skips
		if (paths.IsSkippable)
		{
			var missing = new List<string>();
			if (paths.JournalDbMissing)
			{
				missing.Add($"база журнала ({paths.JournalDbPath})");
			}

			if (paths.StatementDirectoryMissing)
			{
				missing.Add($"каталог выгрузки ({paths.StatementDirectory})");
			}

			Microsoft.VisualStudio.TestTools.UnitTesting.Assert.Inconclusive(
				$"Прогон сверки пропущен: на машине нет живых данных — отсутствуют {string.Join(" и ", missing)}.");
			return;
		}

		var statementRows = StatementCsvParser.ParseDirectory(paths.StatementDirectory);
		Assert.That(statementRows, Is.Not.Empty, "каталог выгрузки существует, но не содержит строк");

		// Диапазон сверки берётся из строк UTA-файла, а не из имени каталога:
		// границы — минимальное и максимальное время строк включительно.
		// Traceability: change:reconcile-bybit-statement/design#d3
		var utaRows = statementRows.Where(row => row.FileKind == StatementFileKind.Uta).ToList();
		if (utaRows.Count == 0)
		{
			Microsoft.VisualStudio.TestTools.UnitTesting.Assert.Inconclusive(
				"Прогон сверки пропущен: выгрузка не содержит строк UTA-файла — диапазон сверки не определён.");
			return;
		}

		var fromMs = new DateTimeOffset(utaRows.Min(row => row.TimeUtc), TimeSpan.Zero).ToUnixTimeMilliseconds();

		// Верхняя граница расширяется до конца последней секунды: строки выгрузки
		// имеют точность до секунды, а записи журнала — до миллисекунд.
		var toMs = new DateTimeOffset(utaRows.Max(row => row.TimeUtc), TimeSpan.Zero).ToUnixTimeMilliseconds() + 999;

		var journal = new JournalDbReader(paths.JournalDbPath).ReadInRange(fromMs, toMs);

		var tradeRows = new List<StatementRow>();
		var deliveryRows = new List<StatementRow>();
		foreach (var row in statementRows)
		{
			switch (StatementRowClassifier.Classify(row))
			{
				case StatementRowCategory.Trade:
					tradeRows.Add(row);
					break;
				case StatementRowCategory.Delivery:
					deliveryRows.Add(row);
					break;
				default:
					// Строки вне области и неизвестные типы не сверяются: отчёт сводит
					// их по всем строкам выгрузки отдельной секцией.
					break;
			}
		}

		// Act: сопоставление обеих сверяемых областей и построение отчёта.
		var trades = StatementMatcher.MatchTrades(tradeRows, journal.Executions);
		var deliveries = StatementMatcher.MatchDeliveries(deliveryRows, journal.Deliveries);
		var report = ReconciliationReport.Build(statementRows, journal, trades, deliveries);
		var reportText = report.Render();
		TestContext?.WriteLine(reportText);

		// Assert: расхождения сверяемой области проваливают прогон текстом отчёта;
		// чистая сверка проходит успешно, отчёт уже выведен в лог прогона выше.
		// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-discrepancies-fail-with-report
		// Traceability: openspec:sync/bybit-statement-reconciliation#scenario-clean-reconciliation-passes
		if (report.Summary.HasDiscrepancies)
		{
			Assert.Fail(reportText);
		}

		Assert.That(report.Summary.MatchedTrades + report.Summary.TradeAttributeMismatches
			+ report.Summary.TradesMissingInJournal, Is.EqualTo(report.Summary.TradeRows),
			"все торговые строки выгрузки должны получить исход: пара, расхождение или отсутствие в журнале");
		Assert.That(report.Summary.TradesMissingInStatement, Is.EqualTo(journal.Executions
			.Where(StatementMatcher.IsTradeExecution).Count()
			- report.Summary.MatchedTrades - report.Summary.TradeAttributeMismatches),
			"все записи исполнения сверяемой TRADE-вселенной в диапазоне должны получить исход: пара, расхождение или отсутствие в выгрузке");
	}
}
