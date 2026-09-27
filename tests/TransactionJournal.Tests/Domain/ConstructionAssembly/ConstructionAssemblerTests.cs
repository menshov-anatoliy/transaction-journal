using NUnit.Framework;
using TransactionJournal.Data;
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
	private static AssemblyExecution Linear(string execId, string symbol, long timeMs, decimal quantity = 1m) => new()
	{
		ExecId = execId,
		Category = "linear",
		Symbol = symbol,
		ExecTimeMs = timeMs,
		SignedQuantity = quantity,
	};

	/// <summary>Закрывающее событие экспирации — delivery-запись либо выведенная OTM-закрывающая.</summary>
	private static AssemblyDelivery Delivery(string symbol, long timeMs) => new()
	{
		Symbol = symbol,
		DeliveryTimeMs = timeMs,
	};

	/// <summary>Существующая конструкция для seed'а инкрементной сборки: ключ БД, имя, статус и ноги с остатками.</summary>
	private static AssemblySeedConstruction Seed(
		long id,
		string name,
		long openedAtMs,
		ConstructionStatus status = ConstructionStatus.Open,
		bool nameIsManual = false,
		long? closedAtMs = null,
		params (string Symbol, decimal Quantity)[] legs) => new()
	{
		Id = id,
		Name = name,
		NameIsManual = nameIsManual,
		Status = status,
		OpenedAtMs = openedAtMs,
		ClosedAtMs = closedAtMs,
		Legs = legs
			.Select(leg => new AssemblySeedLeg { Symbol = leg.Symbol, Quantity = leg.Quantity })
			.ToList(),
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

	[TestMethod]
	[Description("Повторная сборка над тем же сырьём даёт идентичный план: полный пересбор и прогон с seed'ом")]
	// Детерминизм прогона: состав конструкций, атрибуты, привязки и «Входящие»
	// воспроизводятся при повторном прогоне над теми же записями, включая
	// затухающую конструкцию и привязку фьючерсов.
	// Traceability: openspec:domain/construction-assembly#scenario-rebuild-reproduces-result
	public void TryIfRepeatedRebuildReproducesPlan()
	{
		// Arrange: стреддл с усреднением, экспирация одной ноги, затухающая
		// конструкция с фьючерсами и seed-контекст инкрементной сборки.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 9, 0), 1m),
			Option("e2", EthPut1600, Ms(2026, 7, 10, 9, 10), 1m),
			Option("e3", EthCall1600, Ms(2026, 7, 20, 10, 0), 1m),
			Linear("r1", "ETHUSDT", Ms(2026, 7, 25)),
			Linear("r2", "ETHUSDT", Ms(2026, 9, 28)),
		};
		var deliveries = new[]
		{
			Delivery(EthPut1600, Ms(2026, 9, 25, 8, 5)),
		};
		var seed = new[]
		{
			Seed(7, "ETH стреддл 25SEP26 1600", Ms(2026, 7, 10, 9, 0), legs: new[] { (EthCall1600, 2m), (EthPut1600, 1m) }),
		};

		// Act: собираем план дважды над тем же сырьём — полный пересбор и прогон с seed'ом.
		var firstFullRebuild = Assembler.Assemble(executions, deliveries);
		var secondFullRebuild = Assembler.Assemble(executions, deliveries);
		var firstSeeded = Assembler.Assemble(executions, deliveries, seed);
		var secondSeeded = Assembler.Assemble(executions, deliveries, seed);

		// Assert: повторные прогоны воспроизводят планы поэлементно.
		AssertPlansEqual(firstFullRebuild, secondFullRebuild);
		AssertPlansEqual(firstSeeded, secondSeeded);
		Assert.That(firstFullRebuild.Constructions.Single().Status, Is.EqualTo(ConstructionStatus.Open), "Затухающая конструкция воспроизводится открытой");
	}

	#endregion

	#region Разбор символов

	[TestMethod]
	[DataRow("ETH-25SEP26-2100-C-USDT", OptionType.Call)]
	[DataRow("ETH-25SEP26-2100-P-USDT", OptionType.Put)]
	[Description("Символ из истории разбирается в ногу плана: актив, доска, страйк и тип опциона")]
	// Разбор опционного символа поставляет сборке актив, доску, страйк и тип ноги,
	// а имя конструкции выводится из состава живых ног — одиночная нога даёт
	// «направленная CALL/PUT».
	// Traceability: openspec:domain/construction-assembly#requirement-deterministic-option-assembly
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#requirement-derived-construction-naming
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
	// Traceability: openspec:domain/construction-assembly#scenario-new-strike-window-opens-new-construction
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
	// остаются непривязанными во «Входящих»; закрытие при нулевом фьючерсном
	// остатке не меняется.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-robot-trade-outside-periods-stays-in-inbox
	public void TryIfRobotTradeOutsidePeriodsStaysInInbox()
	{
		// Arrange: конструкция ETH (10.07, закрыта delivery 25.09) и сделки робота вокруг её периода.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 9, 0), 1m),
			Option("e2", EthPut1600, Ms(2026, 7, 10, 9, 10), 1m),
			Linear("r0", "ETHUSDT", Ms(2026, 7, 5)),
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

		// Assert: сделки робота вне периода — во «Входящих», конструкция закрыта экспирацией.
		Assert.That(plan.Bindings.ContainsKey("r0"), Is.False, "Сделка до открытия конструкции не привязывается");
		Assert.That(plan.Bindings.ContainsKey("r2"), Is.False, "Сделка по другому активу не привязывается");
		Assert.That(plan.Bindings.ContainsKey("r3"), Is.False, "Сделка после закрытия конструкции не привязывается");
		Assert.That(plan.Constructions.Single().Status, Is.EqualTo(ConstructionStatus.Closed));
		Assert.That(plan.InboxCount, Is.EqualTo(3));
	}

	[TestMethod]
	[Description("Затухающая конструкция (прикрытие обнулено, фьючерс не нулевой) принимает сделки робота")]
	// Период жизни затухающей конструкции продолжается после гибели прикрытия:
	// фьючерсная сделка привязывается к ней, если она открыта раньше прочих.
	// Traceability: change:close-construction-on-all-positions/specs/domain/construction-assembly/spec#scenario-fading-construction-absorbs-robot-trades
	public void TryIfFadingConstructionAbsorbsRobotTrades()
	{
		// Arrange: стреддл, фьючерс +1 внутри периода, экспирация обнуляет ноги,
		// затем сделка робота после гибели прикрытия при живом фьючерсе.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 9, 0), 1m),
			Option("e2", EthPut1600, Ms(2026, 7, 10, 9, 10), 1m),
			Linear("r1", "ETHUSDT", Ms(2026, 7, 15)),
			Linear("r2", "ETHUSDT", Ms(2026, 9, 28)),
		};
		var deliveries = new[]
		{
			Delivery(EthCall1600, Ms(2026, 9, 25, 8, 0)),
			Delivery(EthPut1600, Ms(2026, 9, 25, 8, 5)),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, deliveries);

		// Assert: затухающая конструкция открыта и приняла обе сделки.
		var construction = plan.Constructions.Single();
		Assert.That(construction.Status, Is.EqualTo(ConstructionStatus.Open), "Ненулевой фьючерс держит конструкцию открытой");
		Assert.That(construction.ClosedAtMs, Is.Null, "Период жизни продолжается до обнуления фьючерса");
		Assert.That(plan.Bindings["r1"], Is.EqualTo(construction.Id));
		Assert.That(plan.Bindings["r2"], Is.EqualTo(construction.Id), "Сделка после гибели прикрытия привязывается к затухающей конструкции");
		Assert.That(plan.InboxCount, Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Сделка, обнулившая фьючерсный остаток затухающей конструкции, закрывает её период")]
	// Момент закрытия — событие, обнулившее последнюю позицию: после гибели
	// прикрытия это сделка, сведшая фьючерсный остаток к нулю.
	// Traceability: change:close-construction-on-all-positions/specs/domain/construction-assembly/spec#scenario-last-position-flat-closes-construction
	public void TryIfLastPositionFlatClosesConstruction()
	{
		// Arrange: стреддл, фьючерсы +1 и +1, экспирация обнуляет ноги, затем
		// сделка −2 сводит фьючерсный остаток к нулю и закрывает конструкцию;
		// сделка после закрытия остаётся во «Входящих».
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 9, 0), 1m),
			Option("e2", EthPut1600, Ms(2026, 7, 10, 9, 10), 1m),
			Linear("r1", "ETHUSDT", Ms(2026, 7, 15)),
			Linear("r2", "ETHUSDT", Ms(2026, 9, 28)),
			Linear("r3", "ETHUSDT", Ms(2026, 10, 1, 12, 0), -2m),
			Linear("r4", "ETHUSDT", Ms(2026, 10, 5)),
		};
		var deliveries = new[]
		{
			Delivery(EthCall1600, Ms(2026, 9, 25, 8, 0)),
			Delivery(EthPut1600, Ms(2026, 9, 25, 8, 5)),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, deliveries);

		// Assert: конструкция закрыта обнуляющей сделкой, поздняя сделка не привязана.
		var construction = plan.Constructions.Single();
		Assert.That(construction.Status, Is.EqualTo(ConstructionStatus.Closed));
		Assert.That(construction.ClosedAtMs, Is.EqualTo(Ms(2026, 10, 1, 12, 0)), "Момент закрытия — обнуляющая фьючерс сделка");
		Assert.That(plan.Bindings["r1"], Is.EqualTo(construction.Id));
		Assert.That(plan.Bindings["r2"], Is.EqualTo(construction.Id));
		Assert.That(plan.Bindings["r3"], Is.EqualTo(construction.Id));
		Assert.That(plan.Bindings.ContainsKey("r4"), Is.False, "Сделка после закрытия периода не привязывается");
		Assert.That(plan.InboxCount, Is.EqualTo(1));
	}

	[TestMethod]
	[Description("Фьючерсные исполнения при отсутствии живых конструкций не порождают конструкций")]
	// Инвариант опционной основы: конструкцию создаёт только опционное окно —
	// фьючерсные сделки без покрывающей конструкции остаются во «Входящих».
	// Traceability: change:close-construction-on-all-positions/specs/domain/construction-assembly/spec#scenario-futures-only-window-never-opens-construction
	public void TryIfFuturesOnlyWindowNeverOpensConstruction()
	{
		// Arrange: только фьючерсные сделки двух активов, опционных исполнений нет.
		var executions = new List<AssemblyExecution>
		{
			Linear("r1", "ETHUSDT", Ms(2026, 7, 12)),
			Linear("r2", "ETHUSDT", Ms(2026, 7, 15)),
			Linear("r3", "BTCUSDT", Ms(2026, 7, 20), -1m),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>());

		// Assert: конструкций не создано, все сделки остались во «Входящих».
		Assert.That(plan.Constructions, Is.Empty, "Фьючерсное окно не порождает конструкцию");
		Assert.That(plan.Bindings, Is.Empty, "Фьючерсные сделки без покрывающей конструкции не привязываются");
		Assert.That(plan.InboxCount, Is.EqualTo(3), "Все фьючерсные сделки остаются во «Входящих»");
	}

	#endregion

	#region Seed-состояние и производные атрибуты

	[TestMethod]
	[Description("Seed-конструкция участвует в классификации: закрывающая сделка гасит её остаток, новые конструкции нумеруются после ключей БД")]
	// Остатки ног существующих конструкций — контекст инкрементной сборки:
	// закрывающие сделки классифицируются по ним и погашаются у владельцев.
	// Traceability: change:refine-construction-assembly/design#d1
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#requirement-incremental-inbox-assembly
	public void TryIfSeedLegsParticipateInClosingClassification()
	{
		// Arrange: seed-конструкция с остатком колла 1600 и инкремент: продажа
		// этого колла гасит её остаток, покупка колла 5JUN открывает новую.
		var seed = new[]
		{
			Seed(42, "ETH стреддл 25SEP26 1600", Ms(2026, 7, 10, 9, 0), legs: new[] { (EthCall1600, 1m), (EthPut1600, 0m) }),
		};
		var executions = new List<AssemblyExecution>
		{
			Option("s1", EthCall1600, Ms(2026, 8, 3, 10, 0), -1m),
			Option("e1", EthCall2100Jun, Ms(2026, 8, 4, 10, 0), 1m),
		};

		// Act: собираем план с seed'ом.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>(), seed);

		// Assert: продажа привязана к существующей конструкции по её ключу БД,
		// конструкция закрыта обнулением последней живой ноги.
		Assert.That(plan.Constructions, Has.Count.EqualTo(2));
		var seeded = plan.Constructions.Single(construction => construction.IsSeeded);
		Assert.That(seeded.Id, Is.EqualTo(42), "Существующая конструкция сохраняет ключ БД");
		Assert.That(seeded.Status, Is.EqualTo(ConstructionStatus.Closed), "Обнуление всех ног закрывает существующую конструкцию");
		Assert.That(seeded.Legs.Single(leg => leg.Symbol == EthCall1600).Quantity, Is.EqualTo(0m));
		Assert.That(plan.Bindings["s1"], Is.EqualTo(42L), "Закрывающая сделка привязана к владельцу из seed'а");

		// Assert: новая конструкция нумеруется после максимального ключа seed'а.
		var opened = plan.Constructions.Single(construction => construction.IsSeeded == false);
		Assert.That(opened.Id, Is.EqualTo(43), "Новые конструкции не пересекаются с ключами БД");
		Assert.That(plan.Bindings["e1"], Is.EqualTo(43L));
		Assert.That(plan.InboxCount, Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Одноногое окно при нескольких живых конструкциях усредняется по единственной совпадающей доске")]
	// Эвристика доски: ровно одна живая конструкция имеет ногу той же доски,
	// что и нога окна, вне дня её экспирации — окно усредняется в неё.
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#scenario-single-leg-averages-by-board-among-alive
	public void TryIfSingleLegAveragesByBoardAmongAlive()
	{
		// Arrange: живые конструкции двух досок — стреддл 25SEP26 и стренгл 25DEC26,
		// затем докупка колла 1600 доски 25SEP26.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 9, 0), 1m),
			Option("e2", EthPut1600, Ms(2026, 7, 10, 9, 10), 1m),
			Option("e3", EthCall2100Dec, Ms(2026, 7, 20, 9, 0), 1m),
			Option("e4", EthPut1800Dec, Ms(2026, 7, 20, 9, 10), 1m),
			Option("e5", EthCall1600, Ms(2026, 7, 25, 10, 0), 1m),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>());

		// Assert: докупка присоединилась к конструкции той же доски, вторая живая не тронута.
		Assert.That(plan.Constructions, Has.Count.EqualTo(2));
		var sep = plan.Constructions[0];
		var dec = plan.Constructions[1];
		Assert.That(sep.Legs.Select(leg => (leg.Symbol, leg.Quantity)), Is.EqualTo(new[]
		{
			(EthCall1600, 2m),
			(EthPut1600, 1m),
		}));
		Assert.That(dec.Legs, Has.Count.EqualTo(2), "Живая конструкция другой доски не получила ног окна");
		Assert.That(plan.Bindings["e5"], Is.EqualTo(sep.Id));
		Assert.That(plan.InboxCount, Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Одноногое окно при неоднозначной доске — ноль или несколько кандидатов — открывает новую конструкцию")]
	// Неоднозначность цели усреднения снимается в пользу новой конструкции:
	// живые конструкции остаются нетронутыми.
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#scenario-ambiguous-board-opens-new-construction
	public void TryIfAmbiguousBoardOpensNewConstruction()
	{
		// Arrange: две живые конструкции доски 25SEP26 (стреддлы 1600 и 1900),
		// затем докупка колла 1600 — совпадающую доску имеют обе.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 9, 0), 1m),
			Option("e2", EthPut1600, Ms(2026, 7, 10, 9, 10), 1m),
			Option("e3", EthCall1900, Ms(2026, 7, 15, 9, 0), 1m),
			Option("e4", EthPut1900, Ms(2026, 7, 15, 9, 10), 1m),
			Option("e5", EthCall1600, Ms(2026, 7, 20, 10, 0), 1m),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>());

		// Assert: окно открыло третью конструкцию, живые не получили ног.
		Assert.That(plan.Constructions, Has.Count.EqualTo(3));
		Assert.That(plan.Constructions[0].Legs.Single(leg => leg.Symbol == EthCall1600).Quantity, Is.EqualTo(1m));
		Assert.That(plan.Constructions[1].Legs, Has.Count.EqualTo(2));
		var opened = plan.Constructions[2];
		Assert.That(opened.Legs.Select(leg => (leg.Symbol, leg.Quantity)), Is.EqualTo(new[] { (EthCall1600, 1m) }));
		Assert.That(plan.Bindings["e5"], Is.EqualTo(opened.Id));
		Assert.That(plan.InboxCount, Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Достроенный стреддл получает имя стреддла: имя пересчитывается после присоединения окна")]
	// Имя пересчитывается из текущего состава живых ног после каждого окна:
	// направленная покупка колла с присоединённым путом того же страйка зовётся стреддлом.
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#scenario-assembled-straddle-gets-straddle-name
	public void TryIfAssembledStraddleGetsStraddleName()
	{
		// Arrange: живая конструкция из одной ноги — колл 1600; окно докупает пут 1600.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 9, 0), 1m),
			Option("e2", EthPut1600, Ms(2026, 7, 14, 10, 0), 1m),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>());

		// Assert: имя сменилось с «направленная CALL» на «стреддл».
		var construction = plan.Constructions.Single();
		Assert.That(construction.Name, Is.EqualTo("ETH стреддл 25SEP26 1600"));
		Assert.That(construction.Legs, Has.Count.EqualTo(2));
		Assert.That(plan.Bindings["e2"], Is.EqualTo(construction.Id));
	}

	[TestMethod]
	[Description("Ручное переименование фиксирует имя: состав ног меняется, имя не перезаписывается")]
	// Признак ручного имени отключает автогенерацию: ноги пересчитываются,
	// имя существующей конструкции остаётся ручным.
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#scenario-manual-rename-locks-autogenerated-name
	public void TryIfManualRenameLocksAutogeneratedName()
	{
		// Arrange: seed-конструкция с вручную зафиксированным именем и живым коллом,
		// окно докупает пут того же страйка.
		var seed = new[]
		{
			Seed(5, "Разгон ETH к 2000", Ms(2026, 7, 10, 9, 0), nameIsManual: true, legs: new[] { (EthCall1600, 1m) }),
		};
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthPut1600, Ms(2026, 7, 14, 10, 0), 1m),
		};

		// Act: собираем план с seed'ом.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>(), seed);

		// Assert: ноги конструкции пополнились, имя осталось ручным.
		var construction = plan.Constructions.Single();
		Assert.That(construction.Name, Is.EqualTo("Разгон ETH к 2000"), "Ручное имя не перезаписывается автогенерацией");
		Assert.That(construction.NameIsManual, Is.True);
		Assert.That(construction.Legs, Has.Count.EqualTo(2), "Состав ног при зафиксированном имени всё равно обновляется");
		Assert.That(construction.Status, Is.EqualTo(ConstructionStatus.Open));
	}

	[TestMethod]
	[Description("Исчезновение прикрытия при открытом фьючерсе оставляет конструкцию открытой, привязки сохраняются")]
	// Статус следует за всеми позициями: обнуление всех опционных ног экспирацией
	// при ненулевом фьючерсном остатке не закрывает конструкцию — риск жив.
	// Traceability: change:close-construction-on-all-positions/specs/domain/construction-assembly/spec#scenario-cover-loss-with-open-futures-keeps-open
	public void TryIfCoverLossWithOpenFuturesKeepsOpen()
	{
		// Arrange: стреддл, фьючерсная сделка робота внутри периода и экспирация,
		// обнуляющая обе опционные ноги при ненулевом фьючерсном остатке.
		var executions = new List<AssemblyExecution>
		{
			Option("e1", EthCall1600, Ms(2026, 7, 10, 9, 0), 1m),
			Option("e2", EthPut1600, Ms(2026, 7, 10, 9, 10), 1m),
			Linear("r1", "ETHUSDT", Ms(2026, 7, 15)),
		};
		var deliveries = new[]
		{
			Delivery(EthCall1600, Ms(2026, 9, 25, 8, 0)),
			Delivery(EthPut1600, Ms(2026, 9, 25, 8, 5)),
		};

		// Act: собираем план.
		var plan = Assembler.Assemble(executions, deliveries);

		// Assert: конструкция остаётся открытой, фьючерсная сделка привязана.
		var construction = plan.Constructions.Single();
		Assert.That(construction.Status, Is.EqualTo(ConstructionStatus.Open), "Открытый фьючерс держит конструкцию открытой");
		Assert.That(construction.ClosedAtMs, Is.Null, "Период жизни продолжается до обнуления фьючерса");
		Assert.That(construction.Legs.All(leg => leg.Quantity == 0m), Is.True);
		Assert.That(plan.Bindings["r1"], Is.EqualTo(construction.Id), "Фьючерсные сделки сохраняют привязку");
	}

	[TestMethod]
	[Description("Ролл возвращает закрытую конструкцию в «открыта»: конец периода отступает")]
	// Повторное появление ненулевой ноги возвращает статус «открыта»
	// и снова открывает период жизни конструкции.
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#scenario-roll-reopens-status
	public void TryIfRollReopensStatus()
	{
		// Arrange: закрытая конструкция с восстановленным вручную прикрытием
		// (колл 1600 в остатке) и ролл-окно: частичное погашение колла и
		// покупка колла дальней доски рефинансируют конструкцию.
		var seed = new[]
		{
			Seed(8, "ETH колл-спред 25SEP26 1600/25DEC26 2100", Ms(2026, 6, 1, 9, 0),
				status: ConstructionStatus.Closed, closedAtMs: Ms(2026, 7, 1, 12, 0),
				legs: new[] { (EthCall1600, 2m) }),
		};
		var executions = new List<AssemblyExecution>
		{
			Option("s1", EthCall1600, Ms(2026, 7, 20, 10, 0), -1m),
			Option("s2", EthCall2100Dec, Ms(2026, 7, 20, 10, 5), 1m),
		};

		// Act: собираем план с seed'ом.
		var plan = Assembler.Assemble(executions, Array.Empty<AssemblyDelivery>(), seed);

		// Assert: конструкция снова открыта, период жизни снова открыт.
		var construction = plan.Constructions.Single();
		Assert.That(construction.Status, Is.EqualTo(ConstructionStatus.Open), "Ненулевая нога возвращает статус «открыта»");
		Assert.That(construction.ClosedAtMs, Is.Null, "Конец периода жизни отступает при повторном открытии");
		Assert.That(construction.Legs.Select(leg => (leg.Symbol, leg.Quantity)), Is.EqualTo(new[]
		{
			(EthCall1600, 1m),
			(EthCall2100Dec, 1m),
		}));
		Assert.That(plan.Bindings["s1"], Is.EqualTo(8L));
		Assert.That(plan.Bindings["s2"], Is.EqualTo(8L));
		Assert.That(plan.InboxCount, Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Архив не трогается сборкой: статус и имя сохраняются, остатки погашаются")]
	// Статус «архив» — строго ручной: сборка не меняет его и не переименовывает
	// архивную конструкцию, но продолжает обрабатывать её остатки.
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#scenario-archive-untouched-by-assembly
	public void TryIfArchiveUntouchedByAssembly()
	{
		// Arrange: архивная конструкция с автогенерируемым (не ручным) именем
		// и живым коллом; экспирация обнуляет ногу.
		var seed = new[]
		{
			Seed(3, "ETH направленная CALL 25SEP26 1600", Ms(2026, 6, 1, 9, 0),
				status: ConstructionStatus.Archived, legs: new[] { (EthCall1600, 1m) }),
		};
		var executions = new List<AssemblyExecution>();
		var deliveries = new[] { Delivery(EthCall1600, Ms(2026, 9, 25, 8, 0)) };

		// Act: собираем план с seed'ом.
		var plan = Assembler.Assemble(executions, deliveries, seed);

		// Assert: статус и имя не изменились, остаток ноги погашен.
		var construction = plan.Constructions.Single();
		Assert.That(construction.Status, Is.EqualTo(ConstructionStatus.Archived), "Архивный статус сборкой не меняется");
		Assert.That(construction.Name, Is.EqualTo("ETH направленная CALL 25SEP26 1600"), "Имя архивной конструкции не пересчитывается");
		Assert.That(construction.Legs.Single().Quantity, Is.EqualTo(0m), "Погашение остатков архивной конструкции продолжается");
	}

	#endregion
}
