using System.Globalization;

namespace TransactionJournal.Components;

/// <summary>
/// Отображение дат журнала на экранах. Мгновения, загруженные синхронизацией из Bybit,
/// хранятся в UTC; пользователь видит их в локальном времени — конвертация выполняется
/// только при форматировании, хранение и программные контракты остаются в UTC.
/// Traceability: openspec:ui/screens#requirement-local-time-display
/// </summary>
public static class DisplayTime
{
	/// <summary>Формат даты-времени таблиц и форм: «2026-09-24 14:30».</summary>
	public const string DateTimeFormat = "yyyy-MM-dd HH:mm";

	/// <summary>Формат календарного дня: «2026-09-24».</summary>
	public const string DayFormat = "yyyy-MM-dd";

	/// <summary>Дата-время записи для таблиц и предзаполнения форм: локальная стеночная часть UTC-мгновения.</summary>
	// Рендер и разбор форм редактирования работают в одной локальной зоне: сохранение
	// без правки предзаполненного времени сохраняет мгновение.
	// Traceability: openspec:ui/screens#scenario-entry-edit-preserves-instant
	public static string FormatMoment(DateTimeOffset moment) =>
		moment.ToLocalTime().ToString(DateTimeFormat, CultureInfo.InvariantCulture);

	/// <summary>Дата-время с прочерком для отсутствующего значения.</summary>
	public static string FormatMoment(DateTimeOffset? moment) =>
		moment is { } value ? FormatMoment(value) : "—";

	/// <summary>Календарный день записи: локальный день UTC-мгновения.</summary>
	public static string FormatDay(DateTimeOffset moment) =>
		moment.ToLocalTime().ToString(DayFormat, CultureInfo.InvariantCulture);
}
