namespace TransactionJournal.Hints.Ports;

using TransactionJournal.Domain.Data;

/// <summary>
/// Порт чтения журнал-снапшота: данные журнала, нужные триггерам движка, —
/// конструкции с целями риска/профита, их позиции и хронология сделок.
/// Снапшот производен: строится заново при каждом проходе поверх движков
/// домена (FIFO, сборка конструкций), хранением не живёт и проходом не мутируется.
// Traceability: openspec:hints/engine-pass#requirement-engine-deterministic-selection
/// </summary>
public interface IJournalSnapshotReader
{
	/// <summary>Строит снимок журнала на момент чтения.</summary>
	/// <param name="cancellationToken">Токен отмены.</param>
	/// <returns>Неизменяемый снимок журнала для триггеров движка.</returns>
	Task<JournalSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Снимок журнала для одного прохода агента: открытые и архивные конструкции
/// с их позициями и сделками. Содержимое достаточно триггерам v1: цели
/// риска/профита берутся из сущности Construction, результаты — из того же
/// конвейера метрик, что читают экраны.
// Traceability: openspec:hints/engine-pass#requirement-engine-deterministic-selection
/// </summary>
public sealed record JournalSnapshot
{
	/// <summary>Конструкции журнала, упорядоченные по идентификатору.</summary>
	public required IReadOnlyList<ConstructionView> Constructions { get; init; }
}

/// <summary>Конструкция в снимке журнала: доменные поля и метрики результата.</summary>
public sealed record ConstructionView
{
	/// <summary>Идентификатор конструкции — значение субъекта-конструкции подсказок.</summary>
	public required long Id { get; init; }

	/// <summary>Имя конструкции.</summary>
	public required string Name { get; init; }

	/// <summary>Конструкция открыта; закрытая конструкция субъектом новых подсказок не становится.</summary>
	public required bool IsOpen { get; init; }

	/// <summary>Выделенный капитал в USDT — база процентных величин; null, когда капитал не задан.</summary>
	public required decimal? AllocatedCapitalUsdt { get; init; }

	/// <summary>Значение риска конструкции; null — параметр риска не задан.</summary>
	public required decimal? RiskValue { get; init; }

	/// <summary>Единица ввода риска; null — параметр риска не задан.</summary>
	public required TargetUnit? RiskUnit { get; init; }

	/// <summary>Значение профита конструкции; null — параметр профита не задан.</summary>
	public required decimal? ProfitValue { get; init; }

	/// <summary>Единица ввода профита; null — параметр профита не задан.</summary>
	public required TargetUnit? ProfitUnit { get; init; }

	/// <summary>Реализованный PnL конструкции.</summary>
	public required decimal RealizedPnL { get; init; }

	/// <summary>Нереализованный PnL; null, пока открытый остаток не оценён марками.</summary>
	public required decimal? UnrealizedPnL { get; init; }

	/// <summary>Сумма внешних корректировок PnL.</summary>
	public required decimal AdjustmentsPnL { get; init; }

	/// <summary>Итог конструкции; null, пока нереализованная оценка недоступна.</summary>
	public required decimal? TotalPnL { get; init; }

	/// <summary>Итог в процентах от капитала; null без капитала или без доступного итога.</summary>
	public required decimal? TotalPnLPercent { get; init; }

	/// <summary>Время первой сделки конструкции; null, пока сделок нет.</summary>
	public required DateTimeOffset? OpenedAt { get; init; }

	/// <summary>Время закрытия конструкции; null, пока конструкция открыта.</summary>
	public required DateTimeOffset? ClosedAt { get; init; }

	/// <summary>Позиции конструкции по инструментам.</summary>
	public required IReadOnlyList<PositionView> Positions { get; init; }

	/// <summary>Сделки конструкции в хронологии исполнения.</summary>
	public required IReadOnlyList<TradeView> Trades { get; init; }
}

/// <summary>Позиция «конструкция × инструмент» в снимке журнала.</summary>
public sealed record PositionView
{
	/// <summary>Инструмент позиции.</summary>
	public required string Symbol { get; init; }

	/// <summary>Знаковый остаток: положителен для длинной позиции, отрицателен для короткой.</summary>
	public required decimal Residual { get; init; }

	/// <summary>Позиция открыта, пока остаток не нулевой.</summary>
	public required bool IsOpen { get; init; }

	/// <summary>Реализованный PnL позиции (FIFO).</summary>
	public required decimal RealizedPnL { get; init; }

	/// <summary>Нереализованный PnL позиции; null, пока остаток не оценён марками.</summary>
	public required decimal? UnrealizedPnL { get; init; }

	/// <summary>Средняя цена открытого остатка; null у закрытой позиции.</summary>
	public required decimal? AverageOpenPrice { get; init; }

	/// <summary>Марка инструмента при открытом остатке; null у закрытой позиции и при сбое марок.</summary>
	public required decimal? MarkPrice { get; init; }

	/// <summary>Время первой записи позиции.</summary>
	public required DateTimeOffset OpenedAt { get; init; }

	/// <summary>Время закрытия позиции; null, пока позиция открыта.</summary>
	public required DateTimeOffset? ClosedAt { get; init; }
}

/// <summary>Сделка конструкции в снимке журнала — хронологическая запись исполнения.</summary>
public sealed record TradeView
{
	/// <summary>Идентификатор исполнения на бирже.</summary>
	public required string ExecId { get; init; }

	/// <summary>Инструмент сделки.</summary>
	public required string Symbol { get; init; }

	/// <summary>Знаковое количество: покупка положительна, продажа отрицательна.</summary>
	public required decimal Quantity { get; init; }

	/// <summary>Цена исполнения.</summary>
	public required decimal Price { get; init; }

	/// <summary>Комиссия сделки со знаком уменьшения результата.</summary>
	public required decimal Fee { get; init; }

	/// <summary>Время исполнения.</summary>
	public required DateTimeOffset ExecutedAt { get; init; }
}
