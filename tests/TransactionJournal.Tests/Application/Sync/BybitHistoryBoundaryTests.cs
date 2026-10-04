using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Application.Sync;
using TransactionJournal.Application;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Application.Sync;

/// <summary>
/// Проверки арифметики границы хранения истории биржи: вычисление самой ранней
/// запрашиваемой даты по серверному времени и clamp пола перебора — глубина из
/// конфигурации зажимается только когда пробивает границу биржи.
/// </summary>
[TestClass]
public class BybitHistoryBoundaryTests
{
	// Виртуальное «серверное время» фиксировано на 2026-01-01: арифметика детерминирована.
	private static readonly long NowMs = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
	private static readonly long DayMs = 86_400_000L;

	[TestMethod]
	[Description("Самая ранняя запрашиваемая дата отстоит от серверного времени на глубину хранения за вычетом запаса")]
	public void TryIfAllowedEarliestComputedFromServerNowAndMargin()
	{
		// Arrange: серверное время биржи и ожидаемая граница: 730 дней глубины минус
		// запас в одно execution-окно (7 дней).
		// Требование: граница хранения считается по часам биржи с фиксированным запасом.
		// Traceability: openspec:sync/bybit-history#scenario-floor-clamped-to-exchange-boundary
		var expectedEarliestMs = NowMs - 730 * DayMs + 7 * DayMs;

		// Act
		var allowedEarliestMs = BybitHistoryBoundary.GetAllowedEarliestMs(NowMs);

		// Assert
		Assert.That(allowedEarliestMs, Is.EqualTo(expectedEarliestMs));
	}

	[TestMethod]
	[Description("Пол из конфига глубже границы хранения биржи зажимается до границы")]
	public void TryIfConfigFloorDeeperThanBoundaryClampedToBoundary()
	{
		// Arrange: конфигурация запрашивает 730 дней глубины — ровно на границе хранения
		// и глубже допустимой зоны: такой пол зажимается до самой ранней запрашиваемой даты.
		// Требование: глубина из конфигурации не может пробить границу хранения биржи.
		// Traceability: openspec:sync/bybit-history#scenario-floor-clamped-to-exchange-boundary
		var configuredFloorMs = NowMs - 730 * DayMs;
		var allowedEarliestMs = BybitHistoryBoundary.GetAllowedEarliestMs(NowMs);

		// Act
		var clampedFloorMs = BybitHistoryBoundary.ClampFloorMs(configuredFloorMs, NowMs);

		// Assert: пол стал позднее конфигурационного и совпал с границей.
		Assert.That(clampedFloorMs, Is.EqualTo(allowedEarliestMs));
		Assert.That(clampedFloorMs, Is.GreaterThan(configuredFloorMs));
	}

	[TestMethod]
	[Description("Пол из конфига мельче границы хранения биржи не трогается")]
	public void TryIfConfigFloorShallowerThanBoundaryUntouched()
	{
		// Arrange: конфигурация запрашивает 30 дней глубины — пол много позднее границы,
		// clamp не имеет права сузить запрошенную глубину.
		// Требование: конфигурируемая глубина сохраняется, пока не пробивает границу биржи.
		// Traceability: openspec:sync/bybit-history#scenario-floor-clamped-to-exchange-boundary
		var configuredFloorMs = NowMs - 30 * DayMs;

		// Act
		var clampedFloorMs = BybitHistoryBoundary.ClampFloorMs(configuredFloorMs, NowMs);

		// Assert
		Assert.That(clampedFloorMs, Is.EqualTo(configuredFloorMs));
	}

	[TestMethod]
	[Description("Пол из конфига, совпадающий с границей хранения, остаётся без изменений")]
	public void TryIfConfigFloorEqualsBoundaryIdempotent()
	{
		// Arrange: конфигурационный пол уже стоит на самой ранней запрашиваемой дате —
		// clamp идемпотентен и не сдвигает его ни назад, ни вперёд.
		// Требование: clamp возвращает позднейший из полов без иных поправок.
		// Traceability: openspec:sync/bybit-history#scenario-floor-clamped-to-exchange-boundary
		var configuredFloorMs = BybitHistoryBoundary.GetAllowedEarliestMs(NowMs);

		// Act
		var clampedFloorMs = BybitHistoryBoundary.ClampFloorMs(configuredFloorMs, NowMs);

		// Assert
		Assert.That(clampedFloorMs, Is.EqualTo(configuredFloorMs));
	}
}
