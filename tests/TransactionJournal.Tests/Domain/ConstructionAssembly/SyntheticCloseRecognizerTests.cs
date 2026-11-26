using System.Globalization;
using NUnit.Framework;
using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Domain.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain.ConstructionAssembly;

/// <summary>
/// Проверки чистой функции распознавания синтетических связок по шаблону
/// правила ac-43: распознавание частичного закрытия пута, отказ для скальпов
/// без встречного опциона, дробные исполнения — связка покрывает минимум
/// непокрытых остатков сторон, одно исполнение входит в несколько связок,
/// окно 60 минут, детерминизм повторного вычисления и реальный кейс
/// 15.10.2026 по XAUT.
/// </summary>
[TestClass]
public class SyntheticCloseRecognizerTests
{
	private const string XautCall4400 = "XAUT-30OCT26-4400-C-USDT";
	private const string XautPut4400 = "XAUT-30OCT26-4400-P-USDT";
	private const string XautPerp = "XAUTUSDT";

	#region Помощники

	/// <summary>Момент времени UTC в мс Unix-эпохи для записей тестов.</summary>
	private static long Ms(int year, int month, int day, int hour = 10, int minute = 0) =>
		new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	/// <summary>Опционное исполнение с знаковым количеством.</summary>
	private static AssemblyExecution Option(string execId, string symbol, long timeMs, decimal quantity) => new()
	{
		ExecId = execId,
		Category = "option",
		Symbol = symbol,
		ExecTimeMs = timeMs,
		SignedQuantity = quantity,
	};

	/// <summary>Линейное исполнение — сделка по вечному фьючерсу.</summary>
	private static AssemblyExecution Linear(string execId, string symbol, long timeMs, decimal quantity) => new()
	{
		ExecId = execId,
		Category = "linear",
		Symbol = symbol,
		ExecTimeMs = timeMs,
		SignedQuantity = quantity,
	};

	/// <summary>Стреддл 0.5/0.5 страйка 4400 с доской 30OCT26 — открытые ноги конструкции.</summary>
	private static List<AssemblyExecution> XautStraddleHalfLegs() =>
	[
		Option("open-call", XautCall4400, Ms(2026, 9, 30, 12, 0), 0.5m),
		Option("open-put", XautPut4400, Ms(2026, 9, 30, 12, 5), 0.5m),
	];

	#endregion

	#region Распознавание связок ac-43

	[TestMethod]
	[Description("Покупка фьючерса и продажа колла той же доски в пределах окна распознаются как закрытие части пута")]
	// Связка ac-43: длинный пут закрывается покупкой фьючерса и продажей
	// встречного колла; связка указывает на путовую ногу, её доску и закрываемую часть.
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-synthetic-put-close-recognized
	public void TryIfSyntheticPutCloseIsRecognized()
	{
		// Arrange: стреддл 0.5/0.5 страйка 4400 и связка «покупка 0.3 фьючерса + продажа 0.3 колла».
		var executions = XautStraddleHalfLegs();
		executions.Add(Linear("hedge-perp", XautPerp, Ms(2026, 10, 15, 14, 0), 0.3m));
		executions.Add(Option("hedge-call", XautCall4400, Ms(2026, 10, 15, 14, 4), -0.3m));

		// Act: распознаём связки.
		var links = SyntheticCloseRecognizer.Recognize(executions);

		// Assert: одна связка указывает на путовую ногу и закрывает 0.3 из 0.5.
		Assert.That(links, Has.Count.EqualTo(1));
		var link = links[0];
		Assert.That(link.LinearExecId, Is.EqualTo("hedge-perp"), "Линейная сторона связки воспроизводится");
		Assert.That(link.CounterOptionExecId, Is.EqualTo("hedge-call"), "Встречная опционная сторона связки воспроизводится");
		Assert.That(link.ClosedLegSymbol, Is.EqualTo(XautPut4400), "Связка указывает на закрываемый пут");
		Assert.That(link.ClosedLegType, Is.EqualTo(OptionType.Put), "Тип закрываемой ноги — пут");
		Assert.That(link.Quantity, Is.EqualTo(0.3m), "Количество связки равно количествам обеих сделок");
		Assert.That(link.Strike, Is.EqualTo(4400m), "Страйк доски связки совпадает с открытой ногой");
		Assert.That(link.BoardExpiryDate, Is.EqualTo(new DateTime(2026, 10, 30)), "Доска связки совпадает с открытой ногой");
		Assert.That(link.BaseCoin, Is.EqualTo("XAUT"), "Базовый актив связки един для обеих сделок");
	}

	[TestMethod]
	[Description("Линейная сделка без встречного опциона той же доски не связывается")]
	// Скальп линейной ноги без встречной опционной сделки в окне остаётся
	// обычным остатком линейной ноги и не образует связку.
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-scalp-without-counter-option-not-linked
	public void TryIfScalpWithoutCounterOptionStaysUnlinked()
	{
		// Arrange: стреддл и одиночная покупка 0.04 фьючерса — скальп без встречного опциона.
		var executions = XautStraddleHalfLegs();
		executions.Add(Linear("scalp-perp", XautPerp, Ms(2026, 10, 15, 15, 0), 0.04m));

		// Act: распознаём связки.
		var links = SyntheticCloseRecognizer.Recognize(executions);

		// Assert: связки нет — сделка осталась обычным остатком линейной ноги.
		Assert.That(links, Is.Empty);
	}

	[TestMethod]
	[DataRow("0.3", "0.2")]
	[DataRow("0.2", "0.3")]
	[Description("Несогласованные количества связываются дробно: связка покрывает минимум остатков сторон")]
	// Дробное исполнение образует связку: количество связки — минимум
	// непокрытых остатков линейной сделки и встречного опциона, непокрытые
	// остатки сторон остаются вне связок.
	// Traceability: issue:#100
	public void TryIfQuantityMismatchLinksPartialCoverage(string linearQuantityText, string optionQuantityText)
	{
		// Arrange: стреддл и сделки с разными количествами в пределах окна.
		var linearQuantity = decimal.Parse(linearQuantityText, CultureInfo.InvariantCulture);
		var optionQuantity = decimal.Parse(optionQuantityText, CultureInfo.InvariantCulture);
		var executions = XautStraddleHalfLegs();
		executions.Add(Linear("hedge-perp", XautPerp, Ms(2026, 10, 15, 14, 0), linearQuantity));
		executions.Add(Option("hedge-call", XautCall4400, Ms(2026, 10, 15, 14, 4), -optionQuantity));

		// Act: распознаём связки.
		var links = SyntheticCloseRecognizer.Recognize(executions);

		// Assert: одна связка покрывает минимум остатков сторон — 0.2.
		Assert.That(links, Has.Count.EqualTo(1));
		Assert.That(links[0].Quantity, Is.EqualTo(0.2m), "Связка покрывает минимум остатков сторон");
		Assert.That(links[0].LinearExecId, Is.EqualTo("hedge-perp"));
		Assert.That(links[0].CounterOptionExecId, Is.EqualTo("hedge-call"));
	}

	[TestMethod]
	[Description("Пять дробных покупок по 0.1 исчерпывают встречный опцион 0.5 связками по 0.1")]
	// Одно встречное исполнение может входить в несколько связок: у записи
	// остаётся непокрытый остаток, исчерпанная запись дальнейших связок
	// не образует.
	// Traceability: issue:#100
	public void TryIfFivePartialLinearsExhaustCounterOption()
	{
		// Arrange: стреддл, продажа 0.5 колла и шесть покупок фьючерса по 0.1
		// в пределах окна — шестая покупка остаётся при исчерпанном опционе.
		var executions = XautStraddleHalfLegs();
		executions.Add(Option("hedge-call", XautCall4400, Ms(2026, 10, 15, 14, 30), -0.5m));
		executions.Add(Linear("buy-1", XautPerp, Ms(2026, 10, 15, 14, 0), 0.1m));
		executions.Add(Linear("buy-2", XautPerp, Ms(2026, 10, 15, 14, 10), 0.1m));
		executions.Add(Linear("buy-3", XautPerp, Ms(2026, 10, 15, 14, 20), 0.1m));
		executions.Add(Linear("buy-4", XautPerp, Ms(2026, 10, 15, 14, 30), 0.1m));
		executions.Add(Linear("buy-5", XautPerp, Ms(2026, 10, 15, 14, 40), 0.1m));
		executions.Add(Linear("buy-6", XautPerp, Ms(2026, 10, 15, 14, 50), 0.1m));

		// Act: распознаём связки.
		var links = SyntheticCloseRecognizer.Recognize(executions);

		// Assert: пять связок по 0.1 с одним встречным исполнением; шестая
		// покупка не даёт связку — у опциона нулевой остаток.
		Assert.That(links, Has.Count.EqualTo(5), "Шесть покупок дают пять связок — опцион 0.5 исчерпан");
		Assert.That(links.Select(link => link.Quantity), Is.All.EqualTo(0.1m), "Каждая связка покрывает минимум остатков — 0.1");
		Assert.That(links.Select(link => link.CounterOptionExecId), Is.All.EqualTo("hedge-call"), "Одно опционное исполнение входит во все связки");
		Assert.That(links.Select(link => link.LinearExecId), Is.EqualTo(new[] { "buy-1", "buy-2", "buy-3", "buy-4", "buy-5" }), "Связки идут в порядке прогона линейных сделок");
		Assert.That(links.Select(link => link.ClosedLegSymbol), Is.All.EqualTo(XautPut4400), "Дробные связки закрывают ту же путовую ногу");
	}

	[TestMethod]
	[Description("Встречный опцион тремя частями по 0.1 даёт три связки против линейной 0.5 с остатком 0.2")]
	// Линейное исполнение тоже может входить в несколько связок: три продажи
	// опциона по 0.1 исчерпываются, у линейной сделки остаётся непокрытый
	// остаток 0.2, полного покрытия не требуется.
	// Traceability: issue:#100
	public void TryIfSplitCounterOptionLinksPartialLinear()
	{
		// Arrange: стреддл, покупка 0.5 фьючерса и три продажи колла по 0.1 рядом.
		var executions = XautStraddleHalfLegs();
		executions.Add(Linear("hedge-perp", XautPerp, Ms(2026, 10, 15, 14, 0), 0.5m));
		executions.Add(Option("sell-call-1", XautCall4400, Ms(2026, 10, 15, 14, 5), -0.1m));
		executions.Add(Option("sell-call-2", XautCall4400, Ms(2026, 10, 15, 14, 10), -0.1m));
		executions.Add(Option("sell-call-3", XautCall4400, Ms(2026, 10, 15, 14, 15), -0.1m));

		// Act: распознаём связки.
		var links = SyntheticCloseRecognizer.Recognize(executions);

		// Assert: три связки по 0.1 против одной линейной сделки; остаток 0.2
		// линейной сделки не покрыт — связки исчерпали опционные части.
		Assert.That(links, Has.Count.EqualTo(3), "Три продажи по 0.1 дают три связки");
		Assert.That(links.Select(link => link.Quantity), Is.All.EqualTo(0.1m), "Каждая связка покрывает минимум остатков — 0.1");
		Assert.That(links.Sum(link => link.Quantity), Is.EqualTo(0.3m), "Связки покрывают 0.3 из 0.5 линейной сделки");
		Assert.That(links.Select(link => link.LinearExecId), Is.All.EqualTo("hedge-perp"), "Одно линейное исполнение входит во все связки");
		Assert.That(links.Select(link => link.CounterOptionExecId), Is.EqualTo(new[] { "sell-call-1", "sell-call-2", "sell-call-3" }), "Связки идут в порядке прогона опционных частей");
	}

	[TestMethod]
	[Description("Вторая пара не связывается, когда открытая часть ноги уже исчерпана покрытым")]
	// Закрытие не больше открытой части закрываемой ноги с учётом уже
	// покрытого связками: после покрытия 0.4 из 0.5 пара 0.3/0.3 связки
	// не образует.
	// Traceability: issue:#100
	public void TryIfSecondPairStaysUnlinkedWhenClosedLegCoverageSpent()
	{
		// Arrange: стреддл 0.5 и две пары «продажа колла + покупка фьючерса» —
		// 0.4/0.4 и 0.3/0.3 в пределах окна.
		var executions = XautStraddleHalfLegs();
		executions.Add(Option("call-1", XautCall4400, Ms(2026, 10, 15, 14, 0), -0.4m));
		executions.Add(Linear("perp-1", XautPerp, Ms(2026, 10, 15, 14, 5), 0.4m));
		executions.Add(Option("call-2", XautCall4400, Ms(2026, 10, 15, 14, 10), -0.3m));
		executions.Add(Linear("perp-2", XautPerp, Ms(2026, 10, 15, 14, 15), 0.3m));

		// Act: распознаём связки.
		var links = SyntheticCloseRecognizer.Recognize(executions);

		// Assert: первая пара связана на 0.4, второй паре не хватает открытой
		// части ноги — осталось 0.1, а пары 0.3 не хватает.
		Assert.That(links, Has.Count.EqualTo(1), "Вторая пара сверх открытой части ноги не связывается");
		Assert.That(links[0].Quantity, Is.EqualTo(0.4m));
		Assert.That(links[0].LinearExecId, Is.EqualTo("perp-1"));
		Assert.That(links[0].CounterOptionExecId, Is.EqualTo("call-1"));
	}

	[TestMethod]
	[Description("Покупка встречного опциона той же доски в окне и количестве связкой не считается")]
	// Встречный опцион в связке ac-43 только продаётся: покупка колла той же
	// доски, окна и количества — самостоятельная сделка, нога остаётся
	// с полным остатком.
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-synthetic-put-close-recognized
	public void TryIfBoughtCounterOptionStaysUnlinked()
	{
		// Arrange: стреддл, покупка 0.3 фьючерса и покупка 0.3 колла рядом.
		var executions = XautStraddleHalfLegs();
		executions.Add(Linear("hedge-perp", XautPerp, Ms(2026, 10, 15, 14, 0), 0.3m));
		executions.Add(Option("bought-call", XautCall4400, Ms(2026, 10, 15, 14, 4), 0.3m));

		// Act: распознаём связки.
		var links = SyntheticCloseRecognizer.Recognize(executions);

		// Assert: связки нет — покупка встречного опциона не закрывает пут.
		Assert.That(links, Is.Empty);
	}

	[TestMethod]
	[DataRow(60, 1)]
	[DataRow(61, 0)]
	[Description("Связка распознаётся при зазоре ровно 60 минут и не распознаётся при большем зазоре")]
	// Окно связки совпадает с константой окна сборки: 60 минут включительно,
	// больший зазор оставляет сделки несвязанными.
	// Traceability: change:synthetic-close-logical-layer/design#d2
	public void TryIfLinkMatchesWithinSixtyMinutesGap(int gapMinutes, int expectedLinkCount)
	{
		// Arrange: стреддл и сделки с зазором gapMinutes между линейной и опционной.
		var executions = XautStraddleHalfLegs();
		executions.Add(Linear("hedge-perp", XautPerp, Ms(2026, 10, 15, 14, 0), 0.3m));
		executions.Add(Option("hedge-call", XautCall4400, Ms(2026, 10, 15, 14, 0) + gapMinutes * 60_000L, -0.3m));

		// Act: распознаём связки.
		var links = SyntheticCloseRecognizer.Recognize(executions);

		// Assert: число связок соответствует границе окна.
		Assert.That(links, Has.Count.EqualTo(expectedLinkCount));
	}

	#endregion

	#region Детерминизм и реальный кейс

	[TestMethod]
	[Description("Повторное распознавание над тем же составом записей воспроизводит те же связки, включая другой порядок выдачи")]
	// Детерминизм чистой функции: состав связок не зависит ни от повторного
	// вызова, ни от порядка выдачи записей хранилищем.
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-recognition-recomputation-stable
	public void TryIfRecognitionIsStableOnRecomputation()
	{
		// Arrange: стреддл и две связки на разных ногах — закрытие пута и закрытие колла.
		var executions = XautStraddleHalfLegs();
		executions.Add(Linear("put-close-perp", XautPerp, Ms(2026, 10, 15, 14, 0), 0.2m));
		executions.Add(Option("put-close-call", XautCall4400, Ms(2026, 10, 15, 14, 2), -0.2m));
		executions.Add(Linear("call-close-perp", XautPerp, Ms(2026, 10, 15, 15, 0), -0.3m));
		executions.Add(Option("call-close-put", XautPut4400, Ms(2026, 10, 15, 15, 3), -0.3m));

		// Act: распознаём трижды — повторно и на обратном порядке выдачи.
		var first = SyntheticCloseRecognizer.Recognize(executions);
		var second = SyntheticCloseRecognizer.Recognize(executions);
		var reversed = SyntheticCloseRecognizer.Recognize(executions.AsEnumerable().Reverse().ToList());

		// Assert: состав связок совпадает во всех трёх прогонах.
		Assert.That(first, Has.Count.EqualTo(2));
		Assert.That(second, Is.EqualTo(first), "Повторный вызов воспроизводит связки");
		Assert.That(reversed, Is.EqualTo(first), "Обратный порядок выдачи не меняет связки");
		Assert.That(first.Select(link => link.ClosedLegType), Is.EqualTo(new[] { OptionType.Put, OptionType.Call }), "Связки идут в порядке прогона линейных сделок");
	}

	[TestMethod]
	[Description("Повторное распознавание дробных связок воспроизводит те же связки, включая другой порядок выдачи")]
	// Детерминизм чистой функции с дробными связками: линейное исполнение,
	// разбитое между двумя опционными частями, повторный вызов и обратный
	// порядок выдачи дают тот же состав связок.
	// Traceability: issue:#100
	public void TryIfFractionalRecognitionIsStableOnRecomputation()
	{
		// Arrange: стреддл, покупка 0.5 фьючерса и две продажи колла по 0.1 и 0.2.
		var executions = XautStraddleHalfLegs();
		executions.Add(Linear("hedge-perp", XautPerp, Ms(2026, 10, 15, 14, 0), 0.5m));
		executions.Add(Option("sell-call-small", XautCall4400, Ms(2026, 10, 15, 14, 5), -0.1m));
		executions.Add(Option("sell-call-mid", XautCall4400, Ms(2026, 10, 15, 14, 10), -0.2m));

		// Act: распознаём трижды — повторно и на обратном порядке выдачи.
		var first = SyntheticCloseRecognizer.Recognize(executions);
		var second = SyntheticCloseRecognizer.Recognize(executions);
		var reversed = SyntheticCloseRecognizer.Recognize(executions.AsEnumerable().Reverse().ToList());

		// Assert: состав дробных связок совпадает во всех трёх прогонах —
		// связки 0.1 и 0.2 против одной линейной сделки в порядке прогона.
		Assert.That(first, Has.Count.EqualTo(2));
		Assert.That(first.Select(link => link.Quantity), Is.EqualTo(new[] { 0.1m, 0.2m }), "Связки покрывают минимум остатков в порядке прогона кандидатов");
		Assert.That(second, Is.EqualTo(first), "Повторный вызов воспроизводит дробные связки");
		Assert.That(reversed, Is.EqualTo(first), "Обратный порядок выдачи не меняет дробные связки");
	}

	[TestMethod]
	[Description("Кейс 15.10.2026: покупка 0.3 XAUTUSDT и продажа 0.3 колла распознаются как закрытие 0.3 пута из 0.5")]
	// Реальный кейс конструкции «XAUT стреддл 30OCT26 4400»: частичное закрытие
	// 0.3 пута из 0.5 по ценам 4141.91 и 5.9 распознаётся по шаблону ac-43.
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-synthetic-put-close-recognized
	// Traceability: doc:docs/adr/0014-synthetic-close-logical-layer.md#consequences
	public void TryIfXautCaseRecognizesPartialPutCloseFromHalfLeg()
	{
		// Arrange: стреддл 0.5/0.5, затем 15.10.2026 покупка 0.3 XAUTUSDT @ 4141.91
		// и продажа 0.3 XAUT-30OCT26-4400-C @ 5.9 в пределах четырёх минут.
		var executions = XautStraddleHalfLegs();
		executions.Add(Linear("case-perp", XautPerp, Ms(2026, 10, 15, 13, 0), 0.3m));
		executions.Add(Option("case-call", XautCall4400, Ms(2026, 10, 15, 13, 4), -0.3m));

		// Act: распознаём связки.
		var links = SyntheticCloseRecognizer.Recognize(executions);

		// Assert: закрытие 0.3 путовой ноги из открытых 0.5.
		Assert.That(links, Has.Count.EqualTo(1));
		var link = links[0];
		Assert.That(link.ClosedLegSymbol, Is.EqualTo(XautPut4400), "Кейс указывает на путовую ногу стреддла");
		Assert.That(link.ClosedLegType, Is.EqualTo(OptionType.Put), "Закрываемая нога — пут");
		Assert.That(link.Quantity, Is.EqualTo(0.3m), "Закрываемая часть меньше открытых 0.5 ноги");
		Assert.That(link.LinearExecId, Is.EqualTo("case-perp"));
		Assert.That(link.CounterOptionExecId, Is.EqualTo("case-call"));
	}

	[TestMethod]
	[Description("Распознавание без записей конструкции отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnRecognizeWithoutExecutions()
	{
		// Arrange: записи не заданы.

		// Act: распознаём по незаданному набору записей.
		SyntheticCloseRecognizer.Recognize(null!);

		// Assert: ожидается ArgumentNullException — проверяется атрибутом.
	}

	#endregion
}
