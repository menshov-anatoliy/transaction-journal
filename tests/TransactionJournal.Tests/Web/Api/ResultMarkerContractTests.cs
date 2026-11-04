namespace TransactionJournal.Tests.Web.Api;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Api.Constructions;
using TransactionJournal.Application.Analytics;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

/// <summary>
/// Юнит-проверки сериализатора контракта маркеров изменения результата:
/// стабильные kebab-case строки видов событий и операций расшифровки,
/// единый маппинг перечня операций для карточки, списка и превью.
/// Неизвестный вид не сериализуется, а падает с явной ошибкой.
/// </summary>
[TestClass]
public sealed class ResultMarkerContractTests
{
	[TestMethod]
	[DataRow(ResultMarkerKind.Opening, "opening")]
	[DataRow(ResultMarkerKind.Closing, "closing")]
	[DataRow(ResultMarkerKind.Roll, "roll")]
	[DataRow(ResultMarkerKind.SyntheticClose, "synthetic-close")]
	[DataRow(ResultMarkerKind.SyntheticRelease, "synthetic-release")]
	[DataRow(ResultMarkerKind.FuturesClose, "futures-close")]
	[DataRow(ResultMarkerKind.PnLAdjustment, "pnl-adjustment")]
	[DataRow(ResultMarkerKind.Delivery, "delivery")]
	[Description("Виды событий и операций сериализуются стабильными kebab-case строками контракта")]
	// Один набор строк обслуживает вид маркера и вид операции расшифровки:
	// SPA сопоставляет строку перечня со строкой события одним словарём.
	// Traceability: openspec:analytics/performance#requirement-result-markers-contract
	public void TryIfSerializesValidKinds(ResultMarkerKind kind, string expected)
	{
		// Act: сериализация валидного вида.
		var serialized = ResultMarkerContract.SerializeKind(kind);

		// Assert: строка контракта стабильна.
		Assert.That(serialized, Is.EqualTo(expected));
	}

	[TestMethod]
	[ExpectedException(typeof(ArgumentOutOfRangeException))]
	[Description("Неизвестный вид маркера не сериализуется")]
	// Вне перечня видов контракта нет строк: значение за пределами enum —
	// ошибка вычислителя, сериализатор падает, а не отдаёт мусор в API.
	// Traceability: openspec:analytics/performance#requirement-result-markers-contract
	public void ThrowOnUnknownKindSerialization()
	{
		// Act: сериализация значения вне перечня видов.
		ResultMarkerContract.SerializeKind((ResultMarkerKind)999);
	}

	[TestMethod]
	[Description("Перечень операций расшифровки сериализуется с видом, моментом и вкладом")]
	// Сервер публикует перечни целиком: каждая операция несёт вид, дату и
	// сумму вклада — сумма перечня равна изменению маркера, SPA сверяет
	// расшифровку сложением.
	// Traceability: openspec:analytics/performance#scenario-marker-operations-sum-to-change
	public void TryIfSerializesOperationsWithKindDateAndContribution()
	{
		// Arrange: перечень из двух операций расшифровки.
		var at = new DateTimeOffset(2026, 9, 18, 10, 0, 0, TimeSpan.Zero);
		var operations = new[]
		{
			new ResultMarkerOperation { Kind = ResultMarkerKind.FuturesClose, EventTime = at, AmountUsdt = 0.92m },
			new ResultMarkerOperation { Kind = ResultMarkerKind.Roll, EventTime = at.AddDays(1), AmountUsdt = -99.17m },
		};

		// Act: сериализация перечня.
		var serialized = ResultMarkerContract.SerializeOperations(operations);

		// Assert: строки перечня сохраняют вид, момент и вклад операции.
		Assert.That(serialized, Has.Count.EqualTo(2));
		Assert.That(serialized[0].Kind, Is.EqualTo("futures-close"));
		Assert.That(serialized[0].Date, Is.EqualTo(at));
		Assert.That(serialized[0].ContributionUsdt, Is.EqualTo(0.92m));
		Assert.That(serialized[1].Kind, Is.EqualTo("roll"));
		Assert.That(serialized[1].ContributionUsdt, Is.EqualTo(-99.17m));
	}

	[TestMethod]
	[Description("Сводка последнего маркера сериализуется с перечнем операций расшифровки")]
	// Хвост списка и последний маркер карточки публикуют одинаковый контракт:
	// итог, время события и перечень операций — расшифровка доступна и в
	// компактных видах индикатора.
	// Traceability: openspec:analytics/performance#scenario-list-preview-publish-last-marker
	public void TryIfSerializesLastMarkerSummariesWithOperations()
	{
		// Arrange: сводка хвоста и последний маркер карточки.
		var at = new DateTimeOffset(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
		var operations = new[] { new ResultMarkerOperation { Kind = ResultMarkerKind.SyntheticClose, EventTime = at, AmountUsdt = 15.44m } };
		var summary = new ResultMarkerSummary(-67.54m, at, operations);
		var lastMarker = new ResultMarker
		{
			Number = 3,
			EventTime = at,
			Kind = ResultMarkerKind.SyntheticClose,
			ChangeUsdt = 15.44m,
			TotalUsdt = -67.54m,
			Operations = operations,
		};

		// Act: сериализация обеих форм последнего маркера.
		var fromSummary = ResultMarkerContract.ToSummaryResponse(summary);
		var fromMarker = ResultMarkerContract.ToSummaryResponse(lastMarker);

		// Assert: контракты идентичны по итогу, времени и перечню.
		Assert.That(fromSummary.TotalUsdt, Is.EqualTo(-67.54m));
		Assert.That(fromSummary.EventAt, Is.EqualTo(at));
		Assert.That(fromSummary.Operations, Has.Count.EqualTo(1));
		Assert.That(fromSummary.Operations[0].Kind, Is.EqualTo("synthetic-close"));
		Assert.That(fromMarker.TotalUsdt, Is.EqualTo(fromSummary.TotalUsdt));
		Assert.That(fromMarker.EventAt, Is.EqualTo(fromSummary.EventAt));
		Assert.That(fromMarker.Operations[0].ContributionUsdt, Is.EqualTo(fromSummary.Operations[0].ContributionUsdt));
	}
}
