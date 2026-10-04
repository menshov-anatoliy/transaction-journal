using System.Text.Json;

namespace TransactionJournal.Domain.Sync;

/// <summary>
/// Предупреждения завершённого запуска синхронизации: перечень пропущенных областей
/// перебора, неразрешённых инструментов и непокрытых базовых активов опционной доски.
/// Сохраняется JSON-строкой в записи запуска и читается при открытии страницы
/// синхронизации — предупреждения переживают перезагрузку страницы.
/// Traceability: openspec:sync/bybit-history#requirement-run-warnings-persisted
/// </summary>
public sealed record SyncRunWarnings
{
	/// <summary>Перечень пропущенных областей перебора: исполнение и delivery по категориям.</summary>
	public IReadOnlyList<string> SkippedAreas { get; init; } = [];

	/// <summary>Перечень неразрешённых инструментов: спецификации не получены.</summary>
	public IReadOnlyList<string> UnresolvedInstruments { get; init; } = [];

	/// <summary>Перечень непокрытых базовых активов опционной доски.</summary>
	public IReadOnlyList<string> UncoveredBaseCoins { get; init; } = [];

	/// <summary>Предупреждений нет: все три перечня пусты — заметки на странице не показываются.</summary>
	public bool IsEmpty =>
		SkippedAreas.Count == 0 &&
		UnresolvedInstruments.Count == 0 &&
		UncoveredBaseCoins.Count == 0;

	/// <summary>Сериализует предупреждения в JSON колонки SyncRuns.WarningsJson.</summary>
	public string ToJson() => JsonSerializer.Serialize(this, SerializerOptions);

	/// <summary>
	/// Разбирает JSON колонки SyncRuns.WarningsJson. Записи без сохранённого значения
	/// (старые запуски) читаются пустым перечнем — заметки на странице не показываются.
	/// </summary>
	public static SyncRunWarnings Parse(string? json) =>
		string.IsNullOrWhiteSpace(json)
			? new SyncRunWarnings()
			: JsonSerializer.Deserialize<SyncRunWarnings>(json, SerializerOptions) ?? new SyncRunWarnings();

	/// <summary>Веб-умолчания JSON: camelCase-имена полей при записи и регистронезависимое чтение.</summary>
	private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
}
