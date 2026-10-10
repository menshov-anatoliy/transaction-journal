using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransactionJournal.Domain.Bybit;
using TransactionJournal.Infrastructure.Bybit;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Domain.Data;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Materialization;
using TransactionJournal.Domain.Sync;
using TransactionJournal.Domain.ConstructionAssembly;
// Алиас устраняет двусмысленность с новым LinearSymbolParser из Materialization
// (парсер линейных ног реального риска): здесь используется парсер привязки сделок робота.
using LinearSymbolParser = TransactionJournal.Domain.ConstructionAssembly.LinearSymbolParser;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Application.Ops;
using TransactionJournal.Application.Sync;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Chats.Ports;
using TransactionJournal.Application;
using TransactionJournal.Domain;

namespace TransactionJournal.Infrastructure.UseCases;

/// <summary>
/// Use-case пересбора конструкций: снимок сырых записей хранилища превращается
/// в план сборки детерминированным алгоритмом, затем план применяется к базе
/// одной транзакцией — доменные таблицы вычищаются и наполняются заново.
/// Вместе со старыми записями конструкций пересбор стирает и чаты, привязанные
/// к ним; чаты без привязки переживают. Удаление идёт прямым вычищением таблиц
/// в обход поштучного удаления сервиса конструкций: пересборка — массовая
/// операция, карв-аут из запрета удаления непустой конструкции. Каждый вызов
/// создаёт короткоживущий контекст, поэтому сервис безопасен в длительных
/// сессиях Blazor Server.
// Traceability: openspec:domain/construction-assembly#requirement-full-rebuild-semantics
/// Traceability: change:add-construction-auto-assembly/design#d1
/// </summary>
public sealed class ConstructionAssemblyService : IConstructionAssemblyService
{
	private readonly IJournalRawSnapshotStore _rawSnapshotStore;
	private readonly IJournalBackupService _backupService;
	private readonly DbContextOptions<JournalDbContext> _options;
	private readonly IChatStore _chatStore;
	private readonly TimeProvider _timeProvider;

	/// <summary>Создаёт команду пересбора над сырым хранилищем, сервисом резервных копий, опциями контекста журнала и хранилищем чатов.</summary>
	/// <param name="rawSnapshotStore">Источник полного снимка сырых записей журнала.</param>
	/// <param name="backupService">Сервис резервных копий; пересбор обязан стартовать после успешной копии.</param>
	/// <param name="options">Опции EF-контекста журнала; база развёрнута миграциями.</param>
	/// <param name="chatStore">Хранилище чатов: пересбор стирает чаты привязанных конструкций вместе со старой записью.</param>
	/// <param name="timeProvider">Поставщик времени для границы OTM-закрывающих; по умолчанию системные часы.</param>
	/// <exception cref="ArgumentNullException">Какая-либо обязательная зависимость не задана.</exception>
	public ConstructionAssemblyService(
		IJournalRawSnapshotStore rawSnapshotStore,
		IJournalBackupService backupService,
		DbContextOptions<JournalDbContext> options,
		IChatStore chatStore,
		TimeProvider? timeProvider = null)
	{
		_rawSnapshotStore = rawSnapshotStore ?? throw new ArgumentNullException(nameof(rawSnapshotStore));
		_backupService = backupService ?? throw new ArgumentNullException(nameof(backupService));
		_options = options ?? throw new ArgumentNullException(nameof(options));
		_chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	/// <inheritdoc cref="IConstructionAssemblyService.RebuildAsync" />
	public async Task<ConstructionRebuildResult> RebuildAsync(CancellationToken cancellationToken = default)
	{
		// Обязательная резервная копия предшествует любой работе пересбора: копия
		// создаётся до чтения снимка сырья и транзакции вычищения, неудача копирования
		// проходит наружу исключением и блокирует пересбор — данные журнала нетронуты.
		// Traceability: openspec:ops/db-backup#requirement-backup-mandatory-before-rebuild
		// Traceability: openspec:domain/construction-assembly#scenario-rebuild-blocked-without-backup
		await _backupService.CreateBackupAsync("rebuild", cancellationToken).ConfigureAwait(false);

		// Пересбор работает только над локальным сырьём: снимок читается целиком,
		// план строится в памяти и применяется одной транзакцией — сырьё, справочник
		// инструментов и состояние синхронизации операция не трогает.
		// Traceability: openspec:domain/construction-assembly#scenario-rebuild-preserves-raw-storage
		var snapshot = await _rawSnapshotStore.LoadAsync(cancellationToken).ConfigureAwait(false);
		var plan = BuildPlan(snapshot);

		await ApplyPlanAsync(plan, cancellationToken).ConfigureAwait(false);

		return new ConstructionRebuildResult
		{
			ConstructionsCount = plan.Constructions.Count,
			BoundCount = plan.Bindings.Count,
			TradesInInbox = plan.InboxCount,
		};
	}

	/// <inheritdoc cref="IConstructionAssemblyService.AssembleInboxAsync" />
	public async Task<ConstructionRebuildResult> AssembleInboxAsync(CancellationToken cancellationToken = default)
	{
		// Инкремент работает над тем же локальным сырьём: исполнения фильтруются
		// критерием «Входящих», delivery-таймлайн остаётся полным — экспирации,
		// наступившие между прогонами, обязаны погасить ноги владельцев.
		var snapshot = await _rawSnapshotStore.LoadAsync(cancellationToken).ConfigureAwait(false);
		var executions = BuildExecutions(snapshot.Executions);
		var deliveries = BuildDeliveries(
			snapshot,
			new InstrumentCatalog(snapshot.Instruments),
			executions,
			_timeProvider.GetUtcNow());

		using var db = new JournalDbContext(_options);
		var userdataRows = await db.TradeUserdata.ToListAsync(cancellationToken).ConfigureAwait(false);
		var constructions = await db.Constructions
			.OrderBy(construction => construction.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);

		// «Входящие» — исполнения без действующей привязки: тот же критерий, что у
		// читающей модели «Входящих». Ролл с привязанной закрывающей ногой приходит
		// в прогон одной открывающей записью и открывает новую конструкцию —
		// принятое расхождение инкремента с полным пересбором.
		// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#scenario-split-roll-opens-new-construction
		// Traceability: change:refine-construction-assembly/design#d3
		var boundExecIds = userdataRows
			.Where(row => row.ConstructionId != null)
			.Select(row => row.ExecId)
			.ToHashSet(StringComparer.Ordinal);
		var inboxExecutions = executions
			.Where(execution => boundExecIds.Contains(execution.ExecId) == false)
			.ToList();

		// Существующие конструкции читаются как контекст остатков: их остатки ног
		// участвуют в классификации закрывающих сделок и выборе целей усреднения.
		// Traceability: change:refine-construction-assembly/design#d1
		var seed = await BuildSeedAsync(constructions, userdataRows, executions, cancellationToken).ConfigureAwait(false);

		var plan = new ConstructionAssembler().Assemble(inboxExecutions, deliveries, seed);

		await ApplyInboxPlanAsync(plan, cancellationToken).ConfigureAwait(false);

		return new ConstructionRebuildResult
		{
			ConstructionsCount = plan.Constructions.Count(construction => construction.IsSeeded == false),
			BoundCount = plan.Bindings.Count,
			TradesInInbox = plan.InboxCount,
		};
	}

	#region Seed существующих конструкций

	/// <summary>
	/// Читает существующие конструкции как начальное состояние сборки: ключ базы,
	/// имя с признаком ручной фиксации, статус, ноги с текущими остатками и
	/// фьючерсный остаток.
	/// Символы ног выводятся из опционных сделок, привязанных к конструкции, —
	/// нога известна и с обнулённым остатком; количества остатков вычисляются
	/// читающим слоем позиций — агрегатом привязанных сделок и закрывающих
	/// записей (delivery/OTM из сырья, ручные пометки), — без дублирования
	/// формулы. Конструкции без привязанных сделок в seed не
	/// попадают: их период жизни не выводится из данных, контекста остатков
	/// они не дают. Период жизни — агрегат времён привязанных исполнений;
	/// конструкция с полностью обнуленными позициями — ногами и фьючерсом —
	/// передаётся закрытой в момент последнего события своей истории.
	// Traceability: change:refine-construction-assembly/design#d2
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#requirement-incremental-inbox-assembly
	/// </summary>
	private async Task<IReadOnlyList<AssemblySeedConstruction>> BuildSeedAsync(
		IReadOnlyList<Construction> constructions,
		IReadOnlyList<TradeUserdata> userdataRows,
		IReadOnlyList<AssemblyExecution> executions,
		CancellationToken cancellationToken)
	{
		var positions = await new PositionReadModel(_options, timeProvider: _timeProvider)
			.ListAsync(cancellationToken)
			.ConfigureAwait(false);

		var boundExecIdsByConstruction = userdataRows
			.Where(row => row.ConstructionId != null)
			.GroupBy(row => row.ConstructionId!.Value)
			.ToDictionary(
				group => group.Key,
				group => group.Select(row => row.ExecId).ToHashSet(StringComparer.Ordinal));
		var executionByExecId = executions
			.GroupBy(execution => execution.ExecId, StringComparer.Ordinal)
			.ToDictionary(
				group => group.Key,
				group => group.OrderBy(execution => execution.ExecTimeMs).First(),
				StringComparer.Ordinal);
		var positionsByConstruction = positions.Positions
			.GroupBy(position => position.ConstructionId)
			.ToDictionary(group => group.Key, group => group.ToList());
		var lastClosingByConstruction = positions.ClosingEntries
			.GroupBy(entry => entry.ConstructionId)
			.ToDictionary(group => group.Key, group => group.Max(entry => entry.ClosedAt.ToUnixTimeMilliseconds()));

		var seed = new List<AssemblySeedConstruction>(constructions.Count);
		foreach (var construction in constructions)
		{
			if (boundExecIdsByConstruction.TryGetValue(construction.Id, out var execIds) == false)
			{
				continue;
			}

			var boundExecutions = execIds
				.Where(executionByExecId.ContainsKey)
				.Select(execId => executionByExecId[execId])
				.OrderBy(execution => execution.ExecTimeMs)
				.ToList();
			if (boundExecutions.Count == 0)
			{
				continue;
			}

			// Ноги выводятся из опционных символов привязанных исполнений: нога
			// существует и с обнулённым остатком — именно нулевая нога отличает
			// закрытую конструкцию от конструкции без известных ног (читающий
			// слой нулевые позиции не отдаёт). Количества берутся из читающего
			// слоя позиций, отсутствие строки означает нулевой остаток.
			var residualsBySymbol = positionsByConstruction.TryGetValue(construction.Id, out var constructionPositions)
				? constructionPositions
					.GroupBy(position => position.Symbol, StringComparer.Ordinal)
					.ToDictionary(group => group.Key, group => group.First().Residual, StringComparer.Ordinal)
				: [];
			var legs = boundExecutions
				.Select(execution => execution.Symbol)
				.Where(symbol => OptionSymbolParser.TryParse(symbol, out _))
				.Distinct(StringComparer.Ordinal)
				.OrderBy(symbol => symbol, StringComparer.Ordinal)
				.Select(symbol => new AssemblySeedLeg
				{
					Symbol = symbol,
					Quantity = residualsBySymbol.GetValueOrDefault(symbol),
				})
				.ToList();

			// Фьючерсный остаток — агрегат linear-позиций конструкции из того же
			// вызова read-модели позиций: сделки робота и закрывающие записи
			// (включая ручные пометки между прогонами) уже применены читающим
			// слоем, поэтому seed получает актуальный остаток без дублирования
			// формулы.
			// Traceability: change:close-construction-on-all-positions/design#d3
			var futuresQuantity = constructionPositions?
				.Where(position => LinearSymbolParser.TryParseBaseCoin(position.Symbol, out _)
					&& OptionSymbolParser.TryParse(position.Symbol, out _) == false)
				.Sum(position => position.Residual) ?? 0m;

			// Полное обнуление всех позиций — ног и фьючерса — делает конструкцию
			// мёртвой для инкремента: усреднение ищет только живые конструкции,
			// ролл может реанимировать закрытую по ноге с ненулевым остатком.
			// Момент закрытия — последнее событие позиций: привязанное исполнение
			// либо применённая закрывающая запись read-модели.
			// Статус выводится из всех позиций, а не берётся из базы: ненулевой
			// фьючерсный остаток возвращает конструкцию в «открыта», а читающий
			// слой уже применил закрывающие записи (включая экспирации и ручные
			// пометки между прогонами), поэтому обнулённые позиции обязаны прийти
			// в план закрытыми даже без событий в этом прогоне. Архивный статус
			// сборкой не меняется; конструкция без известных ног сохраняет статус
			// базы — её прикрытие не выводится из данных.
			// Traceability: change:close-construction-on-all-positions/specs/domain/construction-assembly/spec#requirement-status-follows-all-positions
			var lastEventAtMs = Math.Max(
				boundExecutions[^1].ExecTimeMs,
				lastClosingByConstruction.GetValueOrDefault(construction.Id));
			var isFullyClosed = legs.Count > 0
				&& legs.All(leg => leg.Quantity == 0m)
				&& futuresQuantity == 0m;
			var derivedStatus = construction.Status == ConstructionStatus.Archived
				? ConstructionStatus.Archived
				: legs.Count > 0
					? (isFullyClosed ? ConstructionStatus.Closed : ConstructionStatus.Open)
					: construction.Status;
			seed.Add(new AssemblySeedConstruction
			{
				Id = construction.Id,
				Name = construction.Name,
				NameIsManual = construction.NameIsManual,
				Status = derivedStatus,
				OpenedAtMs = boundExecutions[0].ExecTimeMs,
				ClosedAtMs = isFullyClosed ? lastEventAtMs : null,
				FuturesQuantity = futuresQuantity,
				LastPositionEventAtMs = lastEventAtMs,
				Legs = legs,
			});
		}

		return seed;
	}

	#endregion

	#region План сборки из снимка сырья

	/// <summary>
	/// Строит план сборки из снимка сырых записей, не трогая базу: торговые записи
	/// выводятся из сырья напрямую (только биржевой тип Trade, знаковое количество
	/// по стороне), deliveries — из сырых delivery-записей и выведенных OTM-закрывающих.
	/// Справочник инструментов входом сборки не фильтруется: истёкшие опционы биржа
	/// отдаёт отказом 110023 и их спецификаций в справочнике нет, но сделки по ним
	/// обязаны попадать в сборку — атрибуты ноги разбираются из символа. Повторный
	/// прогон над тем же сырьём даёт тот же план. Публичен для контрольных сверок:
	/// диагностический тест сверяет план с контрольными показателями истории.
	// Traceability: change:add-construction-auto-assembly/design#d1
	// Traceability: openspec:domain/construction-assembly#scenario-rebuild-reproduces-result
	/// </summary>
	public AssemblyPlan BuildPlan(JournalRawSnapshot snapshot)
	{
		var executions = BuildExecutions(snapshot.Executions);
		var deliveries = BuildDeliveries(snapshot, new InstrumentCatalog(snapshot.Instruments), executions, _timeProvider.GetUtcNow());

		return new ConstructionAssembler().Assemble(executions, deliveries);
	}

	/// <summary>Биржевой тип исполнения, из которого выводится сделка.</summary>
	private const string TradeExecType = "Trade";

	/// <summary>Сторона исполнения покупки — знак количества положительный.</summary>
	private const string BuySide = "Buy";

	/// <summary>Сторона исполнения продажи — знак количества отрицательный.</summary>
	private const string SellSide = "Sell";

	/// <summary>
	/// Выводит вход сборки из сырых записей: сделкой становится только исполнение
	/// биржевого типа Trade, знаковое количество берётся по стороне. Фандинг и
	/// прочие не-Trade записи сделками не являются и в сборку не попадают —
	/// так же, как и в материализаторе «Входящих». Повреждённый payload и
	/// неизвестная сторона остаются жёсткими ошибками.
	// Traceability: openspec:sync/bybit-history#requirement-non-trade-executions-are-not-trades
	/// </summary>
	private static List<AssemblyExecution> BuildExecutions(IReadOnlyList<RawExecution> rawExecutions)
	{
		var executions = new List<AssemblyExecution>(rawExecutions.Count);
		foreach (var rawExecution in rawExecutions)
		{
			var execution = ParseExecutionPayload(rawExecution);

			if (string.Equals(execution.ExecType, TradeExecType, StringComparison.OrdinalIgnoreCase) == false)
			{
				continue;
			}

			// Идентичность execId в строке хранилища и payload — условие идемпотентности:
			// расхождение означает повреждение сырья, как и в материализаторе.
			if (string.Equals(execution.ExecId, rawExecution.ExecId, StringComparison.Ordinal) == false)
			{
				throw new TradeMaterializationException(
					rawExecution.ExecId,
					$"Идентификатор исполнения полезной нагрузки ({execution.ExecId}) расходится со строкой хранилища ({rawExecution.ExecId}).");
			}

			var sideSign = execution.Side switch
			{
				BuySide => 1m,
				SellSide => -1m,
				_ => throw new TradeMaterializationException(
					rawExecution.ExecId,
					$"Сторона исполнения «{execution.Side}» не распознана: ожидается Buy или Sell."),
			};

			if (execution.ExecQty is null)
			{
				throw new TradeMaterializationException(
					rawExecution.ExecId,
					$"Запись исполнения {rawExecution.ExecId} не содержит исполненное количество (execQty).");
			}

			executions.Add(new AssemblyExecution
			{
				ExecId = rawExecution.ExecId,
				Category = rawExecution.Category,
				Symbol = execution.Symbol,
				ExecTimeMs = execution.ExecTimeMs,
				SignedQuantity = sideSign * execution.ExecQty.Value,
			});
		}

		return executions;
	}

	/// <summary>Разбирает JSON сырой записи в типизированную запись исполнения биржи.</summary>
	/// <exception cref="TradeMaterializationException">JSON некорректен или пуст.</exception>
	private static BybitExecution ParseExecutionPayload(RawExecution rawExecution)
	{
		try
		{
			return JsonSerializer.Deserialize<BybitExecution>(rawExecution.PayloadJson, BybitJson.Options)
				?? throw new TradeMaterializationException(
					rawExecution.ExecId,
					$"Полезная нагрузка записи исполнения {rawExecution.ExecId} оказалась пустой после разбора JSON.");
		}
		catch (JsonException exception)
		{
			throw new TradeMaterializationException(
				rawExecution.ExecId,
				$"Полезная нагрузка записи исполнения {rawExecution.ExecId} содержит некорректный JSON: {exception.Message}",
				exception);
		}
	}

	/// <summary>
	/// Собирает закрывающие события экспирации: реальные delivery-записи биржи плюс
	/// выведенные OTM-закрывающие — опционные символы, по которым были сделки, без
	/// биржевой записи, чьё время доставки уже наступило. Время доставки берётся из
	/// справочника, а для истёкших инструментов, которых в справочнике нет (биржа
	/// отвечает отказом 110023), — из символа: доска кодирует только дату, а биржа
	/// доставляет опционы в 08:00 UTC этой даты. OTM-событие выводится только по
	/// торговым символам: события по неторговым инструментам ничего не гасят, а
	/// лишние разрывы окон нарушили бы кластеризацию.
	// Traceability: adr:docs/adr/0002-option-expiry-closing-entries.md#option-expiry-closing-entries
	/// </summary>
	private static List<AssemblyDelivery> BuildDeliveries(
		JournalRawSnapshot snapshot,
		InstrumentCatalog catalog,
		IReadOnlyList<AssemblyExecution> executions,
		DateTimeOffset asOf)
	{
		var deliveries = snapshot.Deliveries
			.Select(delivery => new AssemblyDelivery
			{
				Symbol = delivery.Symbol,
				DeliveryTimeMs = delivery.DeliveryTimeMs,
			})
			.ToList();
		var deliveredSymbols = snapshot.Deliveries
			.Select(delivery => delivery.Symbol)
			.ToHashSet(StringComparer.Ordinal);

		// Торговые опционные символы берутся из входа сборки: нога может существовать
		// только у символа, по которому были сделки.
		foreach (var symbol in executions
			.Where(execution => string.Equals(execution.Category, InstrumentResolver.OptionsCategory, StringComparison.Ordinal))
			.Select(execution => execution.Symbol)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(symbol => symbol, StringComparer.Ordinal))
		{
			if (deliveredSymbols.Contains(symbol)
				|| OptionSymbolParser.TryParse(symbol, out _) == false)
			{
				continue;
			}

			if (TryGetDeliveryTime(catalog, symbol) is not { } deliveryTime
				|| deliveryTime > asOf)
			{
				continue;
			}

			deliveries.Add(new AssemblyDelivery
			{
				Symbol = symbol,
				DeliveryTimeMs = deliveryTime.ToUnixTimeMilliseconds(),
			});
		}

		return deliveries;
	}

	/// <summary>
	/// Определяет время доставки опционного символа: спецификация справочника точна,
	/// а для отсутствующих в нём истёкших инструментов время выводится из доски
	/// символа как 08:00 UTC даты экспирации.
	/// </summary>
	private static DateTimeOffset? TryGetDeliveryTime(InstrumentCatalog catalog, string symbol)
	{
		if (catalog.TryGet(symbol, out var entry) && entry.DeliveryTime is not null)
		{
			return entry.DeliveryTime;
		}

		return OptionSymbolParser.TryParse(symbol, out var parts)
			? new DateTimeOffset(parts!.ExpiryDate, TimeSpan.Zero).AddHours(8)
			: null;
	}

	#endregion

	#region Применение плана одной транзакцией

	/// <summary>
	/// Применяет план к базе одной транзакцией: вычищает доменные таблицы
	/// (привязки с комментариями сделок, корректировки PnL, комментарии позиций,
	/// ручные пометки закрытия и сами конструкции с любым содержимым) и создаёт
	/// конструкции и привязки заново. Сырьё и служебные таблицы синхронизации
	/// остаются нетронутыми.
	// Traceability: openspec:domain/construction-assembly#scenario-rebuild-wipes-manual-data
	/// </summary>
	private async Task ApplyPlanAsync(AssemblyPlan plan, CancellationToken cancellationToken)
	{
		using var db = new JournalDbContext(_options);
		await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

		// Чат — запись окружения: пересбор вычищает доменные таблицы и создаёт
		// конструкции заново с новыми ключами, поэтому чаты, привязанные к
		// старым записям конструкций, стираются вместе с ними; чаты без
		// привязки пересбор переживают. Идентификаторы старых записей читаются
		// до вычищения таблиц. Стирание идёт в собственной транзакции
		// хранилища чатов (базы журнала и чатов раздельные, атомарной
		// транзакции над двумя базами нет): удаления копятся в области и
		// фиксируются только после фиксации плана журнала — сбой плана
		// откатывает и журнал, и стирание, пережитых привязанных чатов не
		// остаётся; сбой фиксации стирания после плана оставляет привязанный
		// чат до следующего пересбора. Прежние per-construction базы
		// консультаций не мигрируют: поддержка снята без конвертации истории,
		// пересбор стирает только чаты единого хранилища (чистый лист).
		// Traceability: openspec:chats/history#scenario-chat-rebuild-wipes-bound-chats
		// Traceability: openspec:chats/history#scenario-chat-unbound-chat-survives-rebuild
		var oldConstructionIds = await db.Constructions
			.Select(construction => construction.Id)
			.ToListAsync(cancellationToken)
			.ConfigureAwait(false);
		await using var chatWipe = await _chatStore.BeginRebuildWipeAsync(cancellationToken).ConfigureAwait(false);
		foreach (var constructionId in oldConstructionIds)
		{
			await chatWipe.DeleteForConstructionAsync(constructionId, cancellationToken).ConfigureAwait(false);
		}

		// Дочерние записи вычищаются перед конструкциями: внешние ключи привязок и
		// корректировок запрещают тихое каскадное удаление, пересборка снимает их
		// явным вычищением целиком.
		await db.TradeUserdata.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
		await db.PnLAdjustments.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
		await db.PositionComments.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
		await db.ManualCloseMarks.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
		await db.Constructions.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);

		// Конструкции вставляются в порядке плана: порядковый номер плана задаёт
		// и порядок строк базы, поэтому повторный прогон воспроизводит состояние.
		// Имя и статус берутся из плана: имя пересчитано по живым ногам, статус
		// следует за опционным прикрытием — те же правила, что у инкремента.
		// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#requirement-full-rebuild-semantics
		var created = plan.Constructions
			.Select(planned => new Construction
			{
				Name = planned.Name,
				Status = planned.Status,
			})
			.ToList();
		db.Constructions.AddRange(created);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		// Соответствие «номер плана → ключ базы» строится по фактическим
		// сгенерированным ключам, а не по предположению о нумерации SQLite.
		var databaseIdByPlanId = new Dictionary<long, long>(created.Count);
		for (var index = 0; index < plan.Constructions.Count; index++)
		{
			databaseIdByPlanId[plan.Constructions[index].Id] = created[index].Id;
		}

		db.TradeUserdata.AddRange(plan.Bindings
			.Select(binding => new TradeUserdata
			{
				ExecId = binding.Key,
				ConstructionId = databaseIdByPlanId[binding.Value],
			}));
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		// Сначала фиксируется план журнала, затем стирание чатов: сбой до этой
		// строки откатывает и журнал, и стирание чатов, привязанные чаты
		// остаются жить вместе со старыми записями конструкций.
		// Traceability: openspec:chats/history#scenario-chat-rebuild-wipes-bound-chats
		await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
		await chatWipe.CommitAsync(cancellationToken).ConfigureAwait(false);
	}

	/// <summary>
	/// Применяет план инкремента к базе одной транзакцией: вставляет новые
	/// конструкции в порядке плана, ставит привязки «Входящих» — строку без
	/// привязки получает ключ конструкции, комментарий сделки остаётся нетронутым, —
	/// и точечно обновляет производные атрибуты затронутых существующих конструкций:
	/// статус и имя (пока оно не зафиксировано вручную). Ручные данные — капитал,
	/// риск и профит, комментарии, корректировки PnL, пометки закрытия — планом
	/// не содержатся и не изменяются.
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#requirement-incremental-inbox-assembly
	// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#scenario-rerun-processes-only-inbox
	// Traceability: change:refine-construction-assembly/design#d8
	/// </summary>
	private async Task ApplyInboxPlanAsync(AssemblyPlan plan, CancellationToken cancellationToken)
	{
		using var db = new JournalDbContext(_options);
		await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

		// Новые конструкции вставляются в порядке плана — порядок строк базы
		// воспроизводим между прогонами, как и у пересбора.
		var newPlanned = plan.Constructions
			.Where(construction => construction.IsSeeded == false)
			.ToList();
		var created = newPlanned
			.Select(planned => new Construction
			{
				Name = planned.Name,
				Status = planned.Status,
			})
			.ToList();
		db.Constructions.AddRange(created);
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		// Существующие конструкции ключуются собственным Id базы, новые —
		// фактически сгенерированными ключами вставки.
		var databaseIdByPlanId = new Dictionary<long, long>(plan.Constructions.Count);
		foreach (var planned in plan.Constructions)
		{
			if (planned.IsSeeded)
			{
				databaseIdByPlanId[planned.Id] = planned.Id;
			}
		}
		for (var index = 0; index < created.Count; index++)
		{
			databaseIdByPlanId[newPlanned[index].Id] = created[index].Id;
		}

		// Привязка ставится только непривязанной записи: строка с комментарием без
		// привязки получает ключ конструкции, комментарий пользователя сохраняется.
		var userdataByExecId = (await db.TradeUserdata.ToListAsync(cancellationToken).ConfigureAwait(false))
			.ToDictionary(row => row.ExecId, StringComparer.Ordinal);
		foreach (var execId in plan.Bindings.Keys.OrderBy(key => key, StringComparer.Ordinal))
		{
			var databaseId = databaseIdByPlanId[plan.Bindings[execId]];
			if (userdataByExecId.TryGetValue(execId, out var row))
			{
				if (row.ConstructionId != databaseId)
				{
					row.ConstructionId = databaseId;
				}
			}
			else
			{
				db.TradeUserdata.Add(new TradeUserdata
				{
					ExecId = execId,
					ConstructionId = databaseId,
				});
			}
		}
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		// Производные атрибуты существующих конструкций пишутся точечно:
		// только фактически изменившиеся имя (у незафиксированного вручную)
		// и статус; архивный статус и ручное имя приходят из плана неизменными.
		// Traceability: change:close-construction-on-all-positions/specs/domain/construction-assembly/spec#requirement-status-follows-all-positions
		// Traceability: change:refine-construction-assembly/specs/domain/construction-assembly/spec#requirement-derived-construction-naming
		var seededIds = plan.Constructions
			.Where(construction => construction.IsSeeded)
			.Select(construction => construction.Id)
			.ToList();
		var existingById = await db.Constructions
			.Where(construction => seededIds.Contains(construction.Id))
			.ToDictionaryAsync(construction => construction.Id, cancellationToken)
			.ConfigureAwait(false);
		foreach (var planned in plan.Constructions)
		{
			if (planned.IsSeeded == false
				|| existingById.TryGetValue(planned.Id, out var row) == false)
			{
				continue;
			}

			if (row.Status != planned.Status)
			{
				row.Status = planned.Status;
			}

			if (planned.NameIsManual == false && row.Name != planned.Name)
			{
				row.Name = planned.Name;
			}
		}
		await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
	}

	#endregion
}
