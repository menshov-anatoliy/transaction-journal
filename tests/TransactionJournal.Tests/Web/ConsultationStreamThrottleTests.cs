namespace TransactionJournal.Tests.Web;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Components;
using TransactionJournal.Tests;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Проверки троттлинга перерисовок стримящегося ответа консультации:
/// перерисовка разрешается не чаще интервала ~150 мс — SignalR-канал
/// не забивается StateHasChanged на каждый чанк модели.
/// Traceability: openspec:ui/screens#scenario-ui-streaming-with-tool-status
/// </summary>
[TestClass]
public sealed class ConsultationStreamThrottleTests
{
	/// <summary>Начало управляемого времени в проверках.</summary>
	private static readonly DateTimeOffset Start = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

	[TestMethod]
	[Description("Перерисовка разрешается сразу, затем не чаще интервала троттлинга")]
	// Чанки модели идут часто, а перерисовка разрешена не чаще интервала:
	// первая перерисовка мгновенная, повторы — только по истечении интервала,
	// отсчёт ведётся от последней разрешённой перерисовки.
	// Traceability: openspec:ui/screens#scenario-ui-streaming-with-tool-status
	public void TryIfChunksArriveFasterThanInterval_RenderPermittedOnlyPerInterval()
	{
		// Arrange: управляемые часы и троттлер с интервалом 150 мс.
		var time = new FixedTimeProvider(Start);
		var throttle = new ConsultationStreamThrottle(TimeSpan.FromMilliseconds(150), time);

		// Act — Assert: первая перерисовка разрешена, немедленный повтор — нет.
		Assert.That(throttle.ShouldRender(), Is.True);
		Assert.That(throttle.ShouldRender(), Is.False);

		// Act — Assert: до истечения интервала перерисовка запрещена.
		time.UtcNow = Start.AddMilliseconds(100);
		Assert.That(throttle.ShouldRender(), Is.False);

		// Act — Assert: по истечении интервала от последней отметки разрешена.
		time.UtcNow = Start.AddMilliseconds(150);
		Assert.That(throttle.ShouldRender(), Is.True);

		// Act — Assert: цикл повторяется от новой отметки перерисовки.
		time.UtcNow = Start.AddMilliseconds(200);
		Assert.That(throttle.ShouldRender(), Is.False);
		time.UtcNow = Start.AddMilliseconds(300);
		Assert.That(throttle.ShouldRender(), Is.True);
	}

	[TestMethod]
	[Description("Неположительный интервал троттлинга отвергается исключением")]
	// Негативный сценарий: интервал без положительной длительности сделал бы
	// троттлинг бессмысленным — каждая перерисовка проходила бы мгновенно.
	public void ThrowOnNonPositiveInterval() =>
		Assert.Throws<ArgumentOutOfRangeException>(() => new ConsultationStreamThrottle(TimeSpan.Zero));
}
