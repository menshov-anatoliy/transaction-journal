namespace TransactionJournal.Tests.Hints.Engine;

using NUnit.Framework;
using TransactionJournal.Hints.Engine;
using TransactionJournal.Hints.Engine.Triggers;
using Assert = NUnit.Framework.Assert;

/// <summary>
/// Проверки реестра триггеров: полный набор машинных ключей v1 разрешается в
/// реализации, дубликат ключа в наборе — ошибка сборки реестра.
/// </summary>
[TestClass]
public class HintTriggerRegistryTests
{
	[TestMethod]
	[Description("Реестр по умолчанию разрешает все 11 ключей машинных триггеров v1")]
	// Набор v1 корпуса закрыт: каждый ключ trigger.implementation карточек
	// правил должен разрешаться кодом движка.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public void TryIfRegistryResolvesAllElevenV1Keys()
	{
		var registry = new HintTriggerRegistry();
		string[] keys =
		[
			"risk-limit-period",
			"uncovered-sale-margin",
			"profit-target-reached",
			"edge-sale-cap",
			"roll-time-window",
			"roll-threshold",
			"atm-decay-window",
			"min-straddle-size",
			"flat-win-streak",
			"unfreeze-profit-ratio",
			"synthetic-close-itm",
		];

		foreach (var key in keys)
		{
			Assert.That(registry.TryResolve(key, out var trigger), Is.True, $"Ключ {key} не разрешён реестром.");
			Assert.That(trigger.Key, Is.EqualTo(key));
		}
	}

	[TestMethod]
	[Description("Два триггера с одним ключом ломают создание реестра исключением")]
	// Дубликат ключа — ошибка кода движка, а не карточки: реестр не обязан
	// угадывать, какой триггер имелся в виду.
	// Traceability: openspec:hints/engine-pass#requirement-engine-v1-trigger-set
	public void ThrowOnDuplicateTriggerKey()
	{
		Assert.Throws<ArgumentException>(() => _ = new HintTriggerRegistry(
		[
			new ProfitTargetReachedTrigger(),
			new ProfitTargetReachedTrigger(),
		]));
	}
}
