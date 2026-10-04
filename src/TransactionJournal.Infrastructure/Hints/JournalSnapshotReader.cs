namespace TransactionJournal.Infrastructure.Hints;

using System.Globalization;
using Microsoft.EntityFrameworkCore;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain.Data;
using TransactionJournal.Hints.Ports;
using TransactionJournal.Infrastructure.Data;

/// <summary>
/// Адаптер порт-снапшота поверх того же конвейера чтения, что питает экраны:
/// метрики конструкций и позиций берутся из read-модели метрик журнала, цели
/// риска/профита — из сущности Construction, хронология сделок — из
/// материализатора сырых исполнений с привязками к конструкциям. Снапшот
/// строится заново при каждом проходе и хранением не живёт.
// Traceability: openspec:hints/engine-pass#requirement-engine-deterministic-selection
/// </summary>
public sealed class JournalSnapshotReader : IJournalSnapshotReader
{
	private readonly DbContextOptions<JournalDbContext> _options;

	private readonly IJournalMetricsReadModel _metricsReadModel;

	/// <summary>Создаёт читателя снапшота над опциями контекста и read-моделью метрик; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <param name="metricsReadModel">Read-модель метрик — тот же конвейер, что читают экраны.</param>
	/// <exception cref="ArgumentNullException">Опции или read-модель не заданы.</exception>
	public JournalSnapshotReader(
		DbContextOptions<JournalDbContext> options,
		IJournalMetricsReadModel metricsReadModel)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_metricsReadModel = metricsReadModel ?? throw new ArgumentNullException(nameof(metricsReadModel));
	}

	/// <inheritdoc cref="IJournalSnapshotReader.ReadAsync" />
	public async Task<JournalSnapshot> ReadAsync(CancellationToken cancellationToken = default)
	{
		var metrics = await _metricsReadModel.ReadAsync(cancellationToken).ConfigureAwait(false);

		List<Construction> entities;
		Dictionary<string, long> constructionIdByExecId;
		IReadOnlyList<MaterializedTrade> trades;
		using (var db = new JournalDbContext(_options))
		{
			// Цели риска/профита и имя — данные сущности Construction: read-модель
			// метрик их не отдаёт, а движку они нужны по спецификации прохода.
			// Traceability: openspec:hints/engine-pass#requirement-engine-deterministic-selection
			entities = await db.Constructions
				.AsNoTracking()
				.OrderBy(construction => construction.Id)
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false);

			var boundUserdata = await db.TradeUserdata
				.AsNoTracking()
				.Where(userdata => userdata.ConstructionId != null)
				.Select(userdata => new { userdata.ExecId, userdata.ConstructionId })
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false);
			constructionIdByExecId = boundUserdata.ToDictionary(
				userdata => userdata.ExecId,
				userdata => userdata.ConstructionId!.Value);

			var rawExecutions = await db.RawExecutions
				.AsNoTracking()
				.OrderBy(execution => execution.Id)
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false);

			// Хронология сделок выводится тем же материализатором сырых исполнений,
			// что питает метрики: расхождений между снапшотом и экранами нет.
			trades = new TradeMaterializer(new InstrumentResolver(
					new InstrumentCatalog(await db.RawInstruments
						.AsNoTracking()
						.OrderBy(instrument => instrument.Id)
						.ToListAsync(cancellationToken)
						.ConfigureAwait(false))))
				.Materialize(rawExecutions)
				.Trades;
		}

		var positionsByConstruction = metrics.Positions
			.GroupBy(position => position.ConstructionId)
			.ToDictionary(group => group.Key, group => group.ToList());
		var tradesByConstruction = trades
			.Where(trade => constructionIdByExecId.ContainsKey(trade.ExecId))
			.GroupBy(trade => constructionIdByExecId[trade.ExecId])
			.ToDictionary(group => group.Key, group => group.ToList());

		var views = entities
			.Select(entity => ToView(
				entity,
				metrics.Constructions.FirstOrDefault(constructionMetrics => constructionMetrics.ConstructionId == entity.Id),
				positionsByConstruction.TryGetValue(entity.Id, out var positions) ? positions : [],
				tradesByConstruction.TryGetValue(entity.Id, out var constructionTrades) ? constructionTrades : []))
			.ToList();

		return new JournalSnapshot
		{
			Constructions = views,
		};
	}

	#region Отображение в представления снапшота

	private static ConstructionView ToView(
		Construction entity,
		ConstructionMetrics? constructionMetrics,
		IReadOnlyList<PositionMetrics> positions,
		IReadOnlyList<MaterializedTrade> trades)
	{
		// Метрики читаются тем же конвейером, что и экраны: конструкция без записей
		// имеет нулевые метрики, а не отсутствует в снапшоте.
		var realizedPnL = constructionMetrics?.RealizedPnL ?? 0m;
		decimal? unrealizedPnL = constructionMetrics?.UnrealizedPnL;
		decimal? totalPnL = constructionMetrics?.TotalPnL;
		decimal? totalPnLPercent = constructionMetrics?.TotalPnLPercent;

		// Конструкция открыта, пока у неё есть ненулевой остаток хотя бы одной
		// позиции: закрытая конструкция субъектом новых подсказок не становится.
		// Traceability: openspec:hints/hint-lifecycle#requirement-hint-subject-v1-closed-set
		var isOpen = positions.Any(position => position.Residual != 0m);

		return new ConstructionView
		{
			Id = entity.Id,
			Name = entity.Name,
			IsOpen = isOpen,
			AllocatedCapitalUsdt = entity.AllocatedCapitalUsdt,
			RiskValue = entity.RiskValue,
			RiskUnit = entity.RiskUnit,
			ProfitValue = entity.ProfitValue,
			ProfitUnit = entity.ProfitUnit,
			RealizedPnL = realizedPnL,
			UnrealizedPnL = unrealizedPnL,
			AdjustmentsPnL = constructionMetrics?.AdjustmentsPnL ?? 0m,
			TotalPnL = totalPnL,
			TotalPnLPercent = totalPnLPercent,
			OpenedAt = constructionMetrics?.OpenedAt,
			ClosedAt = constructionMetrics?.ClosedAt,
			Positions = positions
				.OrderBy(position => position.Symbol, StringComparer.Ordinal)
				.Select(ToPositionView)
				.ToList(),
			Trades = trades
				.OrderBy(trade => trade.ExecutedAt)
				.ThenBy(trade => trade.ExecId, StringComparer.Ordinal)
				.Select(ToTradeView)
				.ToList(),
		};
	}

	private static PositionView ToPositionView(PositionMetrics position) => new()
	{
		Symbol = position.Symbol,
		Residual = position.Residual,
		IsOpen = position.Residual != 0m,
		RealizedPnL = position.RealizedPnL,
		UnrealizedPnL = position.UnrealizedPnL,
		AverageOpenPrice = position.AverageOpenPrice,
		MarkPrice = position.MarkPrice,
		OpenedAt = position.OpenedAt,
		ClosedAt = position.ClosedAt,
	};

	private static TradeView ToTradeView(MaterializedTrade trade) => new()
	{
		ExecId = trade.ExecId,
		Symbol = trade.Symbol,
		Quantity = trade.Quantity,
		Price = trade.Price,
		Fee = trade.Fee,
		ExecutedAt = trade.ExecutedAt,
	};

	#endregion
}
