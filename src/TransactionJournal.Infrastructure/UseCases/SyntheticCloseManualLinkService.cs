using Microsoft.EntityFrameworkCore;
using TransactionJournal.Application;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Domain;
using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Materialization;
using TransactionJournal.Infrastructure.Data;

namespace TransactionJournal.Infrastructure.UseCases;

/// <summary>
/// Use-case сервис ручных синтетических связок: пользователь соединяет линейную
/// сделку и встречный опцион одной конструкции без 60-минутного окна
/// распознавания. Создание проверяет принадлежность обеих сделок конструкции,
/// совпадение страйка и экспирации встречного опциона с закрываемой ногой
/// и согласованность количества с количествами обеих сделок; удаление свободно,
/// редактирование выполняется удалением и созданием заново. Связка — ручные
/// данные логического слоя: инкрементная сборка её сохраняет, полный пересбор
/// стирает вместе с содержимым конструкций. Каждый вызов создаёт собственный
/// короткоживущий контекст, поэтому сервис безопасен в длительных сессиях
/// Blazor Server.
/// </summary>
// Связка создаётся и удаляется пользователем и действует в логическом слое
// наравне с распознанными: действующие связки = распознанные − оспоренные + ручные.
// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
// Персистентная сущность рядом с оспариванием, а не вывод при чтении.
// Traceability: change:manual-journal-management/design#d5
public sealed class SyntheticCloseManualLinkService : ISyntheticCloseManualLinkService
{
	private readonly DbContextOptions<JournalDbContext> _options;

	/// <summary>Создаёт сервис над опциями контекста журнала; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <exception cref="ArgumentNullException">Опции не заданы.</exception>
	public SyntheticCloseManualLinkService(DbContextOptions<JournalDbContext> options)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
	}

	#region Создание

	/// <summary>
	/// Создаёт ручную связку из двух сделок конструкции: валидация следует
	/// правилу ручной связки — одна конструкция, совпадение страйка/экспирации
	/// встречного опциона с закрываемой ногой, согласованное количество; окно
	/// времени между сделками не проверяется.
	/// </summary>
	/// <param name="constructionId">Конструкция, внутри которой создаётся связка.</param>
	/// <param name="linearTradeId">Идентификатор линейной сделки: execId биржевой либо ключ ручной.</param>
	/// <param name="optionTradeId">Идентификатор встречной опционной сделки — с теми же правилами.</param>
	/// <param name="quantity">Количество связки: положительное, не больше количеств обеих сделок.</param>
	/// <param name="cancellationToken">Токен отмены операции.</param>
	/// <returns>Сохранённая ручная связка.</returns>
	/// <exception cref="ArgumentException">Идентификатор сделки не задан или состоит из пробелов.</exception>
	/// <exception cref="SyntheticCloseManualLinkValidationException">Нарушены правила создания связки.</exception>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	public async Task<SyntheticCloseManualLink> CreateAsync(
		long constructionId,
		string linearTradeId,
		string optionTradeId,
		decimal quantity,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(linearTradeId);
		ArgumentException.ThrowIfNullOrWhiteSpace(optionTradeId);

		// Валидация количества идёт до обращений к хранилищу: нарушенную форму
		// пользователь исправляет без побочных эффектов.
		// Traceability: openspec:domain/synthetic-close#scenario-manual-link-rejected-on-mismatch
		if (quantity <= 0m)
		{
			throw new SyntheticCloseManualLinkValidationException(
				$"Количество связки должно быть больше нуля, получено {quantity}.");
		}

		using var db = CreateContext();
		await EnsureConstructionExistsAsync(db, constructionId, cancellationToken).ConfigureAwait(false);
		var trades = await ReadConstructionTradesAsync(db, constructionId, cancellationToken).ConfigureAwait(false);

		// Обе сделки связки обязаны принадлежать ровно этой конструкции:
		// связка соединяет сделки одного журнала конструкций.
		// Traceability: openspec:domain/synthetic-close#scenario-manual-link-created
		var linear = SyntheticCloseManualLinkMaterial.FindTrade(trades, linearTradeId.Trim())
			?? throw new SyntheticCloseManualLinkValidationException(
				$"Линейная сделка «{linearTradeId.Trim()}» не найдена в конструкции {constructionId}.");
		var option = SyntheticCloseManualLinkMaterial.FindTrade(trades, optionTradeId.Trim())
			?? throw new SyntheticCloseManualLinkValidationException(
				$"Встречный опцион «{optionTradeId.Trim()}» не найден в конструкции {constructionId}.");

		ValidatePair(linear, option);

		// Совпадение страйка и экспирации: закрываемая нога — опцион противоположного
		// типа на той же доске, существующий в конструкции длинной частью.
		// Traceability: openspec:domain/synthetic-close#scenario-manual-link-rejected-on-mismatch
		var counterParts = OptionSymbolParser.Parse(option.Symbol);
		if (SyntheticCloseManualLinkMaterial.TryResolveClosedLeg(trades, counterParts, out _) == false)
		{
			var closedType = counterParts.Type == OptionType.Call ? "пут" : "колл";
			throw new SyntheticCloseManualLinkValidationException(
				$"В конструкции {constructionId} нет длинной ноги {closedType} страйка {counterParts.Strike} " +
				$"с экспирацией {counterParts.ExpiryDate:ddMMMyy}: страйк и экспирация встречного опциона " +
				"не совпадают с закрываемой ногой.");
		}

		// Количество связки не превышает количество ни одной из сделок пары.
		// Traceability: openspec:domain/synthetic-close#scenario-manual-link-rejected-on-mismatch
		var linearQuantity = Math.Abs(linear.Quantity);
		var optionQuantity = Math.Abs(option.Quantity);
		if (quantity > linearQuantity || quantity > optionQuantity)
		{
			throw new SyntheticCloseManualLinkValidationException(
				$"Количество связки {quantity} не согласовано со сделками: линейная {linearQuantity}, встречный опцион {optionQuantity}.");
		}

		var link = new SyntheticCloseManualLink
		{
			ConstructionId = constructionId,
			LinearTradeId = linearTradeId.Trim(),
			OptionTradeId = optionTradeId.Trim(),
			Quantity = quantity,
		};
		db.SyntheticCloseManualLinks.Add(link);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return link;
	}

	#endregion

	#region Удаление

	/// <summary>
	/// Удаляет ручную связку: связка исчезает из логического слоя ближайшим
	/// чтением карточки конструкции.
	/// </summary>
	/// <param name="constructionId">Конструкция, внутри которой удаляется связка.</param>
	/// <param name="linkId">Идентификатор ручной связки.</param>
	/// <param name="cancellationToken">Токен отмены операции.</param>
	/// <exception cref="SyntheticCloseManualLinkNotFoundException">Связки с таким идентификатором в конструкции нет.</exception>
	public async Task DeleteAsync(long constructionId, long linkId, CancellationToken cancellationToken = default)
	{
		// Удаление свободно: строка ручных данных убирается без синхронных правок
		// производного слоя, карточка пересчитывает логический слой при чтении.
		// Traceability: openspec:domain/synthetic-close#requirement-synthetic-close-manual-link
		using var db = CreateContext();
		var link = await db.SyntheticCloseManualLinks
			.FirstOrDefaultAsync(candidate =>
				candidate.Id == linkId && candidate.ConstructionId == constructionId, cancellationToken)
			.ConfigureAwait(false)
			?? throw new SyntheticCloseManualLinkNotFoundException(constructionId, linkId);
		db.SyntheticCloseManualLinks.Remove(link);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region Помощники

	private JournalDbContext CreateContext() => new(_options);

	/// <summary>Проверяет существование конструкции до создания связки.</summary>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	private static async Task EnsureConstructionExistsAsync(
		JournalDbContext db,
		long constructionId,
		CancellationToken cancellationToken)
	{
		var exists = await db.Constructions
			.AnyAsync(construction => construction.Id == constructionId, cancellationToken)
			.ConfigureAwait(false);
		if (exists == false)
		{
			throw new ConstructionNotFoundException(constructionId);
		}
	}

	/// <summary>
	/// Материализует сделки конструкции из сырья обоих источников — тем же путём,
	/// что и read-модель деталей: биржевые сделки по привязке пользовательских
	/// данных, ручные — по привязке в строке сырья.
	/// </summary>
	private static async Task<List<MaterializedTrade>> ReadConstructionTradesAsync(
		JournalDbContext db,
		long constructionId,
		CancellationToken cancellationToken)
	{
		var boundExecIds = (await db.TradeUserdata
				.AsNoTracking()
				.Where(userdata => userdata.ConstructionId == constructionId)
				.Select(userdata => userdata.ExecId)
				.ToListAsync(cancellationToken)
				.ConfigureAwait(false))
			.ToHashSet(StringComparer.Ordinal);
		var manualTrades = await db.ManualTrades
			.AsNoTracking()
			.Where(trade => trade.ConstructionId == constructionId)
			.OrderBy(trade => trade.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var rawExecutions = await db.RawExecutions
			.AsNoTracking()
			.OrderBy(execution => execution.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		var rawInstruments = await db.RawInstruments
			.AsNoTracking()
			.OrderBy(instrument => instrument.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		var materializer = new TradeMaterializer(new InstrumentResolver(new InstrumentCatalog(rawInstruments)));
		return materializer.Materialize(rawExecutions, manualTrades).Trades
			.Where(trade => trade.Source == TradeSource.Manual
				? trade.ConstructionId == constructionId
				: boundExecIds.Contains(trade.ExecId!))
			.ToList();
	}

	/// <summary>
	/// Проверяет пару сделок связки: линейная сделка разбирается линейным
	/// инструментом, встречный опцион — символом опциона Bybit.
	/// </summary>
	/// <exception cref="SyntheticCloseManualLinkValidationException">Категория одной из сделок не годится для связки.</exception>
	private static void ValidatePair(MaterializedTrade linear, MaterializedTrade option)
	{
		if (TransactionJournal.Domain.ConstructionAssembly.LinearSymbolParser.TryParseBaseCoin(linear.Symbol, out _) == false
			|| string.Equals(linear.Category, "linear", StringComparison.Ordinal) == false)
		{
			throw new SyntheticCloseManualLinkValidationException(
				$"Сделка «{SyntheticCloseManualLinkMaterial.TradeKey(linear)}» не линейная: связка соединяет линейную сделку со встречным опционом.");
		}

		if (string.Equals(option.Category, "option", StringComparison.Ordinal) == false
			|| OptionSymbolParser.TryParse(option.Symbol, out _) == false)
		{
			throw new SyntheticCloseManualLinkValidationException(
				$"Сделка «{SyntheticCloseManualLinkMaterial.TradeKey(option)}» не опцион: связка соединяет линейную сделку со встречным опционом.");
		}
	}

	#endregion
}
