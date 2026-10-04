namespace TransactionJournal.Tests.Hints.Engine;

using NUnit.Framework;
using TransactionJournal.Hints.Corpus;
using TransactionJournal.Hints.Engine;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// Проверки рендера подсказок: шаблон заполняется фактами, чёткость задаёт
/// формулировку — чёткое правило звучит императивом, размытое получает префикс
/// «[решение]»; нехватка факта или шаблона у сработавшего триггера — нарушение
/// контракта.
/// </summary>
[TestClass]
public class HintTextRendererTests
{
	[TestMethod]
	[Description("Чёткое правило рендерится шаблоном с подстановкой фактов без префикса")]
	// Императив прямого действия: текст ровно как в hintTemplate карточки.
	// Traceability: openspec:hints/engine-pass#requirement-engine-clarity-shapes-wording
	public void TryIfCrispCardRendersTemplateWithFacts()
	{
		var card = HintPassHarness.MakeCard(
			"ac-01",
			hintTemplate: "Убыток за {period}: {lossPct}% при лимите {limitPct}%.");
		var facts = new Dictionary<string, string>
		{
			["period"] = "неделя",
			["lossPct"] = "2",
			["limitPct"] = "1",
		};

		var text = HintTextRenderer.Render(card, facts);

		Assert.That(text, Is.EqualTo("Убыток за неделя: 2% при лимите 1%."));
	}

	[TestMethod]
	[Description("Размытое правило получает префикс «[решение]» перед текстом шаблона")]
	// Размытое правило — предмет оценки человека: префикс отделяет его от
	// императива чётких правил.
	// Traceability: openspec:hints/engine-pass#requirement-engine-clarity-shapes-wording
	public void TryIfFuzzyCardGetsSolutionPrefix()
	{
		var card = HintPassHarness.MakeCard(
			"ac-14",
			clarity: RuleClarity.Fuzzy,
			hintTemplate: "Результат {pnlPct}% — кандидат на разборку.");
		var facts = new Dictionary<string, string> { ["pnlPct"] = "12" };

		var text = HintTextRenderer.Render(card, facts);

		Assert.That(text, Is.EqualTo("[решение] Результат 12% — кандидат на разборку."));
	}

	[TestMethod]
	[Description("Чёткость отображается строкой самоописательной записи: crisp/fuzzy")]
	// Запись подсказки денормализует чёткость строкой на момент генерации.
	// Traceability: openspec:hints/engine-pass#requirement-engine-self-describing-record
	public void TryIfClarityMappedToRecordText()
	{
		Assert.That(HintTextRenderer.ClarityText(RuleClarity.Crisp), Is.EqualTo("crisp"));
		Assert.That(HintTextRenderer.ClarityText(RuleClarity.Fuzzy), Is.EqualTo("fuzzy"));
	}

	[TestMethod]
	[Description("Сработавший триггер без факта подстановки шаблона ломает рендер исключением")]
	// Нехватка факта — нарушение контракта триггера: молчаливая подстановка
	// пустоты исказила бы смысл подсказки.
	// Traceability: openspec:hints/engine-pass#requirement-engine-clarity-shapes-wording
	[ExpectedException(typeof(InvalidOperationException))]
	public void ThrowOnMissingFactForTemplate()
	{
		var card = HintPassHarness.MakeCard("ac-27", hintTemplate: "Цена ушла от страйка на {shiftPct}%.");

		HintTextRenderer.Render(card, new Dictionary<string, string>());
	}

	[TestMethod]
	[Description("Сработавший триггер карточки без hintTemplate ломает рендер исключением")]
	// Пустой шаблон не имеет осмысленного текста подсказки: карточка с
	// машинным триггером обязана нести шаблон.
	// Traceability: openspec:hints/engine-pass#requirement-engine-clarity-shapes-wording
	[ExpectedException(typeof(InvalidOperationException))]
	public void ThrowOnEmptyHintTemplate()
	{
		var card = HintPassHarness.MakeCard("ac-43");

		HintTextRenderer.Render(card, new Dictionary<string, string>());
	}
}
