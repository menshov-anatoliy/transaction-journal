using NUnit.Framework;
using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Materialization;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain.ConstructionAssembly;

/// <summary>
/// Проверки чистого правила исключения оспоренных связок: спор по паре execId
/// скрывает связку из логического слоя, неоспоренные связки сохраняют порядок
/// распознанного прогона, отсутствие споров возвращает состав целиком.
/// </summary>
[TestClass]
public class SyntheticCloseDisputeFilterTests
{
	private const string XautCall4400 = "XAUT-30OCT26-4400-C-USDT";
	private const string XautPut4400 = "XAUT-30OCT26-4400-P-USDT";

	[TestMethod]
	[Description("Спор по паре execId исключает связку из выдачи, остальные связки остаются")]
	// Оспоренная связка скрывается из логического слоя: логическая нога
	// возвращается к полному размеру, строка «зафиксировано синтетикой» исчезает.
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-dispute-hides-logical-link
	public void TryIfDisputedLinkExcludedFromLinks()
	{
		// Arrange: две распознанные связки и спор по первой из них.
		var links = new List<SyntheticCloseLink>
		{
			MakeLink("put-close-perp", "put-close-call", XautPut4400),
			MakeLink("call-close-perp", "call-close-put", XautCall4400),
		};
		var disputes = new List<SyntheticCloseDispute>
		{
			new() { ConstructionId = 1, LinearExecId = "put-close-perp", CounterOptionExecId = "put-close-call", DisputedAt = new DateTimeOffset(2026, 10, 15, 13, 30, 0, TimeSpan.Zero) },
		};

		// Act: исключаем оспоренные связки.
		var filtered = SyntheticCloseDisputeFilter.ExcludeDisputed(links, disputes);

		// Assert: осталась только неоспоренная связка.
		Assert.That(filtered.Select(link => link.LinearExecId), Is.EqualTo(new[] { "call-close-perp" }));
	}

	[TestMethod]
	[Description("Связки без споров возвращаются целиком в исходном порядке")]
	// Фильтр без записей оспаривания — пропуск: логический слой совпадает с
	// распознанным прогоном, повторное чтение детерминировано.
	// Traceability: change:synthetic-close-logical-layer/specs/domain/synthetic-close/spec#scenario-dispute-hides-logical-link
	public void TryIfNoDisputesKeepsAllLinksInOrder()
	{
		// Arrange: три связки и пустой список споров.
		var links = new List<SyntheticCloseLink>
		{
			MakeLink("a-perp", "a-call", XautPut4400),
			MakeLink("b-perp", "b-put", XautCall4400),
			MakeLink("c-perp", "c-call", XautPut4400),
		};

		// Act: исключаем оспоренные без записей спора.
		var filtered = SyntheticCloseDisputeFilter.ExcludeDisputed(links, []);

		// Assert: состав и порядок совпадают с распознанным прогоном.
		Assert.That(filtered.Select(link => link.LinearExecId), Is.EqualTo(new[] { "a-perp", "b-perp", "c-perp" }));
	}

	[TestMethod]
	[Description("Частичное совпадение пары execId не скрывает связку")]
	// Спор ключуется полной парой execId: совпадение только линейного или только
	// встречного исполнения связку не скрывает.
	// Traceability: change:synthetic-close-logical-layer/design#d3
	public void TryIfHalfPairMatchKeepsLink()
	{
		// Arrange: связка и спор с совпадающим линейным, но другим встречным исполнением.
		var links = new List<SyntheticCloseLink>
		{
			MakeLink("hedge-perp", "hedge-call", XautPut4400),
		};
		var disputes = new List<SyntheticCloseDispute>
		{
			new() { ConstructionId = 1, LinearExecId = "hedge-perp", CounterOptionExecId = "other-call", DisputedAt = new DateTimeOffset(2026, 10, 15, 13, 30, 0, TimeSpan.Zero) },
		};

		// Act: исключаем оспоренные.
		var filtered = SyntheticCloseDisputeFilter.ExcludeDisputed(links, disputes);

		// Assert: связка осталась — пара не совпала целиком.
		Assert.That(filtered, Has.Count.EqualTo(1));
	}

	[TestMethod]
	[Description("Исключение без связок отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnFilterWithoutLinks()
	{
		// Arrange — Act: список связок не задан.
		SyntheticCloseDisputeFilter.ExcludeDisputed(null!, []);

		// Assert: ожидается ArgumentNullException — проверяется атрибутом.
	}

	[TestMethod]
	[Description("Исключение без записей спора отклоняется")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnFilterWithoutDisputes()
	{
		// Arrange — Act: список споров не задан.
		SyntheticCloseDisputeFilter.ExcludeDisputed([], null!);

		// Assert: ожидается ArgumentNullException — проверяется атрибутом.
	}

	/// <summary>Связка-заготовка с указанной парой execId и закрываемой ногой.</summary>
	private static SyntheticCloseLink MakeLink(string linearExecId, string counterOptionExecId, string closedLegSymbol) => new()
	{
		LinearExecId = linearExecId,
		LinearExecTimeMs = 0,
		CounterOptionExecId = counterOptionExecId,
		CounterOptionExecTimeMs = 60_000,
		BaseCoin = "XAUT",
		ClosedLegSymbol = closedLegSymbol,
		ClosedLegType = OptionType.Put,
		Strike = 4400m,
		BoardExpiryDate = new DateTime(2026, 10, 30),
		Quantity = 0.3m,
	};
}
