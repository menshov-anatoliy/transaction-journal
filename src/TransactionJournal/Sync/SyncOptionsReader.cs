using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace TransactionJournal.Sync;

/// <summary>
/// Опции движков синхронизации, собранные из конфигурации приложения: глубина backfill
/// для обоих движков категорий и дополнительные базовые активы опционной доски.
/// </summary>
public sealed record SyncEngineOptions
{
	/// <summary>Опции движка истории исполнения.</summary>
	public ExecutionCategorySyncOptions Execution { get; init; } = new();

	/// <summary>Опции движка delivery-истории.</summary>
	public DeliveryCategorySyncOptions Delivery { get; init; } = new();

	/// <summary>Дополнительные базовые активы опционной доски из конфигурации.</summary>
	public IReadOnlyList<string> ExtraOptionBaseCoins { get; init; } = [];
}

/// <summary>
/// Читает конфигурацию подсистемы синхронизации при сборке опций движков: глубину
/// backfill <c>Sync:MaxBackfillDepthDays</c> и дополнительные базовые активы опционной
/// доски <c>Sync:ExtraOptionBaseCoins</c> — делистнутые доски, отсутствующие в текущем
/// справочнике биржи, но с торговой историей. Приложение работает без секции Sync —
/// дефолты зашиты в код: 730 дней (порядок глубины хранения истории биржи) и пустой
/// список; нечисловая или неположительная глубина откатывается к дефолту.
/// Traceability: openspec:sync/bybit-history#requirement-backfill-full-history
/// Traceability: openspec:sync/bybit-history#scenario-delisted-base-coin-from-config
/// </summary>
public static class SyncOptionsReader
{
	/// <summary>Ключ глубины backfill в днях.</summary>
	public const string MaxBackfillDepthDaysKey = "Sync:MaxBackfillDepthDays";

	/// <summary>Ключ списка дополнительных базовых активов опционной доски.</summary>
	public const string ExtraOptionBaseCoinsKey = "Sync:ExtraOptionBaseCoins";

	/// <summary>Дефолтная глубина backfill, дней.</summary>
	public const int DefaultMaxBackfillDepthDays = 730;

	private const long DayMs = 86_400_000L;

	/// <summary>Собирает опции движков из конфигурации; отсутствующая секция даёт дефолты.</summary>
	/// <param name="configuration">Конфигурация приложения.</param>
	/// <exception cref="ArgumentNullException">Конфигурация не задана.</exception>
	public static SyncEngineOptions Read(IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		var depthMs = ReadMaxBackfillDepthMs(configuration);
		return new SyncEngineOptions
		{
			Execution = new ExecutionCategorySyncOptions { MaxBackfillDepthMs = depthMs },
			Delivery = new DeliveryCategorySyncOptions { MaxBackfillDepthMs = depthMs },
			ExtraOptionBaseCoins = ReadExtraOptionBaseCoins(configuration),
		};
	}

	#region Вспомогательные методы

	/// <summary>Читает глубину backfill в мс; нечисловое или неположительное значение — дефолт.</summary>
	private static long ReadMaxBackfillDepthMs(IConfiguration configuration)
	{
		var raw = configuration[MaxBackfillDepthDaysKey];
		if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) && days > 0)
		{
			return days * DayMs;
		}

		return DefaultMaxBackfillDepthDays * DayMs;
	}

	/// <summary>Читает список дополнительных активов; пустые строки отбрасываются, порядок сохраняется.</summary>
	private static IReadOnlyList<string> ReadExtraOptionBaseCoins(IConfiguration configuration) =>
		configuration.GetSection(ExtraOptionBaseCoinsKey)
			.GetChildren()
			.Select(child => child.Value)
			.Where(value => string.IsNullOrWhiteSpace(value) == false)
			.Select(value => value!)
			.ToList();

	#endregion
}
