using Microsoft.Extensions.Configuration;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Sync;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Sync;

/// <summary>
/// Проверки чтения конфигурации подсистемы синхронизации при сборке опций движков:
/// без секции Sync приложение работает на дефолтах кода (730 дней, пустой список
/// дополнений), заданные значения передаются в опции обоих движков, а нечисловая
/// или неположительная глубина откатывается к дефолту.
/// Traceability: openspec:sync/bybit-history#requirement-backfill-full-history
/// Traceability: openspec:sync/bybit-history#scenario-delisted-base-coin-from-config
/// </summary>
[TestClass]
public class SyncOptionsReaderTests
{
	private const long DayMs = 86_400_000L;

	[TestMethod]
	[Description("Отсутствие секции Sync даёт опциям движков дефолты кода")]
	public void TryIfMissingSyncSectionFallsBackToDefaults()
	{
		// Arrange: конфигурация без секции Sync — обычный случай приложения по умолчанию.
		// Требование: глубина backfill ограничивается границей максимальной глубины,
		// которая без явной настройки равна порядку глубины хранения истории биржи.
		// Traceability: openspec:sync/bybit-history#requirement-backfill-full-history
		var configuration = new ConfigurationBuilder().Build();

		// Act
		var options = SyncOptionsReader.Read(configuration);

		// Assert: оба движка получили дефолтную глубину 730 дней, дополнений нет.
		Assert.That(options.Execution.MaxBackfillDepthMs, Is.EqualTo(730 * DayMs));
		Assert.That(options.Delivery.MaxBackfillDepthMs, Is.EqualTo(730 * DayMs));
		Assert.That(options.ExtraOptionBaseCoins, Is.Empty);
	}

	[TestMethod]
	[Description("Заданные в конфигурации значения передаются в опции обоих движков")]
	public void TryIfConfiguredValuesReachEngineOptions()
	{
		// Arrange: глубина 400 дней и список дополнительных активов с пустой строкой —
		// пустые строки в конфигурации не должны попадать в список.
		// Требование: делистнутый актив из конфигурации загружается при синхронизации.
		// Traceability: openspec:sync/bybit-history#scenario-delisted-base-coin-from-config
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				[SyncOptionsReader.MaxBackfillDepthDaysKey] = "400",
				[SyncOptionsReader.ExtraOptionBaseCoinsKey + ":0"] = "ARB",
				[SyncOptionsReader.ExtraOptionBaseCoinsKey + ":1"] = "alt",
				[SyncOptionsReader.ExtraOptionBaseCoinsKey + ":2"] = "   ",
			})
			.Build();

		// Act
		var options = SyncOptionsReader.Read(configuration);

		// Assert: глубина переведена в миллисекунды для обоих движков, порядок
		// конфигурации сохранён; нормализацию регистра выполняет источник активов.
		Assert.That(options.Execution.MaxBackfillDepthMs, Is.EqualTo(400 * DayMs));
		Assert.That(options.Delivery.MaxBackfillDepthMs, Is.EqualTo(400 * DayMs));
		Assert.That(options.ExtraOptionBaseCoins, Is.EqualTo(new[] { "ARB", "alt" }));
	}

	[TestMethod]
	[Description("Нечисловая или неположительная глубина откатывается к дефолту")]
	public void TryIfInvalidDepthFallsBackToDefault()
	{
		foreach (var raw in new[] { "abc", "0", "-5" })
		{
			// Arrange: каждое некорректное значение глубины проверяется отдельной конфигурацией.
			var configuration = new ConfigurationBuilder()
				.AddInMemoryCollection(new Dictionary<string, string?>
				{
					[SyncOptionsReader.MaxBackfillDepthDaysKey] = raw,
				})
				.Build();

			// Act
			var options = SyncOptionsReader.Read(configuration);

			// Assert: неверное значение не способно отключить глубину — работает дефолт.
			Assert.That(options.Execution.MaxBackfillDepthMs, Is.EqualTo(730 * DayMs), raw);
			Assert.That(options.Delivery.MaxBackfillDepthMs, Is.EqualTo(730 * DayMs), raw);
		}
	}
}
