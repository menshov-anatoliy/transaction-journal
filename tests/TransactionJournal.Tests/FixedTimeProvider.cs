using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
namespace TransactionJournal.Tests;

/// <summary>
/// Фальшивый поставщик времени с фиксированным моментом: изолирует от системных
/// часов проверки, поведение которых зависит от «сейчас», — например, границы
/// доставки доски делистингового опциона.
/// </summary>
public sealed class FixedTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
{
	/// <summary>Момент, который фальшивые часы считают текущим.</summary>
	public DateTimeOffset UtcNow { get; set; } = initialUtcNow;

	/// <inheritdoc />
	public override DateTimeOffset GetUtcNow() => UtcNow;
}
