namespace TransactionJournal.Domain.Data;

/// <summary>
/// Настройка приложения уровня рантайма: пара «ключ — значение» для пользовательских
/// переключателей, которые меняются во время работы и обязаны переживать перезапуск.
/// Рантаймовое состояние живёт в базе журнала — второго магазина состояния приложение
/// не заводит, поэтому файл конфигурации для таких переключателей не используется.
/// Traceability: openspec:ops/db-backup#requirement-backup-optional-operations
/// Traceability: adr:docs/adr/0003-stack-single-exe-dotnet-sqlite.md#consequences
/// </summary>
public sealed class AppSetting
{
	/// <summary>Стабильный ключ настройки — первичный ключ строки.</summary>
	public required string Key { get; set; }

	/// <summary>Значение настройки в строковом виде; интерпретация значения — у читающего ключ.</summary>
	public required string Value { get; set; }
}
