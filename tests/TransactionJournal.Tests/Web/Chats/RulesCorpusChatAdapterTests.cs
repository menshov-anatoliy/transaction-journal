namespace TransactionJournal.Tests.Web.Chats;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Chats;
using TransactionJournal.Hints.Corpus;
using TransactionJournal.Tests.Hints.Corpus;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Проверки адаптера корпуса правил для чата в composition root: пороги
/// карточки читаются по идентификатору парами имя-величина-единица без
/// рендеринга полного текста, отсутствие карточки даёт пустой список без
/// исключения, а пустой идентификатор отклоняется контрактом порта.
/// Traceability: openspec:chats/context#scenario-chat-context-portfolio-snapshot-without-construction
/// </summary>
[TestClass]
public class RulesCorpusChatAdapterTests
{
	[TestMethod]
	[Description("Пороги карточки читаются по идентификатору без полного текста карточки")]
	// Проверяем чтение порогов адаптером корпуса: карточка с тремя периодными
	// лимитами отдаётся парами имя-величина-единица в порядке карточки, а
	// описания триггера и действия в результат порогов не попадают.
	// Traceability: openspec:chats/context#scenario-chat-context-portfolio-snapshot-without-construction
	public async Task TryIfCardThresholdsReadByIdWithoutFullText()
	{
		// Arrange: каталог корпуса с карточкой порогов периодных лимитов риска.
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-lim.yaml", CorpusYaml.Card("tc-lim",
				implementation: "risk-limit-period",
				thresholds:
				[
					("weeklyRiskLimit", "1", "percent"),
					("monthlyRiskLimit", "5", "percent"),
					("quarterlyRiskLimit", "10", "percent"),
				]));
			var adapter = new RulesCorpusChatAdapter(new RulesCorpusLoader(dir));

			// Act: читаем пороги карточки по идентификатору.
			var thresholds = await adapter.ReadCardThresholdsAsync("tc-lim");

			// Assert: пороги отданы парами имя-величина-единица в порядке карточки.
			Assert.That(thresholds, Has.Count.EqualTo(3));
			Assert.That(thresholds[0].Name, Is.EqualTo("weeklyRiskLimit"));
			Assert.That(thresholds[0].Value, Is.EqualTo("1"));
			Assert.That(thresholds[0].Unit, Is.EqualTo("percent"));
			Assert.That(thresholds[1].Name, Is.EqualTo("monthlyRiskLimit"));
			Assert.That(thresholds[1].Value, Is.EqualTo("5"));
			Assert.That(thresholds[2].Name, Is.EqualTo("quarterlyRiskLimit"));
			Assert.That(thresholds[2].Value, Is.EqualTo("10"));
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[Description("Неизвестный идентификатор карточки даёт пустой список порогов")]
	// Проверяем отсутствие карточки: при валидном корпусе без запрошенной
	// карточки читатель отдаёт пустой список без исключения, деградация
	// не ломает снимок контекста.
	// Traceability: openspec:chats/context#scenario-chat-context-portfolio-snapshot-without-construction
	public async Task TryIfUnknownCardGivesEmptyThresholds()
	{
		// Arrange: валидный корпус с одной карточкой, запрос идёт по другому id.
		var dir = CorpusYaml.TempDir();
		try
		{
			CorpusYaml.Write(dir, "tc-other.yaml", CorpusYaml.Card("tc-other"));
			var adapter = new RulesCorpusChatAdapter(new RulesCorpusLoader(dir));

			// Act: читаем пороги несуществующей карточки.
			var thresholds = await adapter.ReadCardThresholdsAsync("tc-absent");

			// Assert: результат — пустой список, исключение не поднимается.
			Assert.That(thresholds, Is.Empty);
		}
		finally
		{
			CorpusYaml.DeleteDir(dir);
		}
	}

	[TestMethod]
	[ExpectedException(typeof(ArgumentException))]
	[DataRow("")]
	[DataRow(" ")]
	[Description("Пустой или пробельный идентификатор карточки отклоняется")]
	// Проверяем негативный контракт чтения порогов: пустой и пробельный
	// идентификатор отклоняются аргумент-исключением до чтения корпуса.
	// Traceability: openspec:chats/context#scenario-chat-context-portfolio-snapshot-without-construction
	public void ThrowOnNullOrWhitespaceCardId(string cardId)
	{
		// Arrange: адаптер корпуса без чтения каталога — отклонение раньше.

		// Act / Assert: чтение порогов поднимает ArgumentException.
		_ = new RulesCorpusChatAdapter(new RulesCorpusLoader("missing-corpus-dir")).ReadCardThresholdsAsync(cardId);
	}
}
