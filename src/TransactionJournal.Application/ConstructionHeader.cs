using TransactionJournal.Domain;

namespace TransactionJournal.Application;

/// <summary>
/// Заголовок конструкции для транзитной вкладки каркаса: имя и ручной статус
/// без производных величин — вкладке достаточно опознать конструкцию и показать
/// её статус точкой.
/// </summary>
/// <param name="Id">Идентификатор конструкции.</param>
/// <param name="Name">Имя конструкции.</param>
/// <param name="Status">Ручной статус конструкции.</param>
public sealed record ConstructionHeader(long Id, string Name, ConstructionStatus Status);
