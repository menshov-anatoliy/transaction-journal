using Microsoft.EntityFrameworkCore;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Application;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Application.ManualTrades;
using TransactionJournal.Domain;

namespace TransactionJournal.Infrastructure.UseCases;

/// <summary>
/// Use-case сервис ручных сделок — второе сырьё журнала наравне с биржевыми
/// записями исполнения. Внесение кладёт сделку во «Входящие» либо сразу в
/// указанную конструкцию, правка свободно меняет атрибуты, удаление убирает
/// запись из любого места; позиции, FIFO и метрики — производные и пересчитываются
/// при чтении, синхронных правок производных сервис не выполняет. Перед сохранением
/// значения проверяются правилами валидации, а символ разрешается справочником
/// инструментов с пополнением публичным каналом биржи; попытка изменить биржевую
/// запись по её идентификатору отклоняется — синхронизированные записи неизменяемы.
/// Каждый вызов создаёт собственный короткоживущий контекст, поэтому сервис
/// безопасен в длительных сессиях Blazor Server.
/// </summary>
// Сделка вносится пользователем с признаком источника «ручной ввод» и попадает
// во «Входящие» либо сразу в указанную конструкцию.
// Traceability: openspec:domain/manual-trades#requirement-manual-trade-entry
// Внесение и правка отклоняются для будущей даты, неположительных количества
// и цены и отрицательной комиссии; биржевые записи править и удалять нельзя.
// Traceability: openspec:domain/manual-trades#requirement-manual-trade-validation
public sealed class ManualTradeService : IManualTradeService
{
	private readonly DbContextOptions<JournalDbContext> _options;
	private readonly ManualInstrumentResolver _resolver;
	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт сервис над опциями контекста журнала, разрешителем символов и часами; база развёрнута миграциями.</summary>
	/// <param name="options">Опции EF-контекста журнала.</param>
	/// <param name="resolver">Разрешитель символа ручной сделки справочником инструментов.</param>
	/// <param name="timeProvider">Поставщик времени для отметок создания/правки и проверки будущей даты; по умолчанию системные часы.</param>
	/// <exception cref="ArgumentNullException">Опции или разрешитель не заданы.</exception>
	public ManualTradeService(
		DbContextOptions<JournalDbContext> options,
		ManualInstrumentResolver resolver,
		TimeProvider? timeProvider = null)
	{
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	#region Внесение

	/// <summary>
	/// Вносит ручную сделку во «Входящие» (constructionId = null) либо сразу
	/// в указанную конструкцию. Значения проверяются правилами валидации,
	/// символ разрешается справочником — только после этого сделка сохраняется.
	/// </summary>
	/// <param name="draft">Данные формы ручного ввода.</param>
	/// <param name="cancellationToken">Токен отмены операции.</param>
	/// <returns>Сохранённая ручная сделка.</returns>
	/// <exception cref="ArgumentException">Инструмент не задан или состоит из пробелов.</exception>
	/// <exception cref="ManualTradeValidationException">Значения формы нарушают правила валидации.</exception>
	/// <exception cref="ConstructionNotFoundException">Указанная конструкция не найдена.</exception>
	/// <exception cref="ManualSymbolResolutionException">Символ не разрешён справочником и биржей.</exception>
	public async Task<ManualTrade> CreateAsync(ManualTradeDraft draft, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(draft);
		ArgumentException.ThrowIfNullOrWhiteSpace(draft.Symbol);

		// Валидация идёт до любых обращений к хранилищу и сети: нарушенную форму
		// пользователь исправляет без побочных эффектов.
		// Traceability: openspec:domain/manual-trades#requirement-manual-trade-validation
		Validate(draft, _timeProvider.GetUtcNow());

		using var db = CreateContext();
		await EnsureConstructionExistsAsync(db, draft.ConstructionId, cancellationToken).ConfigureAwait(false);

		// Символ разрешается справочником инструментов: неизвестный символ пополняет
		// справочник публичным каналом биржи до принятия сделки (решение D3).
		// Traceability: change:manual-journal-management/design#d3
		await _resolver.ResolveAsync(draft.Symbol, cancellationToken).ConfigureAwait(false);

		var now = _timeProvider.GetUtcNow();
		var trade = new ManualTrade
		{
			ExecutedAt = draft.ExecutedAt,
			Symbol = draft.Symbol,
			Side = draft.Side,
			Quantity = draft.Quantity,
			Price = draft.Price,
			FeeUsdt = draft.FeeUsdt,
			ConstructionId = draft.ConstructionId,
			CreatedAt = now,
			UpdatedAt = now,
		};
		db.ManualTrades.Add(trade);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return trade;
	}

	#endregion

	#region Правка

	/// <summary>
	/// Правит атрибуты ручной сделки; новые значения подхватываются позициями,
	/// FIFO и метриками при ближайшем чтении. Привязка сделки правкой не меняется —
	/// перенос между конструкциями и возврат во «Входящие» выполняют существующие
	/// команды привязки; поле привязки черновика игнорируется.
	/// </summary>
	/// <param name="id">Идентификатор ручной сделки.</param>
	/// <param name="draft">Новые значения атрибутов; привязка черновика игнорируется.</param>
	/// <param name="cancellationToken">Токен отмены операции.</param>
	/// <returns>Обновлённая ручная сделка.</returns>
	/// <exception cref="ArgumentException">Инструмент не задан или состоит из пробелов.</exception>
	/// <exception cref="ManualTradeValidationException">Значения формы нарушают правила валидации.</exception>
	/// <exception cref="ManualTradeImmutableException">Идентификатор адресует биржевую запись.</exception>
	/// <exception cref="ManualTradeNotFoundException">Ручной сделки с таким идентификатором нет.</exception>
	/// <exception cref="ManualSymbolResolutionException">Символ не разрешён справочником и биржей.</exception>
	public async Task<ManualTrade> EditAsync(long id, ManualTradeDraft draft, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(draft);
		ArgumentException.ThrowIfNullOrWhiteSpace(draft.Symbol);

		Validate(draft, _timeProvider.GetUtcNow());

		using var db = CreateContext();
		var trade = await FindTradeOrRejectAsync(db, id, cancellationToken).ConfigureAwait(false);

		// Символ разрешается справочником до сохранения: новый неизвестный символ
		// пополняет справочник публичным каналом биржи, а отказ разрешения оставляет
		// сделку с прежними значениями (решение D3).
		// Traceability: change:manual-journal-management/design#d3
		await _resolver.ResolveAsync(draft.Symbol, cancellationToken).ConfigureAwait(false);

		trade.ExecutedAt = draft.ExecutedAt;
		trade.Symbol = draft.Symbol;
		trade.Side = draft.Side;
		trade.Quantity = draft.Quantity;
		trade.Price = draft.Price;
		trade.FeeUsdt = draft.FeeUsdt;
		trade.UpdatedAt = _timeProvider.GetUtcNow();
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
		return trade;
	}

	#endregion

	#region Удаление

	/// <summary>
	/// Удаляет ручную сделку из любого места — из «Входящих» и из конструкции.
	/// Позиции и метрики пересчитываются ближайшим чтением: закрывающие записи
	/// и остатки строятся из актуального сырья без удалённой сделки.
	/// </summary>
	/// <param name="id">Идентификатор ручной сделки.</param>
	/// <param name="cancellationToken">Токен отмены операции.</param>
	/// <exception cref="ManualTradeImmutableException">Идентификатор адресует биржевую запись.</exception>
	/// <exception cref="ManualTradeNotFoundException">Ручной сделки с таким идентификатором нет.</exception>
	public async Task DeleteAsync(long id, CancellationToken cancellationToken = default)
	{
		using var db = CreateContext();
		var trade = await FindTradeOrRejectAsync(db, id, cancellationToken).ConfigureAwait(false);
		db.ManualTrades.Remove(trade);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion

	#region Помощники

	private JournalDbContext CreateContext() => new(_options);

	/// <summary>
	/// Проверяет значения формы правилами ручного ввода: дата не в будущем,
	/// количество и цена положительны, комиссия неотрицательна, сторона
	/// распознана биржевым значением Buy или Sell.
	/// </summary>
	/// <param name="draft">Проверяемые значения формы.</param>
	/// <param name="now">Текущий момент для проверки будущей даты.</param>
	/// <exception cref="ManualTradeValidationException">Какое-либо из правил нарушено.</exception>
	// Даты позже текущего момента отклоняются, количество и цена должны быть
	// больше нуля, комиссия — не меньше нуля.
	// Traceability: openspec:domain/manual-trades#requirement-manual-trade-validation
	private static void Validate(ManualTradeDraft draft, DateTimeOffset now)
	{
		if (draft.ExecutedAt == default)
		{
			throw new ManualTradeValidationException("Дата и время сделки не заданы.");
		}

		if (draft.ExecutedAt > now)
		{
			throw new ManualTradeValidationException(
				$"Дата сделки {draft.ExecutedAt:dd.MM.yyyy HH:mm} находится в будущем: вносить сделки с датой позже текущего момента нельзя.");
		}

		if (draft.Quantity <= 0m)
		{
			throw new ManualTradeValidationException($"Количество сделки должно быть больше нуля, получено {draft.Quantity}.");
		}

		if (draft.Price <= 0m)
		{
			throw new ManualTradeValidationException($"Цена сделки должна быть больше нуля, получено {draft.Price}.");
		}

		if (draft.FeeUsdt < 0m)
		{
			throw new ManualTradeValidationException($"Комиссия не может быть отрицательной, получено {draft.FeeUsdt}.");
		}

		if (draft.Side != SideBuy && draft.Side != SideSell)
		{
			throw new ManualTradeValidationException($"Сторона сделки «{draft.Side}» не распознана: ожидается Buy или Sell.");
		}
	}

	/// <summary>Биржевое значение стороны покупки.</summary>
	private const string SideBuy = "Buy";

	/// <summary>Биржевое значение стороны продажи.</summary>
	private const string SideSell = "Sell";

	/// <summary>
	/// Загружает ручную сделку по идентификатору с охраной неизменяемости биржевых
	/// записей: идентификатор, не адресующий ручную сделку, но совпавший со строкой
	/// сырья биржи, отклоняется как попытка изменить синхронизированную запись.
	/// </summary>
	/// <exception cref="ManualTradeImmutableException">Идентификатор адресует биржевую запись.</exception>
	/// <exception cref="ManualTradeNotFoundException">Ручной сделки с таким идентификатором нет.</exception>
	// Попытка изменить несуществующую или биржевую запись — ошибка запроса:
	// правка и удаление синхронизированных сделок отклоняются.
	// Traceability: openspec:domain/manual-trades#scenario-synced-trade-edit-rejected
	// Traceability: change:manual-journal-management/design#d4
	private static async Task<ManualTrade> FindTradeOrRejectAsync(
		JournalDbContext db,
		long id,
		CancellationToken cancellationToken)
	{
		var trade = await db.ManualTrades
			.FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken)
			.ConfigureAwait(false);
		if (trade != null)
		{
			return trade;
		}

		// Идентификатор не адресует ручную сделку: если он совпал с ключом строки
		// сырья биржи, клиент обратился к неизменяемой биржевой записи — отказ
		// сообщает об этом явно, иначе записи с таким идентификатором просто нет.
		var isExchangeRecord = await db.RawExecutions
			.AnyAsync(execution => execution.Id == id, cancellationToken)
			.ConfigureAwait(false);
		if (isExchangeRecord)
		{
			throw new ManualTradeImmutableException(id);
		}

		throw new ManualTradeNotFoundException(id);
	}

	/// <summary>Проверяет существование конструкции перед привязкой ручной сделки; null означает «Входящие».</summary>
	/// <exception cref="ConstructionNotFoundException">Конструкция не найдена.</exception>
	private static async Task EnsureConstructionExistsAsync(
		JournalDbContext db,
		long? constructionId,
		CancellationToken cancellationToken)
	{
		if (constructionId is null)
		{
			return;
		}

		var exists = await db.Constructions
			.AnyAsync(construction => construction.Id == constructionId, cancellationToken)
			.ConfigureAwait(false);
		if (exists == false)
		{
			throw new ConstructionNotFoundException(constructionId.Value);
		}
	}

	#endregion
}
