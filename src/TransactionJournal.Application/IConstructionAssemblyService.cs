
namespace TransactionJournal.Application;

/// <summary>
/// Контракт use-case «Собрать конструкции» для тонких слоёв UI: полный пересбор
/// конструкций и привязок сделок из локального сырья одним действием, а также
/// инкрементная сборка из «Входящих». Экран зависит от интерфейса, тесты
/// подменяют его заглушкой.
/// </summary>
public interface IConstructionAssemblyService
{
	/// <summary>
	/// Полностью перестраивает конструкции и привязки сделок из сырых записей
	/// хранилища и возвращает счётчики итога пересбора.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены пересбора.</param>
	Task<ConstructionRebuildResult> RebuildAsync(CancellationToken cancellationToken = default);

	/// <summary>
	/// Собирает конструкции только из «Входящих» — записей, не привязанных ни к
	/// одной конструкции, — читая существующие конструкции как контекст остатков,
	/// и возвращает счётчики итога: создано, привязано, осталось.
	/// </summary>
	/// <param name="cancellationToken">Токен отмены сборки.</param>
	Task<ConstructionRebuildResult> AssembleInboxAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Счётчики итога команды сборки — и пересбора, и сборки из «Входящих»:
/// конструкций создано, сделок привязано, сделок осталось во «Входящих» —
/// чисел для показа пользователю после команды.
// Traceability: change:add-construction-auto-assembly/design#d5
/// </summary>
public sealed record ConstructionRebuildResult
{
	/// <summary>Число построенных конструкций.</summary>
	public required int ConstructionsCount { get; init; }

	/// <summary>Число сделок, привязанных к конструкциям при пересборе.</summary>
	public required int BoundCount { get; init; }

	/// <summary>Число сделок, оставшихся непривязанными во «Входящих».</summary>
	public required int TradesInInbox { get; init; }
}
