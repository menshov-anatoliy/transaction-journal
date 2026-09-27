using TransactionJournal.Materialization;

namespace TransactionJournal.Domain.ConstructionAssembly;

/// <summary>
/// Детерминированный алгоритм сборки конструкций из опционных исполнений
/// (правила v5): записи упорядочены по (ExecTimeMs, ExecId); исполнения одного
/// базового актива с зазором не более 60 минут образуют окно; delivery-запись
/// окно обрывает и гасит остаток ноги у владельца. Окно с закрывающими
/// сделками — ролл: закрывающие количества погашаются у владельцев, открытия
/// присоединяются к пережившей окно конструкции, иначе — к открытой раньше
/// среди владельцев. Окно без закрывающих сделок усредняется в единственную
/// живую конструкцию актива (вне дня экспирации, одноногое либо с повторением
/// пар «страйк + доска»), иначе открывает новую конструкцию. Конструкция
/// закрывается, когда нулевой становится каждая её нога. Сделки робота
/// привязываются по базовому активу и периоду жизни конструкции. Прогон
/// детерминирован: повторная сборка над тем же сырьём даёт тот же план.
// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#requirement-deterministic-option-assembly
/// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#requirement-robot-trade-binding
/// Traceability: change:add-construction-auto-assembly/design#d2
/// </summary>
public sealed class ConstructionAssembler
{
	/// <summary>Максимальный зазор между исполнениями одного окна, минут.</summary>
	public const int WindowGapMinutes = 60;

	/// <summary>Зазор окна в миллисекундах.</summary>
	private static readonly long WindowGapMs = WindowGapMinutes * 60_000L;

	/// <summary>Категория опционных записей — материал группировки.</summary>
	private const string OptionCategory = "option";

	/// <summary>Категория фьючерсных записей — сделки робота для привязки по периоду.</summary>
	private const string LinearCategory = "linear";

	#region Точка входа

	/// <summary>
	/// Строит план сборки из снимка исполнений и закрывающих событий экспирации.
	/// Опционные исполнения группируются в конструкции, фьючерсные привязываются
	/// по активу и периоду жизни; всё, что не привязалось, остаётся во «Входящих».
	/// </summary>
	/// <param name="executions">Исполнения сделок (торговые записи, без фандинга).</param>
	/// <param name="deliveries">Закрывающие события экспирации: delivery-записи биржи и выведенные OTM-закрывающие.</param>
	/// <exception cref="ArgumentNullException">Снимок не задан.</exception>
	public AssemblyPlan Assemble(
		IReadOnlyCollection<AssemblyExecution> executions,
		IReadOnlyCollection<AssemblyDelivery> deliveries)
	{
		ArgumentNullException.ThrowIfNull(executions);
		ArgumentNullException.ThrowIfNull(deliveries);

		var run = new AssemblyRun();

		// Прогон детерминирован: записи упорядочиваются по (ExecTimeMs, ExecId) до любых
		// правил, повторный execId схлопывается на первой встрече — состав входа не
		// зависит от порядка выдачи хранилища.
		var seenExecIds = new HashSet<string>(StringComparer.Ordinal);
		var orderedExecutions = executions
			.OrderBy(execution => execution.ExecTimeMs)
			.ThenBy(execution => execution.ExecId, StringComparer.Ordinal)
			.ToList();
		var distinctExecutions = new List<AssemblyExecution>(orderedExecutions.Count);
		foreach (var execution in orderedExecutions)
		{
			if (seenExecIds.Add(execution.ExecId))
			{
				distinctExecutions.Add(execution);
			}
		}

		// Опционные исполнения группируются по базовому активу; нулевые количества и
		// неразобранные символы в группировку не попадают и остаются во «Входящих».
		var executionsByAsset = new SortedDictionary<string, List<AssemblyExecution>>(StringComparer.Ordinal);
		var linearExecutions = new List<AssemblyExecution>();
		foreach (var execution in distinctExecutions)
		{
			if (execution.SignedQuantity == 0m)
			{
				continue;
			}

			if (string.Equals(execution.Category, OptionCategory, StringComparison.Ordinal))
			{
				if (OptionSymbolParser.TryParse(execution.Symbol, out var parts) == false)
				{
					continue;
				}

				if (executionsByAsset.TryGetValue(parts!.BaseCoin, out var assetExecutions) == false)
				{
					assetExecutions = new List<AssemblyExecution>();
					executionsByAsset.Add(parts.BaseCoin, assetExecutions);
				}

				assetExecutions.Add(execution);
			}
			else if (string.Equals(execution.Category, LinearCategory, StringComparison.Ordinal))
			{
				linearExecutions.Add(execution);
			}
		}

		// Активы обрабатываются в алфавитном порядке: нумерация конструкций и имена
		// плана не зависят от того, в каком порядке активы пришли в снимке.
		foreach (var assetPair in executionsByAsset)
		{
			ProcessAsset(run, assetPair.Key, assetPair.Value, deliveries);
		}

		BindLinearExecutions(run, linearExecutions);

		return new AssemblyPlan
		{
			Constructions = run.Constructions
				.OrderBy(construction => construction.Id)
				.Select(construction => new PlannedConstruction
				{
					Id = construction.Id,
					Name = construction.Name,
					BaseCoin = construction.BaseCoin,
					OpenedAtMs = construction.OpenedAtMs,
					ClosedAtMs = construction.ClosedAtMs,
					Legs = construction.Legs
						.Select(leg => new PlannedLeg
						{
						Symbol = leg.Symbol,
							Strike = leg.Parts.Strike,
							BoardExpiryDate = leg.Parts.ExpiryDate,
							Type = leg.Parts.Type,
							Quantity = leg.Quantity,
						})
						.ToList(),
				})
				.ToList(),
			Bindings = run.Bindings,
			InboxCount = distinctExecutions.Count - run.Bindings.Count,
		};
	}

	#endregion

	#region Кластеризация и классификация

	/// <summary>
	/// Кластеризует исполнения одного базового актива в окна: зазор между соседними
	/// исполнениями не более 60 минут, delivery-запись разрывает накопленное окно.
	/// Используются только опционные записи с разбираемым символом.
	/// </summary>
	/// <param name="executions">Исполнения одного базового актива.</param>
	/// <param name="deliveries">Закрывающие события экспирации того же актива.</param>
	public static IReadOnlyList<AssemblyWindow> BuildWindows(
		IReadOnlyList<AssemblyExecution> executions,
		IReadOnlyCollection<AssemblyDelivery> deliveries)
	{
		ArgumentNullException.ThrowIfNull(executions);
		ArgumentNullException.ThrowIfNull(deliveries);

		var windows = new List<AssemblyWindow>();
		WalkTimeline(
			executions,
			deliveries,
			window => windows.Add(new AssemblyWindow
			{
				Executions = window.Select(item => item.Execution).ToList(),
			}),
			_ => { });
		return windows;
	}

	/// <summary>
	/// Классифицирует сделку против текущего остатка аккаунта: продажа занятой ноги
	/// (при остатке больше нуля) и откуп короткой (при остатке меньше нуля) —
	/// закрывающие, остальные сделки — открывающие.
	/// </summary>
	/// <param name="signedQuantity">Знаковое количество сделки.</param>
	/// <param name="heldQuantity">Знаковый остаток аккаунта по символу до сделки.</param>
	public static bool IsClosingTrade(decimal signedQuantity, decimal heldQuantity) =>
		(signedQuantity < 0m && heldQuantity > 0m) || (signedQuantity > 0m && heldQuantity < 0m);

	#endregion

	#region Проход актива

	/// <summary>Обрабатывает таймлайн одного актива: окна группировки и delivery-события в порядке времени.</summary>
	private static void ProcessAsset(
		AssemblyRun run,
		string baseCoin,
		IReadOnlyList<AssemblyExecution> executions,
		IReadOnlyCollection<AssemblyDelivery> deliveries)
	{
		WalkTimeline(
			executions,
			deliveries,
			window => FlushWindow(run, baseCoin, window),
			delivery => ApplyDelivery(run, delivery));
	}

	/// <summary>
	/// Обрабатывает готовое окно: классифицирует сделки по живому остатку аккаунта,
	/// закрывающие погашает у владельцев, открывающие присоединяет к цели —
	/// пережившей окно, усредняемой живой конструкции или новой конструкции.
	/// </summary>
	private static void FlushWindow(AssemblyRun run, string baseCoin, IReadOnlyList<WindowItem> window)
	{
		var openings = new List<WindowItem>();
		var owners = new List<ConstructionState>();
		var hasClosing = false;

		// Классификация идёт по состоянию аккаунта на момент каждого исполнения:
		// сделка против ненулевого остатка — закрывающая, иначе — открывающая.
		// Открывающие сделки буферизуются до определения цели окна.
		foreach (var item in window)
		{
			var owner = FindOwner(run, item.Symbol);
			if (IsClosingTrade(item.Execution.SignedQuantity, HeldOf(run, item.Symbol)) && owner is not null)
			{
				hasClosing = true;
				RepayAtOwner(run, owner, item);
				if (owners.Contains(owner) == false)
				{
					owners.Add(owner);
				}
			}
			else
			{
				openings.Add(item);
			}
		}

		ConstructionState target;
		if (hasClosing)
		{
			// Цель ролла — владелец, пережившая окно с ненулевым остатком после
			// погашений; если переживших нет — открытая раньше среди владельцев.
			var orderedOwners = owners
				.OrderBy(construction => construction.OpenedAtMs)
				.ThenBy(construction => construction.Id)
				.ToList();
			target = orderedOwners.FirstOrDefault(construction => construction.TotalQuantity != 0m)
				?? orderedOwners[0];
		}
		else
		{
			target = FindAveragingTarget(run, baseCoin, window)
				?? OpenConstruction(run, baseCoin, window);
		}

		foreach (var opening in openings)
		{
			AttachTrade(run, target, opening);
		}

		// Проверка закрытия выполняется после присоединения открытий окна: погашение
		// обнуляет ногу раньше, чем окно рефинансирует конструкцию новыми ногами,
		// поэтому промежуточный ноль внутри окна конструкцию не закрывает —
		// закрывается состояние после всех сделок окна.
		// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#scenario-roll-inherits-surviving-owner
		var windowEndTimeMs = window[^1].Execution.ExecTimeMs;
		CloseConstructionIfEmpty(run, target, windowEndTimeMs);
		foreach (var owner in owners)
		{
			CloseConstructionIfEmpty(run, owner, windowEndTimeMs);
		}
	}

	/// <summary>
	/// Подбирает живую конструкцию для усреднения: единственная живая конструкция
	/// того же актива, день окна не совпадает с днём экспирации самой поздней доски
	/// её ног, окно одноногое (один символ, сколько бы исполнений он ни содержал)
	/// либо каждая нога окна повторяет пару «страйк + доска» уже имеющейся ноги;
	/// иначе окно открывает новую конструкцию. Стреддл-окно из колла и пута одного
	/// нового страйка — двухногое: оно открывает новую конструкцию, а не усредняется.
	/// </summary>
	// Усреднение ищется только среди конструкций своего базового актива: чужая
	// живая конструкция другого актива не может принимать окно — иначе окна
	// разных активов сливаются в одну конструкцию.
	// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#requirement-deterministic-option-assembly
	private static ConstructionState? FindAveragingTarget(AssemblyRun run, string baseCoin, IReadOnlyList<WindowItem> window)
	{
		var alive = run.Constructions
			.Where(construction => construction.IsAlive
				&& string.Equals(construction.BaseCoin, baseCoin, StringComparison.Ordinal))
			.ToList();
		if (alive.Count != 1)
		{
			return null;
		}

		var target = alive[0];

		// Усреднение запрещено в день экспирации самой поздней доски: такой день
		// означает начало новой серии, а не докупку текущей конструкции.
		var windowDay = DateTimeOffset
			.FromUnixTimeMilliseconds(window[0].Execution.ExecTimeMs)
			.UtcDateTime.Date;
		var latestBoard = target.Legs.Max(leg => leg.Parts.ExpiryDate).Date;
		if (windowDay == latestBoard)
		{
			return null;
		}

		var existingPairs = target.Legs
			.Select(leg => (leg.Parts.Strike, leg.Parts.ExpiryDate.Date))
			.ToHashSet();

		// Одноногоесть считается по числу разных символов ноги, а не по числу разных
		// пар «страйк + доска»: окно из колла и пута одного страйка — двухногое даже
		// при совпадении пары, и без совпадающих пар у конструкции оно открывает
		// новую конструкцию, а не усредняется.
		var isSingleLegWindow = window
			.Select(item => item.Symbol)
			.Distinct(StringComparer.Ordinal)
			.Take(2)
			.Count() == 1;
		if (isSingleLegWindow)
		{
			return target;
		}

		var windowPairs = window
			.Select(item => (item.Parts.Strike, item.Parts.ExpiryDate.Date))
			.Distinct()
			.ToList();
		if (windowPairs.All(pair => existingPairs.Contains(pair)))
		{
			return target;
		}

		return null;
	}

	/// <summary>Открывает новую конструкцию из окна: имя строится из состава его ног.</summary>
	private static ConstructionState OpenConstruction(
		AssemblyRun run,
		string baseCoin,
		IReadOnlyList<WindowItem> window)
	{
		var construction = new ConstructionState
		{
			Id = run.TakeId(),
			Name = ConstructionNameBuilder.BuildName(baseCoin, window.Select(item => item.Parts).ToList()),
			BaseCoin = baseCoin,
			OpenedAtMs = window[0].Execution.ExecTimeMs,
		};
		run.Constructions.Add(construction);
		return construction;
	}

	#endregion

	#region Погашение, присоединение и закрытие

	/// <summary>
	/// Погашает закрывающее количество у владельца ноги. Погашение не переводит
	/// остаток через ноль: избыточная часть сверх остатка владельца баланс не меняет.
	/// Закрытие владельца здесь не проверяется — ноль внутри окна ещё не финален,
	/// окно может рефинансировать конструкцию буферизованными открытиями.
	/// </summary>
	private static void RepayAtOwner(AssemblyRun run, ConstructionState owner, WindowItem item)
	{
		var leg = owner.LegsBySymbol[item.Symbol];
		var updated = leg.Quantity + item.Execution.SignedQuantity;
		if ((leg.Quantity > 0m && updated < 0m) || (leg.Quantity < 0m && updated > 0m))
		{
			updated = 0m;
		}

		leg.Quantity = updated;
		run.Bindings[item.Execution.ExecId] = owner.Id;
	}

	/// <summary>Присоединяет открывающую сделку к конструкции, создавая ногу при первом появлении символа.</summary>
	private static void AttachTrade(AssemblyRun run, ConstructionState target, WindowItem item)
	{
		if (target.LegsBySymbol.TryGetValue(item.Symbol, out var leg) == false)
		{
			leg = new LegState
			{
				Symbol = item.Symbol,
				Parts = item.Parts,
				Quantity = 0m,
			};
			target.LegsBySymbol.Add(item.Symbol, leg);
			target.Legs.Add(leg);
		}

		leg.Quantity += item.Execution.SignedQuantity;
		run.Bindings[item.Execution.ExecId] = target.Id;
	}

	/// <summary>
	/// Гасит закрывающее событие экспирации: остаток ноги обнуляется у владельца
	/// немедленно, независимо от знака и размера остатка.
	/// </summary>
	private static void ApplyDelivery(AssemblyRun run, AssemblyDelivery delivery)
	{
		var owner = FindOwner(run, delivery.Symbol);
		if (owner is null)
		{
			return;
		}

		owner.LegsBySymbol[delivery.Symbol].Quantity = 0m;
		CloseConstructionIfEmpty(run, owner, delivery.DeliveryTimeMs);
	}

	/// <summary>
	/// Закрывает конструкцию, когда нулевой становится каждая её нога, а не их сумма:
	/// зачёт длинной и короткой ног конструкцию не закрывает.
	/// </summary>
	private static void CloseConstructionIfEmpty(AssemblyRun run, ConstructionState construction, long eventTimeMs)
	{
		if (construction.IsAlive
			&& construction.Legs.Count > 0
			&& construction.Legs.All(leg => leg.Quantity == 0m))
		{
			construction.ClosedAtMs = eventTimeMs;
		}
	}

	#endregion

	#region Привязка сделок робота

	/// <summary>
	/// Привязывает фьючерсные исполнения к конструкциям того же базового актива
	/// по периоду жизни [открытие .. закрытие]; перекрытие периодов разрешается
	/// конструкцией, открытой раньше; вне всех периодов сделка остаётся во «Входящих».
	/// </summary>
	private static void BindLinearExecutions(AssemblyRun run, IEnumerable<AssemblyExecution> executions)
	{
		foreach (var execution in executions)
		{
			if (LinearSymbolParser.TryParseBaseCoin(execution.Symbol, out var baseCoin) == false)
			{
				continue;
			}

			// Границы периода жизни включительны: сделка в момент открытия и в момент
			// закрытия конструкции всё ещё покрывается её периодом.
			var covering = run.Constructions
				.Where(construction => string.Equals(construction.BaseCoin, baseCoin, StringComparison.Ordinal)
					&& construction.OpenedAtMs <= execution.ExecTimeMs
					&& (construction.ClosedAtMs is null || execution.ExecTimeMs <= construction.ClosedAtMs))
				.OrderBy(construction => construction.OpenedAtMs)
				.ThenBy(construction => construction.Id)
				.FirstOrDefault();
			if (covering is not null)
			{
				run.Bindings[execution.ExecId] = covering.Id;
			}
		}
	}

	#endregion

	#region Состояние прогона

	/// <summary>Находит владельца символа — конструкцию с ненулевым остатком ноги, открытую раньше прочих.</summary>
	private static ConstructionState? FindOwner(AssemblyRun run, string symbol) =>
		run.Constructions
			.Where(construction => construction.LegsBySymbol.TryGetValue(symbol, out var leg) && leg.Quantity != 0m)
			.OrderBy(construction => construction.OpenedAtMs)
			.ThenBy(construction => construction.Id)
			.FirstOrDefault();

	/// <summary>Суммарный знаковый остаток аккаунта по символу — база классификации закрывающих сделок.</summary>
	private static decimal HeldOf(AssemblyRun run, string symbol) =>
		run.Constructions.Sum(construction =>
			construction.LegsBySymbol.TryGetValue(symbol, out var leg) ? leg.Quantity : 0m);

	/// <summary>Нога конструкции в состоянии прогона: разобранные свойства символа и текущий остаток.</summary>
	private sealed class LegState
	{
		/// <summary>Символ опциона ноги.</summary>
		public required string Symbol { get; init; }

		/// <summary>Разобранные свойства символа: актив, доска, страйк, тип.</summary>
		public required OptionSymbolParts Parts { get; init; }

		/// <summary>Текущий знаковый остаток ноги.</summary>
		public decimal Quantity { get; set; }
	}

	/// <summary>Конструкция в состоянии прогона: имя, период жизни и ноги с остатками.</summary>
	private sealed class ConstructionState
	{
		/// <summary>Порядковый номер конструкции в прогоне.</summary>
		public required long Id { get; init; }

		/// <summary>Детерминированное имя из открывающего окна.</summary>
		public required string Name { get; init; }

		/// <summary>Базовый актив конструкции.</summary>
		public required string BaseCoin { get; init; }

		/// <summary>Время первого исполнения открывающего окна.</summary>
		public required long OpenedAtMs { get; init; }

		/// <summary>Время закрытия; null — конструкция жива.</summary>
		public long? ClosedAtMs { get; set; }

		/// <summary>Ноги по символу — доступ к балансу по закрывающим событиям.</summary>
		public Dictionary<string, LegState> LegsBySymbol { get; } = new(StringComparer.Ordinal);

		/// <summary>Ноги в порядке первого появления символа — детерминированный порядок плана.</summary>
		public List<LegState> Legs { get; } = new();

		/// <summary>Конструкция жива, пока закрытие не наступило.</summary>
		public bool IsAlive => ClosedAtMs is null;

		/// <summary>Суммарный знаковый остаток всех ног — признак «пережившей окно» конструкции.</summary>
		public decimal TotalQuantity => Legs.Sum(leg => leg.Quantity);
	}

	/// <summary>Общее состояние прогона: счётчик идентификаторов, конструкции и привязки.</summary>
	private sealed class AssemblyRun
	{
		/// <summary>Счётчик порядковых номеров конструкций.</summary>
		private long _nextId = 1;

		/// <summary>Конструкции прогона во всех активах.</summary>
		public List<ConstructionState> Constructions { get; } = new();

		/// <summary>Привязки execId → порядковый номер конструкции.</summary>
		public Dictionary<string, long> Bindings { get; } = new(StringComparer.Ordinal);

		/// <summary>Выделяет следующий порядковый номер конструкции.</summary>
		public long TakeId() => _nextId++;
	}

	/// <summary>Элемент окна: исполнение с заранее разобранными свойствами символа.</summary>
	private readonly record struct WindowItem(
		AssemblyExecution Execution,
		string Symbol,
		OptionSymbolParts Parts);

	#endregion

	#region Обход таймлайна

	/// <summary>
	/// Обходит таймлайн актива: исполнения по (ExecTimeMs, ExecId), delivery-события
	/// по (DeliveryTimeMs, Symbol); при равном времени delivery применяется раньше
	/// исполнения. Окно отдаётся обработчику при разрыве delivery-событием, зазоре
	/// более 60 минут и в конце таймлайна.
	/// </summary>
	private static void WalkTimeline(
		IReadOnlyList<AssemblyExecution> executions,
		IReadOnlyCollection<AssemblyDelivery> deliveries,
		Action<IReadOnlyList<WindowItem>> onWindow,
		Action<AssemblyDelivery> onDelivery)
	{
		var orderedExecutions = new List<WindowItem>();
		foreach (var execution in executions
			.OrderBy(execution => execution.ExecTimeMs)
			.ThenBy(execution => execution.ExecId, StringComparer.Ordinal))
		{
			if (OptionSymbolParser.TryParse(execution.Symbol, out var parts))
			{
				orderedExecutions.Add(new WindowItem(execution, execution.Symbol, parts!));
			}
		}

		var orderedDeliveries = deliveries
			.Where(delivery => OptionSymbolParser.TryParse(delivery.Symbol, out _))
			.OrderBy(delivery => delivery.DeliveryTimeMs)
			.ThenBy(delivery => delivery.Symbol, StringComparer.Ordinal)
			.ToList();

		List<WindowItem> window = new();
		var executionIndex = 0;
		var deliveryIndex = 0;
		var windowOpen = false;
		while (executionIndex < orderedExecutions.Count || deliveryIndex < orderedDeliveries.Count)
		{
			// При равном времени delivery-событие обрывает окно раньше исполнения:
			// экспирация фиксирует состояние до сделок этого же момента.
			var takeDelivery = deliveryIndex < orderedDeliveries.Count
				&& (executionIndex >= orderedExecutions.Count
					|| orderedDeliveries[deliveryIndex].DeliveryTimeMs
						<= orderedExecutions[executionIndex].Execution.ExecTimeMs);
			if (takeDelivery)
			{
				var delivery = orderedDeliveries[deliveryIndex++];
				if (windowOpen)
				{
					onWindow(window);
					window = new List<WindowItem>();
					windowOpen = false;
				}

				onDelivery(delivery);
			}
			else
			{
				var item = orderedExecutions[executionIndex++];
				if (windowOpen
					&& item.Execution.ExecTimeMs - window[^1].Execution.ExecTimeMs > WindowGapMs)
				{
					onWindow(window);
					window = new List<WindowItem>();
					windowOpen = false;
				}

				window.Add(item);
				windowOpen = true;
			}
		}

		if (windowOpen)
		{
			onWindow(window);
		}
	}

	#endregion
}
