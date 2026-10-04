namespace TransactionJournal.Application.Analytics;

/// <summary>
/// Свежая марка инструмента со временем её получения: результат запроса провайдера
/// к публичным тикерам. Время получения становится отметкой `marks_as_of` оценки
/// нереализованного PnL и штампом строки кэша.
/// </summary>
public sealed record InstrumentMarkSnapshot(string Symbol, decimal MarkPrice, DateTimeOffset ReceivedAt);
