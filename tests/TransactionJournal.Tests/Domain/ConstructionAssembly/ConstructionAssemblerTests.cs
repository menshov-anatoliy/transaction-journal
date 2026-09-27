using NUnit.Framework;
using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain.ConstructionAssembly;

/// <summary>
/// Проверки детерминированного алгоритма сборки конструкций: упорядочение
/// прогона, кластеризация окон, ролл, усреднение, закрытие deliveries и
/// привязка сделок робота. Каждый тест воспроизводит сценарий дельты
/// capability domain/construction-assembly на синтетическом наборе записей.
/// </summary>
[TestClass]
public class ConstructionAssemblerTests
{
	private const string EthCall1600 = "ETH-25SEP26-1600-C-USDT";
	private const string EthPut1600 = "ETH-25SEP26-1600-P-USDT";
	private const string EthCall1900 = "ETH-25SEP26-1900-C-USDT";
	private const string EthPut1800 = "ETH-25SEP26-1800-P-USDT";
	private const string EthPut1900 = "ETH-25SEP26-1900-P-USDT";
	private const string EthCall2100Dec = "ETH-25DEC26-2100-C-USDT";
	private const string EthPut1800Dec = "ETH-25DEC26-1800-P-USDT";
	private const string EthCall2100Jun = "ETH-5JUN26-2100-C-USDT";
	private const string EthPut2100Jun = "ETH-5JUN26-2100-P-USDT";

	private static readonly ConstructionAssembler Assembler = new();

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

	/// <summary>Фьючерсное исполнение — сделка интрадей-робота.</summary>
	private static AssemblyExecution Linear(string execId, string symbol, long timeMs) => new()
	{
		ExecId = execId,
		Category = "linear",
		Symbol = symbol,
		ExecTimeMs = timeMs,
		SignedQuantity = 1m,
	};

	/// <summary>Закрывающее событие экспирации — delivery-запись либо выведенная OTM-закрывающая.</summary>
	private static AssemblyDelivery Delivery(string symbol, long timeMs) => new()
	{
		Symbol = symbol,
		DeliveryTimeMs = timeMs,
	};

	/// <summary>Сравнивает планы поэлементно: состав, атрибуты, ноги, привязки и счётчик «Входящих».</summary>
	private static void AssertPlansEqual(AssemblyPlan expected, AssemblyPlan actual)
	{
		Assert.That(actual.Constructions.Count, Is.EqualTo(expected.Constructions.Count), "Число конструкций плана совпадает");
		for (var i = 0; i < expected.Constructions.Count; i++)
		{
			var expectedConstruction = expected.Constructions[i];
			var actualConstruction = actual.Constructions[i];
			Assert.That(actualConstruction.Id, Is.EqualTo(expectedConstruction.Id), $"Порядковый номер конструкции {i} воспроизводится");
			Assert.That(actualConstruction.Name, Is.EqualTo(expectedConstruction.Name), $"Имя конструкции {i} воспроизводится");
			Assert.That(actualConstruction.BaseCoin, Is.EqualTo(expectedConstruction.BaseCoin), $"Актив конструкции {i} воспроизводится");
			Assert.That(actualConstruction.OpenedAtMs, Is.EqualTo(expectedConstruction.OpenedAtMs), $"Начало периода конструкции {i} воспроизводится");
			Assert.That(actualConstruction.ClosedAtMs, Is.EqualTo(expectedConstruction.ClosedAtMs), $"Конец периода конструкции {i} воспроизводится");
			Assert.That(actualConstruction.Legs.Count, Is.EqualTo(expectedConstruction.Legs.Count), $"Число ног конструкции {i} воспроизводится");
			for (var j = 0; j < expectedConstruction.Legs.Count; j++)
			{
				var expectedLeg = expectedConstruction.Legs[j];
				var actualLeg = actualConstruction.Legs[j];
				Assert.That(actualLeg.Symbol, Is.EqualTo(expectedLeg.Symbol), $"Символ ноги {j} конструкции {i} воспроизводится");
				Assert.That(actualLeg.Strike, Is.EqualTo(expectedLeg.Strike), $"Страйк ноги {j} конструкции {i} воспроизводится");
				Assert.That(actualLeg.BoardExpiryDate, Is.EqualTo(expectedLeg.BoardExpiryDate), $"Доска ноги {j} конструкции {i} воспроизводится");
				Assert.That(actualLeg.Type, Is.EqualTo(expectedLeg.Type), $"Тип ноги {j} конструкции {i} воспроизводится");
				Assert.That(actualLeg.Quantity, Is.EqualTo(expectedLeg.Quantity), $"Остаток ноги {j} конструкции {i} воспроизводится");
			}
		}

		Assert.That(actual.Bindings.Count, Is.EqualTo(expected.Bindings.Count), "Число привязок плана совпадает");
		foreach (var pair in expected.Bindings)
		{
			Assert.That(actual.Bindings.TryGetValue(pair.Key, out var constructionId), Is.True, $"Привязка {pair.Key} воспроизводится");
			Assert.That(actual.Bindings[pair.Key], Is.EqualTo(pair.Value), $"Цель привязки {pair.Key} воспроизводится");
		}

		Assert.That(actual.InboxCount, Is.EqualTo(expected.InboxCount), "Счётчик «Входящих» воспроизводится");
	}

	#endregion

	#region Модель прогона: упорядочение и детерминированность

	[TestMethod]
	[Description("Прогон упорядочивает записи по (ExecTimeMs, ExecId): план не зависит от порядка входного снимка")]
	// Повторная сборка над тем же сырьём даёт тот же результат: порядок записей
	// и активов зафиксирован правилами, входной порядок снимка не влияет.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-rebuild-reproduces-result
	public void TryIfPlanIsIndependentOfInputOrder()
	{
		// Arrange: стреддл, затем усреднение и ролл; входные списки идут в противоположных порядках.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 9, 0), 1m),
			Option("e2", EthPut1600, Ms(2026, 7, 10, 9, 10), 1m),
			Option("e3", EthCall1600, Ms(2026, 8, 3, 10, 0), 1m),
			Option("e4", EthPut1600, Ms(2026, 8, 3, 10, 5), -1m),
		};
		var deliveries = Array.Empty<AssemblyDelivery>();

		// Act: собираем план с прямым и обратным порядком входа.
		var forwardPlan = Assembler.Assemble(executions, deliveries);
		var reversedPlan = Assembler.Assemble(executions.AsEnumerable().Reverse().ToList(), deliveries);

		// Assert: планы совпадают поэлементно — упорядочение прогона детерминировано.
		AssertPlansEqual(forwardPlan, reversedPlan);
		Assert.That(forwardPlan.Constructions[0].OpenedAtMs, Is.EqualTo(Ms(2026, 7, 10, 9, 0)), "Окно открывается первой по времени записью");
	}

	#endregion

	#region Разбор символов

	[TestMethod]
	[DataRow("ETH-25SEP26-2100-C-USDT", OptionType.Call)]
	[DataRow("ETH-25SEP26-2100-P-USDT", OptionType.Put)]
	[Description("Символ из истории разбирается в ногу плана: актив, доска, страйк и тип опциона")]
	// Разбор опционного символа поставляет сборке актив, доску, страйк и тип ноги.
	// Traceability: change:add-construction-auto-assembly/tasks#1-2
	public void TryIfOptionSymbolParsedIntoPlanLeg(string symbol, OptionType type)
	{
		// Arrange: одиночная покупка опциона из истории.
		var executions = new[] { Option("e1", symbol, Ms(2026, 7, 10), 1m) };

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>());

		// Assert: нога несёт разобранные атрибуты символа, конструкция — базовый актив.
		var construction = plan.Constructions.Single();
		Assert.That(construction.BaseCoin, Is.EqualTo("ETH"));
		var leg = construction.Legs.Single();
		Assert.That(leg.Symbol, Is.EqualTo(symbol));
		Assert.That(leg.Strike, Is.EqualTo(2100m));
		Assert.That(leg.BoardExpiryDate, Is.EqualTo(new DateTime(2026, 9, 25)), "Доска — дата экспирации из символа");
		Assert.That(leg.Type, Is.EqualTo(type));
		Assert.That(construction.Name, Is.EqualTo(type == OptionType.Call ? "ETH направленная CALL 25SEP26 2100" : "ETH направленная PUT 25SEP26 2100"));
	}

	[TestMethod]
	[Description("Опционное исполнение с символом вне формата не группируется и остаётся во «Входящих»")]
	public void TryIfUnparseableOptionSymbolStaysInInbox()
	{
		// Arrange: исполнение с битым символом опциона рядом с корректным.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", "ETH-BADSYMBOL-C-USDT", Ms(2026, 7, 10), 1m),
			Option("e2", EthCall1600, Ms(2026, 7, 10, 10, 5), 1m),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>());

		// Assert: битая запись не привязана и учтена во «Входящих», корректная — привязана.
		Assert.That(plan.Bindings.ContainsKey("e1"), Is.False, "Запись с неразобранным символом не привязывается");
		Assert.That(plan.Bindings.ContainsKey("e2"), Is.True);
		Assert.That(plan.InboxCount, Is.EqualTo(1));
	}

	#endregion

	#region Кластеризация окон и классификация сделок

	[TestMethod]
	[Description("Исполнения с зазором ровно 60 минут остаются одним окном")]
	// Кластеризация окон: допустимый зазор между соседними исполнениями — не более 60 минут.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#requirement-deterministic-option-assembly
	public void TryIfWindowKeepsTradesWithinSixtyMinutes()
	{
		// Arrange: две покупки с зазором ровно 60 минут.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 10, 0), 1m),
			Option("e2", EthPut1600, Ms(2026, 7, 10, 11, 0), 1m),
		};

		// Act: кластеризуем окна.
		var windows = ConstructionAssembler.BuildWindows(executions, Array.Empty<AssemblyDelivery>());

		// Assert: записи попали в одно окно.
		Assert.That(windows, Has.Count.EqualTo(1));
		Assert.That(windows[0].Executions.Select(execution => execution.ExecId), Is.EqualTo(new[] { "e1", "e2" }));
	}

	[TestMethod]
	[Description("Зазор больше 60 минут разбивает исполнения на два окна")]
	public void TryIfWindowSplitsOnGapOverSixtyMinutes()
	{
		// Arrange: две покупки с зазором 61 минута.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 10, 0), 1m),
			Option("e2", EthPut1600, Ms(2026, 7, 10, 11, 1), 1m),
		};

		// Act: кластеризуем окна.
		var windows = ConstructionAssembler.BuildWindows(executions, Array.Empty<AssemblyDelivery>());

		// Assert: образовалось два одиночных окна.
		Assert.That(windows, Has.Count.EqualTo(2));
		Assert.That(windows[0].Executions.Select(execution => execution.ExecId), Is.EqualTo(new[] { "e1" }));
		Assert.That(windows[1].Executions.Select(execution => execution.ExecId), Is.EqualTo(new[] { "e2" }));
	}

	[TestMethod]
	[Description("Delivery-запись между исполнениями обрывает окно")]
	// Delivery-запись обрывает накопленное окно и гасится у владельца ноги.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#requirement-deterministic-option-assembly
	public void TryIfDeliveryBreaksWindow()
	{
		// Arrange: покупка, delivery-событие и покупка уже после него.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 10, 0), 1m),
			Option("e2", EthPut1600, Ms(2026, 7, 10, 11, 0), 1m),
		};
		var deliveries = new[] { Delivery(EthCall1600, Ms(2026, 7, 10, 10, 30)) };

		// Act: кластеризуем окна вместе с delivery-событием.
		var windows = ConstructionAssembler.BuildWindows(executions, deliveries);

		// Assert: delivery разорвала окно на два.
		Assert.That(windows, Has.Count.EqualTo(2));
		Assert.That(windows[0].Executions.Select(execution => execution.ExecId), Is.EqualTo(new[] { "e1" }));
		Assert.That(windows[1].Executions.Select(execution => execution.ExecId), Is.EqualTo(new[] { "e2" }));
	}

	[TestMethod]
	[DataRow(-5, 5, true)]
	[DataRow(-3, 5, true)]
	[DataRow(5, -5, true)]
	[DataRow(5, 0, false)]
	[DataRow(5, 5, false)]
	[DataRow(-5, -5, false)]
	[Description("Закрывающей считается продажа занятой ноги и откуп короткой; сделка вдоль остатка — открывающая")]
	// Классификация сделок окна: закрывающая — против ненулевого остатка аккаунта.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#requirement-deterministic-option-assembly
	public void TryIfClosingTradeDetectedAgainstHeldQuantity(int signedQuantity, int heldQuantity, bool expected)
	{
		// Act: классифицируем сделку против текущего остатка.
		var isClosing = ConstructionAssembler.IsClosingTrade(signedQuantity, heldQuantity);

		// Assert: классификация соответствует правилу против остатка.
		Assert.That(isClosing, Is.EqualTo(expected));
	}

	[TestMethod]
	[Description("Покупки колла и пута одного страйка в пределах окна открывают одну конструкцию с двумя ногами")]
	// Окно стреддла из истории: две покупки в одном окне открывают одну конструкцию.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-straddle-opens-as-one-construction
	public void TryIfStraddleOpensAsOneConstruction()
	{
		// Arrange: покупки колла и пута одного страйка в пределах получаса.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 9, 0), 1m),
			Option("e2", EthPut1600, Ms(2026, 7, 10, 9, 30), 1m),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>());

		// Assert: одна конструкция с обеими ногами и именем стреддла.
		Assert.That(plan.Constructions, Has.Count.EqualTo(1));
		var construction = plan.Constructions[0];
		Assert.That(construction.Name, Is.EqualTo("ETH стреддл 25SEP26 1600"));
		Assert.That(construction.Legs, Has.Count.EqualTo(2));
		Assert.That(construction.Legs[0].Quantity, Is.EqualTo(1m));
		Assert.That(construction.Legs[1].Quantity, Is.EqualTo(1m));
		Assert.That(construction.ClosedAtMs, Is.Null);
		Assert.That(plan.InboxCount, Is.EqualTo(0));
	}

	#endregion

	#region Маршрутизация ролла

	[TestMethod]
	[Description("Ролл 28.07: новые ноги присоединяются к пережившей цепочке, погашения уходят владельцам ног")]
	// Сценарий ролла: закрывающие количества гасятся у владельцев, а открывающие
	// сделки окна присоединяются к конструкции с ненулевым остатком после окна.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-roll-inherits-surviving-owner
	public void TryIfRollInheritsSurvivingOwner()
	{
		// Arrange: стреддл 1600 (20.07), стренгл 1800/1900 (25.07) и ролл 28.07 —
		// в одном окне закрываются ноги обеих конструкций и открываются новые ноги.
		var executions = new List<AssemblyExecution>
		{
			Option("o1", EthCall1600, Ms(2026, 7, 20, 9, 0), 1m),
			Option("o2", EthPut1600, Ms(2026, 7, 20, 9, 10), 1m),
			Option("o3", EthCall1900, Ms(2026, 7, 25, 9, 0), 2m),
			Option("o4", EthPut1800, Ms(2026, 7, 25, 9, 10), 1m),
			Option("s1", EthCall1600, Ms(2026, 7, 28, 10, 0), -1m),
			Option("s2", EthPut1600, Ms(2026, 7, 28, 10, 1), -1m),
			Option("s3", EthCall1900, Ms(2026, 7, 28, 10, 2), -1m),
			Option("s4", EthCall2100Dec, Ms(2026, 7, 28, 10, 3), 1m),
			Option("s5", EthPut1900, Ms(2026, 7, 28, 10, 4), 1m),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>());

		// Assert: конструкций две — стреддл закрылся в окне, цепочка пережила его.
		Assert.That(plan.Constructions, Has.Count.EqualTo(2));
		var straddle = plan.Constructions[0];
		var chain = plan.Constructions[1];
		Assert.That(straddle.Name, Is.EqualTo("ETH стреддл 25SEP26 1600"));
		Assert.That(straddle.ClosedAtMs, Is.EqualTo(Ms(2026, 7, 28, 10, 4)), "Стреддл закрыт по итогам окна, когда все его ноги погашены");
		Assert.That(straddle.Legs.All(leg => leg.Quantity == 0m), Is.True, "Обе ноги стреддла погашены");

		// Assert: новые ноги окна присоединились к пережившей цепочке, погашение 1900C уменьшило её остаток.
		Assert.That(chain.Legs.Select(leg => (leg.Symbol, leg.Quantity)), Is.EqualTo(new[]
		{
			(EthCall1900, 1m),
			(EthPut1800, 1m),
			(EthCall2100Dec, 1m),
			(EthPut1900, 1m),
		}));

		// Assert: закрывающие сделки привязаны к владельцам ног, открывающие — к цели ролла.
		Assert.That(plan.Bindings["o1"], Is.EqualTo(straddle.Id));
		Assert.That(plan.Bindings["o2"], Is.EqualTo(straddle.Id));
		Assert.That(plan.Bindings["o3"], Is.EqualTo(chain.Id));
		Assert.That(plan.Bindings["o4"], Is.EqualTo(chain.Id));
		Assert.That(plan.Bindings["s1"], Is.EqualTo(straddle.Id), "Продажа 1600C погашена у стреддла");
		Assert.That(plan.Bindings["s2"], Is.EqualTo(straddle.Id), "Продажа 1600P погашена у стреддла");
		Assert.That(plan.Bindings["s3"], Is.EqualTo(chain.Id), "Продажа 1900C погашена у цепочки");
		Assert.That(plan.Bindings["s4"], Is.EqualTo(chain.Id), "Покупка 2100C присоединена к пережившей цепочке");
		Assert.That(plan.Bindings["s5"], Is.EqualTo(chain.Id), "Покупка 1900P присоединена к пережившей цепочке");
		Assert.That(plan.InboxCount, Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Погашение ноги до нуля внутри окна не закрывает конструкцию, которую окно рефинансирует")]
	// Промежуточный ноль внутри окна — не финальное состояние: закрытие
	// проверяется после присоединения всех открытий окна.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-roll-inherits-surviving-owner
	public void TryIfWindowRefinancingKeepsConstructionAlive()
	{
		// Arrange: цепочка с коллом 5JUN и ролл-окно: продажа 5JUN гасит её ногу,
		// покупка 25DEC в том же окне рефинансирует конструкцию.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall2100Jun, Ms(2026, 5, 16, 9, 0), 1m),
			Option("e2", EthCall2100Dec, Ms(2026, 5, 24, 14, 0), 1m),
			Option("e3", EthCall2100Jun, Ms(2026, 5, 24, 14, 5), -1m),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>());

		// Assert: конструкция осталась одна и живая, нога 5JUN погашена, 25DEC открыт.
		Assert.That(plan.Constructions, Has.Count.EqualTo(1));
		var chain = plan.Constructions[0];
		Assert.That(chain.ClosedAtMs, Is.Null, "Окно рефинансировало конструкцию — она не закрыта");
		Assert.That(chain.Legs.Select(leg => (leg.Symbol, leg.Quantity)), Is.EqualTo(new[]
		{
			(EthCall2100Jun, 0m),
			(EthCall2100Dec, 1m),
		}));
		Assert.That(plan.Bindings["e3"], Is.EqualTo(chain.Id));
		Assert.That(plan.InboxCount, Is.EqualTo(0));
	}

	#endregion

	#region Усреднение

	[TestMethod]
	[Description("Докупка усредняется в живую конструкцию своего актива, чужая живая конструкция ей не мешает")]
	// Усреднение ищется среди живых конструкций того же базового актива: живая
	// конструкция другого актива не блокирует усреднение и не принимает чужие окна.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-single-leg-adds-to-alive-construction
	public void TryIfAveragingIgnoresAliveConstructionsOfOtherAssets()
	{
		// Arrange: живые конструкции BTC и ETH, докупка ETH-ноги.
		var executions = new List<AssemblyExecution>
		{
			Option("b1", "BTC-25DEC26-85000-C-USDT", Ms(2026, 2, 26, 20, 0), 1m),
			Option("b2", "BTC-25DEC26-120000-C-USDT", Ms(2026, 2, 26, 20, 5), -1m),
			Option("e1", EthCall2100Jun, Ms(2026, 5, 16, 9, 0), 1m),
			Option("e2", EthCall2100Jun, Ms(2026, 5, 18, 9, 0), 1m),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>());

		// Assert: конструкций две, докупка присоединилась к конструкции ETH.
		Assert.That(plan.Constructions, Has.Count.EqualTo(2));
		var eth = plan.Constructions.Single(construction => construction.BaseCoin == "ETH");
		Assert.That(eth.Legs.Single().Quantity, Is.EqualTo(2m), "Докупка усреднилась в живую конструкцию ETH");
		Assert.That(plan.Bindings["e2"], Is.EqualTo(eth.Id), "Докупка привязана к конструкции ETH, а не BTC");
	}

	[TestMethod]
	[Description("Стреддл-окно на новом страйке усреднением не поглощается, хотя пара страйк+доска одна")]
	// Одноногоесть считается по числу символов ноги: окно из колла и пута одного
	// нового страйка — двухногое, и без совпадающих пар у живой конструкции оно
	// открывает новую конструкцию.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-new-strike-window-opens-new-construction
	public void TryIfSameStrikeStraddleWindowOpensNewConstruction()
	{
		// Arrange: живая цепочка с коллом 2100 и стреддл-окно 1600 (колл + пут).
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall2100Jun, Ms(2026, 5, 24, 14, 0), 1m),
			Option("e2", EthCall1600, Ms(2026, 6, 5, 17, 45), 1m),
			Option("e3", EthPut1600, Ms(2026, 6, 5, 17, 50), 1m),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>());

		// Assert: стреддл открылся отдельной конструкцией, цепочка не изменилась.
		Assert.That(plan.Constructions, Has.Count.EqualTo(2));
		var chain = plan.Constructions[0];
		var straddle = plan.Constructions[1];
		Assert.That(chain.Legs.Single().Quantity, Is.EqualTo(1m), "Цепочка не получила ног стреддл-окна");
		Assert.That(straddle.Name, Is.EqualTo("ETH стреддл 25SEP26 1600"));
		Assert.That(straddle.Legs, Has.Count.EqualTo(2));
		Assert.That(plan.Bindings["e2"], Is.EqualTo(straddle.Id));
		Assert.That(plan.Bindings["e3"], Is.EqualTo(straddle.Id));
	}

	[TestMethod]
	[Description("Одиночная докупка 05.02 при единственной живой конструкции усредняется в неё")]
	// Усреднение: одноногое окно при единственной живой конструкции вне дня
	// экспирации присоединяется к ней.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-single-leg-adds-to-alive-construction
	public void TryIfSingleLegAddsToAliveConstruction()
	{
		// Arrange: цепочка открыта 01.02, докупка того же страйка 05.02.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall2100Jun, Ms(2026, 2, 1, 10, 0), 1m),
			Option("e2", EthCall2100Jun, Ms(2026, 2, 5, 10, 0), 1m),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>());

		// Assert: конструкция одна, остаток ноги вырос до двух, докупка привязана к ней.
		Assert.That(plan.Constructions, Has.Count.EqualTo(1));
		var construction = plan.Constructions[0];
		Assert.That(construction.Legs.Single().Quantity, Is.EqualTo(2m));
		Assert.That(plan.Bindings["e2"], Is.EqualTo(construction.Id));
	}

	[TestMethod]
	[Description("Окно 05.06 в день экспирации доски открывает новый стреддл, не трогая живую цепочку")]
	// Запрет усреднения в день экспирации: день окна совпал с днём экспирации
	// самой поздней доски ног — окно открывает новую конструкцию.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-new-strike-window-opens-new-construction
	public void TryIfExpiryDayWindowOpensNewConstruction()
	{
		// Arrange: цепочка 2100C (01.02 + усреднение 05.02) и окно-стреддл в день экспирации 05.06.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall2100Jun, Ms(2026, 2, 1, 10, 0), 1m),
			Option("e2", EthCall2100Jun, Ms(2026, 2, 5, 10, 0), 1m),
			Option("e3", EthCall2100Jun, Ms(2026, 6, 5, 10, 0), 1m),
			Option("e4", EthPut2100Jun, Ms(2026, 6, 5, 10, 5), 1m),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>());

		// Assert: цепочка не изменилась, новый стреддл открыт отдельной конструкцией.
		Assert.That(plan.Constructions, Has.Count.EqualTo(2));
		var chain = plan.Constructions[0];
		var straddle = plan.Constructions[1];
		Assert.That(chain.Name, Is.EqualTo("ETH направленная CALL 5JUN26 2100"));
		Assert.That(chain.Legs.Single().Quantity, Is.EqualTo(2m), "Цепочка не получила ног окна 05.06");
		Assert.That(chain.ClosedAtMs, Is.Null);
		Assert.That(straddle.Name, Is.EqualTo("ETH стреддл 5JUN26 2100"));
		Assert.That(straddle.Legs, Has.Count.EqualTo(2));
		Assert.That(straddle.Legs.All(leg => leg.Quantity == 1m), Is.True);
		Assert.That(plan.Bindings["e3"], Is.EqualTo(straddle.Id));
		Assert.That(plan.Bindings["e4"], Is.EqualTo(straddle.Id));
	}

	[TestMethod]
	[Description("Многоногое окно с ногой на новом страйке открывает новую конструкцию, живая не затрагивается")]
	// Многоногое окно без закрывающих сделок содержит страйк, отсутствующий
	// среди ног живой конструкции, — окно целиком открывает новую конструкцию.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-new-strike-window-opens-new-construction
	public void TryIfNewStrikeWindowOpensNewConstruction()
	{
		// Arrange: живая конструкция с коллом 2100 и окно из колла 2100 и пута 1800.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall2100Jun, Ms(2026, 2, 1, 10, 0), 1m),
			Option("e2", EthCall2100Jun, Ms(2026, 2, 15, 10, 0), 1m),
			Option("e3", EthPut1800Dec, Ms(2026, 2, 15, 10, 5), 1m),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>());

		// Assert: пут 1800 отсутствует среди ног живой конструкции — окно открыло новую.
		Assert.That(plan.Constructions, Has.Count.EqualTo(2));
		var alive = plan.Constructions[0];
		var opened = plan.Constructions[1];
		Assert.That(alive.Legs.Single().Quantity, Is.EqualTo(1m), "Живая конструкция не получила ног окна");
		Assert.That(opened.Legs.Select(leg => (leg.Symbol, leg.Quantity)), Is.EqualTo(new[]
		{
			(EthCall2100Jun, 1m),
			(EthPut1800Dec, 1m),
		}));
		Assert.That(plan.Bindings["e2"], Is.EqualTo(opened.Id));
		Assert.That(plan.Bindings["e3"], Is.EqualTo(opened.Id));
	}

	#endregion

	#region Закрытие deliveries и именование

	[TestMethod]
	[Description("Delivery гасит остаток ноги у владельца, но не закрывает конструкцию с другими живыми ногами")]
	public void TryIfDeliveryExtinguishesOwnerLegOnly()
	{
		// Arrange: стреддл и delivery-запись только по коллу.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 9, 0), 1m),
			Option("e2", EthPut1600, Ms(2026, 7, 10, 9, 10), 1m),
		};
		var deliveries = new[] { Delivery(EthCall1600, Ms(2026, 9, 25, 8, 0)) };

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, deliveries);

		// Assert: колл погашен, пут жив — конструкция остаётся открытой.
		var construction = plan.Constructions.Single();
		Assert.That(construction.ClosedAtMs, Is.Null, "Конструкция с ненулевой ногой не закрывается");
		Assert.That(construction.Legs.First(leg => leg.Symbol == EthCall1600).Quantity, Is.EqualTo(0m));
		Assert.That(construction.Legs.First(leg => leg.Symbol == EthPut1600).Quantity, Is.EqualTo(1m));
	}

	[TestMethod]
	[Description("Экспирация, обнуляющая последнюю ненулевую ногу, закрывает конструкцию без ручного участия")]
	// Закрытие конструкции: каждая нога нулевая с учётом закрывающих записей экспираций.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-delivery-closes-construction
	public void TryIfDeliveryClosesConstruction()
	{
		// Arrange: стреддл и закрывающие события по обеим ногам в момент экспирации.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 9, 0), 1m),
			Option("e2", EthPut1600, Ms(2026, 7, 10, 9, 10), 1m),
		};
		var deliveries = new[]
		{
			Delivery(EthCall1600, Ms(2026, 9, 25, 8, 0)),
			Delivery(EthPut1600, Ms(2026, 9, 25, 8, 5)),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, deliveries);

		// Assert: конструкция закрыта в момент последнего закрывающего события.
		var construction = plan.Constructions.Single();
		Assert.That(construction.ClosedAtMs, Is.EqualTo(Ms(2026, 9, 25, 8, 5)));
		Assert.That(construction.Legs.All(leg => leg.Quantity == 0m), Is.True);
	}

	#endregion

	#region Привязка сделок робота

	[TestMethod]
	[Description("Фьючерсная сделка внутри периода жизни конструкции привязывается к ней")]
	// Сделка робота по активу между открытием и закрытием конструкции привязывается к ней.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-robot-trade-binds-to-covering-construction
	public void TryIfRobotTradeBindsToCoveringConstruction()
	{
		// Arrange: живая конструкция ETH и сделка робота ETHUSDT внутри её жизни.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 9, 0), 1m),
			Linear("r1", "ETHUSDT", Ms(2026, 7, 15)),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>());

		// Assert: сделка привязана к конструкции, «Входящих» нет.
		var construction = plan.Constructions.Single();
		Assert.That(plan.Bindings["r1"], Is.EqualTo(construction.Id));
		Assert.That(plan.InboxCount, Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Перекрытие периодов двух конструкций разрешается в пользу открытой раньше")]
	// Перекрытие периодов: сделка привязывается к конструкции, открытой раньше.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-overlap-resolves-to-earliest
	public void TryIfOverlapResolvesToEarliest()
	{
		// Arrange: две живые конструкции ETH с перекрывающимися периодами.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 9, 0), 1m),
			Option("e2", EthCall2100Dec, Ms(2026, 7, 20, 9, 0), 1m),
			Option("e3", EthPut1800Dec, Ms(2026, 7, 20, 9, 5), 1m),
			Linear("r1", "ETHUSDT", Ms(2026, 7, 25)),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>());

		// Assert: сделка в перекрытии привязана к первой конструкции.
		Assert.That(plan.Constructions, Has.Count.EqualTo(2));
		Assert.That(plan.Bindings["r1"], Is.EqualTo(plan.Constructions[0].Id));
	}

	[TestMethod]
	[Description("Сделки вне периодов жизни конструкций остаются во «Входящих»")]
	// Сделки вне всех периодов — до открытия, после закрытия или по другому активу —
	// остаются непривязанными во «Входящих».
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-robot-trade-outside-periods-stays-in-inbox
	public void TryIfRobotTradeOutsidePeriodsStaysInInbox()
	{
		// Arrange: конструкция ETH (10.07, закрыта delivery 25.09) и сделки робота вокруг её периода.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 9, 0), 1m),
			Option("e2", EthPut1600, Ms(2026, 7, 10, 9, 10), 1m),
			Linear("r0", "ETHUSDT", Ms(2026, 7, 5)),
			Linear("r1", "ETHUSDT", Ms(2026, 7, 15)),
			Linear("r2", "BTCUSDT", Ms(2026, 7, 15)),
			Linear("r3", "ETHUSDT", Ms(2026, 9, 28)),
		};
		var deliveries = new[]
		{
			Delivery(EthCall1600, Ms(2026, 9, 25, 8, 0)),
			Delivery(EthPut1600, Ms(2026, 9, 25, 8, 5)),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, deliveries);

		// Assert: внутри периода сделка привязана, остальные — во «Входящих».
		Assert.That(plan.Bindings["r1"], Is.EqualTo(plan.Constructions[0].Id));
		Assert.That(plan.Bindings.ContainsKey("r0"), Is.False, "Сделка до открытия конструкции не привязывается");
		Assert.That(plan.Bindings.ContainsKey("r2"), Is.False, "Сделка по другому активу не привязывается");
		Assert.That(plan.Bindings.ContainsKey("r3"), Is.False, "Сделка после закрытия конструкции не привязывается");
		Assert.That(plan.InboxCount, Is.EqualTo(3));
	}

	#endregion
}
