namespace TransactionJournal.Infrastructure.Hints;

using TransactionJournal.Hints.Ports;

/// <summary>
/// Адаптер системного времени поверх <see cref="DateTimeOffset.UtcNow" />:
/// единственный источник as-of прохода подсказок; в тестах время подменяется
/// фейком порта.
/// </summary>
public sealed class SystemClock : IClock
{
	/// <summary>Текущее время UTC.</summary>
	public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
