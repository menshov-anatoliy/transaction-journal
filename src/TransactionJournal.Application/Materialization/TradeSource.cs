namespace TransactionJournal.Application.Materialization;

/// <summary>
/// Источник сделки журнала: биржевая запись пришла синхронизацией, ручная —
/// внесена пользователем. Признак живёт в сырье (у биржевой записи это строка
/// RawExecutions, у ручной — строка ManualTrades) и показывается проекцией,
/// позволяя различать записи списков бейджем источника.
/// Traceability: openspec:domain/manual-trades#scenario-manual-trade-source-attribute
/// </summary>
public enum TradeSource
{
	/// <summary>Сделка биржи, выведенная из сырой записи исполнения синхронизации.</summary>
	Exchange = 0,

	/// <summary>Сделка, внесённая пользователем вручную; execId у неё отсутствует.</summary>
	Manual = 1,
}
