using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Components;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Web;

/// <summary>
/// Проверки отображения дат журнала: UTC-мгновения, загруженные синхронизацией
/// из Bybit, рендерятся локальной стеночной частью, а отображённая строка
/// разбирается обратно в то же мгновение — формы редактирования не сдвигают время.
/// Traceability: openspec:ui/screens#requirement-local-time-display
/// </summary>
[TestClass]
public class DisplayTimeTests
{
	/// <summary>Время исполнения из Bybit — 2025-02-01 00:00 UTC в мс от эпохи.</summary>
	private static DateTimeOffset BybitMoment => DateTimeOffset.FromUnixTimeMilliseconds(1_738_368_000_000);

	[TestMethod]
	[Description("Дата-время из unix-мс рендерится локальной стеночной частью UTC-мгновения")]
	public void TryIfMomentRendersLocalWallTime()
	{
		// Arrange: мгновение из синхронизации с offset +00:00.
		var moment = BybitMoment;

		// Act
		var rendered = DisplayTime.FormatMoment(moment);

		// Assert: строка совпадает с локальным представлением; при ненулевом
		// смещении зоны она отличается от UTC-стеночной части.
		// Требование: даты из синхронизации показываются в локальном времени.
		// Traceability: openspec:ui/screens#scenario-sync-dates-shown-local
		Assert.That(rendered, Is.EqualTo(
			moment.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
		if (TimeZoneInfo.Local.GetUtcOffset(moment) != TimeSpan.Zero)
		{
			Assert.That(rendered, Is.Not.EqualTo(
				moment.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
		}
	}

	[TestMethod]
	[Description("Календарный день из unix-мс рендерится локальным днём UTC-мгновения")]
	public void TryIfDayRendersLocalCalendarDay()
	{
		// Act
		var rendered = DisplayTime.FormatDay(BybitMoment);

		// Assert
		Assert.That(rendered, Is.EqualTo(
			BybitMoment.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
	}

	[TestMethod]
	[Description("Отсутствующее значение даты-времени рендерится прочерком")]
	public void TryIfNullableMomentRendersDash()
	{
		Assert.That(DisplayTime.FormatMoment(null), Is.EqualTo("—"));
	}

	[TestMethod]
	[Description("Отображённая строка разбирается обратно в исходное мгновение")]
	public void TryIfRenderedMomentParsesToSameInstant()
	{
		// Arrange: то же соглашение разбора, что у форм редактирования — строка
		// без смещения трактуется как локальная.
		var moment = BybitMoment;
		var rendered = DisplayTime.FormatMoment(moment);

		// Act
		var parsed = DateTimeOffset.TryParseExact(
			rendered,
			["yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm"],
			CultureInfo.InvariantCulture,
			DateTimeStyles.None,
			out var value);

		// Assert: отображение и разбор работают в одной локальной зоне —
		// сохранение без правки времени не сдвигает мгновение.
		// Требование: правка записи без изменения времени сохраняет мгновение.
		// Traceability: openspec:ui/screens#scenario-entry-edit-preserves-instant
		Assert.That(parsed, Is.True);
		Assert.That(value, Is.EqualTo(moment));
	}
}
