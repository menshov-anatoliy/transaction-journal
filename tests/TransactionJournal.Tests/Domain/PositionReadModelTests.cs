using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using NUnit.Framework;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Domain;

/// <summary>
/// Проверки read-модели позиций на стыке с сырым хранилищем синхронизации
/// и пользовательскими записями домена: чистый остаток вычисляется из сделок
/// и закрывающих записей при чтении, ручные пометки вливаются в единый поток
/// закрывающих записей с ценой пользователя или последней маркой, а избыточные
/// записи отражаются предупреждениями, не переворачивая позицию.
/// </summary>
[TestClass]
public class PositionReadModelTests
{
	private const string CallSymbol = "BTC-29DEC23-45000-C";
	private const string LinearSymbol = "BTCUSDT";

	private static readonly DateTimeOffset FetchedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	/// <summary>Каноническое время delivery инструмента опциона из справочника: 29DEC23 08:00 UTC.</summary>
	private static readonly DateTimeOffset OptionDelivery = new(2023, 12, 29, 8, 0, 0, TimeSpan.Zero);

	private static readonly long OptionDeliveryMs = OptionDelivery.ToUnixTimeMilliseconds();

	private string _databasePath = null!;
	private ConstructionService _constructionService = null!;
	private TradeBindingService _bindingService = null!;
	private ManualCloseMarkService _markService = null!;

	[TestInitialize]
	public void Initialize()
	{
		// Каждая проверка работает со своей пустой базой во временной папке.
		_databasePath = Path.Combine(Path.GetTempPath(), $"journal-positions-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
			SeedInstrumentCatalog(db);
		}

		_constructionService = new ConstructionService(CreateOptions());
		_bindingService = new TradeBindingService(CreateOptions());
		_markService = new ManualCloseMarkService(CreateOptions());
	}

	[TestCleanup]
	public void Cleanup()
	{
		// Пул соединений SQLite держит файл базы открытым — сбрасываем его перед удалением.
		Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

		// Временная база и соседние WAL/SHM-файлы удаляются после каждой проверки.
		foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
		{
			var file = _databasePath + suffix;
			if (File.Exists(file))
			{
				File.Delete(file);
			}
		}
	}

	[TestMethod]
	[Description("Остаток позиции вычисляется из сделок при чтении: две покупки и одна продажа дают разность")]
	public async Task TryIfResidualComputedFromTradesAtRead()
	{
		// Arrange: конструкция с двумя покупками (0.0002 и 0.0001) и одной продажей (0.0001)
		// одного инструмента.
		var construction = await _constructionService.CreateAsync("Стреддл BTC", 1000m);
		await AddLinearTradesAsync(
			("exec-buy-1", "Buy", "0.0002", ExecMs(2023, 12, 28, 10, 0)),
			("exec-buy-2", "Buy", "0.0001", ExecMs(2023, 12, 28, 10, 30)),
			("exec-sell-1", "Sell", "0.0001", ExecMs(2023, 12, 28, 11, 0)));
		await _bindingService.BindBatchAsync(construction.Id,
			new[] { "exec-buy-1", "exec-buy-2", "exec-sell-1" });

		// Act: читаем позиции.
		var readModel = new PositionReadModel(CreateOptions());
		var result = await readModel.ListAsync();

		// Assert: одна позиция с чистым остатком, равным разности знаковых количеств
		// сделок; позиция открыта, закрывающих записей и предупреждений нет.
		// Требование: остаток вычисляется из сделок при чтении.
		// Traceability: openspec:domain/constructions#scenario-residual-computed-from-trades
		var position = result.Positions.Single();
		Assert.That(position.ConstructionId, Is.EqualTo(construction.Id));
		Assert.That(position.Symbol, Is.EqualTo(LinearSymbol));
		Assert.That(position.Residual, Is.EqualTo(0.0002m));
		Assert.That(position.IsOpen, Is.True);
		Assert.That(result.ClosingEntries, Is.Empty);
		Assert.That(result.Warnings, Is.Empty);
	}

	[TestMethod]
	[Description("Позиция не редактируется напрямую: read-модель не предоставляет мутирующего API")]
	public void TryIfPositionExposesNoMutatingApi()
	{
		// Arrange: шаблон имён мутирующих операций и публичный API read-модели.
		// Требование: позиция не редактируется напрямую — количество меняется только
		// сделками и закрывающими записями.
		// Traceability: openspec:domain/constructions#scenario-position-not-directly-editable
		var mutatingName = new Regex("^(Add|Create|Set|Update|Edit|Change|Delete|Remove|Save|Apply|Bind|Unbind|Rename|Close|Open)");

		// Act: собираем объявленные публичные методы read-модели и свойства снапшота.
		var methodNames = typeof(PositionReadModel)
			.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
			.Select(method => method.Name)
			.ToArray();

		// Assert: мутирующих методов нет, а свойства снапшота позиции доступны только
		// на записи — изменить остаток позиции напрямую нечем.
		Assert.That(methodNames, Is.Not.Empty);
		Assert.That(methodNames.Any(mutatingName.IsMatch), Is.False);
		foreach (var property in typeof(PositionSnapshot).GetProperties())
		{
			var setter = property.GetSetMethod();
			var isInitOnly = setter is not null && setter.ReturnParameter
				.GetRequiredCustomModifiers()
				.Any(type => string.Equals(type.Name, "IsExternalInit", StringComparison.Ordinal));
			Assert.That(setter is null || isInitOnly, Is.True,
				$"Свойство {property.Name} снапшота позиции обязано быть неизменяемым.");
		}
	}

	[TestMethod]
	[Description("Чтение по конструкции возвращает только её позиции")]
	public async Task TryIfScopedListReturnsOnlyConstructionPositions()
	{
		// Arrange: две конструкции со сделками по разным инструментам.
		var first = await _constructionService.CreateAsync("Первая", 1000m);
		var second = await _constructionService.CreateAsync("Вторая", 2000m);
		await AddLinearTradesAsync(("exec-first", "Buy", "0.01", ExecMs(2023, 12, 28, 10, 0)));
		await AddOptionTradeAsync("exec-second", "Buy", "0.0001", "100");
		await _bindingService.BindAsync(first.Id, "exec-first");
		await _bindingService.BindAsync(second.Id, "exec-second");

		// Act: читаем позиции только первой конструкции.
		var readModel = new PositionReadModel(CreateOptions());
		var scoped = await readModel.ListAsync(first.Id);

		// Assert: в срезе одна позиция первой конструкции; сделка второй конструкции
		// и «Входящие» в срез не попадают.
		var position = scoped.Positions.Single();
		Assert.That(position.ConstructionId, Is.EqualTo(first.Id));
		Assert.That(position.Symbol, Is.EqualTo(LinearSymbol));
	}

	[TestMethod]
	[Description("Встречные сделки закрывают позицию без специальных записей")]
	public async Task TryIfOffsettingTradesClosePosition()
	{
		// Arrange: покупка и равная продажа одного инструмента в одной конструкции.
		var construction = await _constructionService.CreateAsync("Скальп", 500m);
		await AddLinearTradesAsync(
			("exec-buy", "Buy", "0.01", ExecMs(2023, 12, 28, 10, 0)),
			("exec-sell", "Sell", "0.01", ExecMs(2023, 12, 28, 11, 0)));
		await _bindingService.BindBatchAsync(construction.Id, new[] { "exec-buy", "exec-sell" });

		// Act: читаем позиции.
		var readModel = new PositionReadModel(CreateOptions());
		var result = await readModel.ListAsync();

		// Assert: остаток нулевой — позиция закрыта встречными сделками, без специальных
		// закрывающих записей и предупреждений.
		// Требование: нулевой остаток после очередной сделки закрывает позицию сделками.
		// Traceability: openspec:domain/constructions#scenario-close-by-offsetting-trades
		var position = result.Positions.Single();
		Assert.That(position.Residual, Is.EqualTo(0m));
		Assert.That(position.IsOpen, Is.False);
		Assert.That(result.ClosingEntries, Is.Empty);
		Assert.That(result.Warnings, Is.Empty);
	}

	[TestMethod]
	[Description("Закрывающая запись delivery из синхронизации обнуляет остаток позиции")]
	public async Task TryIfDeliveryClosingEntryZeroesResidual()
	{
		// Arrange: покупка опциона 0.0001 по 100 привязана к конструкции; аккаунтовая
		// delivery-запись с расчётной ценой 46000 и страйком 45000 даёт внутреннюю
		// стоимость 1000.
		var construction = await _constructionService.CreateAsync("Колл BTC", 700m);
		await AddOptionTradeAsync("exec-call-buy", "Buy", "0.0001", "100");
		await _bindingService.BindAsync(construction.Id, "exec-call-buy");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.RawDeliveries.Add(Delivery(deliveryPrice: "46000", strike: "45000", fee: "0", deliveryRpl: "0.09"));
			await db.SaveChangesAsync();
		}

		// Act: читаем позиции.
		var readModel = new PositionReadModel(CreateOptions());
		var result = await readModel.ListAsync();

		// Assert: delivery-запись вошла в поток закрывающих записей с количеством,
		// обнуляющим остаток, и эффективной ценой внутренней стоимости; позиция закрыта.
		// Требование: закрывающие записи delivery/OTM-экспирации из синхронизации
		// закрывают позицию по ADR-0002.
		// Traceability: openspec:domain/constructions#requirement-position-close-lifecycle
		// Traceability: adr:docs/adr/0002-option-expiry-closing-entries.md#option-expiry-closing-entries
		var position = result.Positions.Single();
		Assert.That(position.Residual, Is.EqualTo(0m));
		Assert.That(position.IsOpen, Is.False);
		var entry = result.ClosingEntries.Single();
		Assert.That(entry.ConstructionId, Is.EqualTo(construction.Id));
		Assert.That(entry.Symbol, Is.EqualTo(CallSymbol));
		Assert.That(entry.Kind, Is.EqualTo(PositionClosingKind.Delivery));
		Assert.That(entry.ClosedAt, Is.EqualTo(OptionDelivery));
		Assert.That(entry.Quantity, Is.EqualTo(-0.0001m));
		Assert.That(entry.Price, Is.EqualTo(1000m));
		Assert.That(entry.SourceKey, Is.EqualTo($"{CallSymbol}|{OptionDeliveryMs}"));
		Assert.That(result.Warnings, Is.Empty);
	}

	[TestMethod]
	[Description("Ручная пометка закрывает позицию инструмента без биржевых записей по цене пользователя")]
	public async Task TryIfManualMarkClosesPositionWithoutExchangeRecords()
	{
		// Arrange: линейный перп без биржевых записей закрытия; покупка 0.01 по 42000
		// привязана к конструкции; пользователь ставит пометку с ценой 42100.
		var construction = await _constructionService.CreateAsync("Пердл BTC", 300m);
		await AddLinearTradesAsync(("exec-linear-buy", "Buy", "0.01", ExecMs(2023, 12, 28, 10, 0)));
		await _bindingService.BindAsync(construction.Id, "exec-linear-buy");
		var markedAt = new DateTimeOffset(2023, 12, 30, 10, 0, 0, TimeSpan.Zero);
		var mark = await _markService.AddAsync(construction.Id, LinearSymbol, markedAt, 42100m);

		// Act: читаем позиции.
		var readModel = new PositionReadModel(CreateOptions());
		var result = await readModel.ListAsync();

		// Assert: пометка влита в поток закрывающих записей — количество обнуляет
		// остаток на момент применения, цена пользователя сохранена; позиция закрыта
		// и участвует в результате наравне со сделками.
		// Требование: ручная пометка — fallback-закрытие для инструментов без биржевых записей.
		// Traceability: openspec:domain/constructions#scenario-manual-mark-delistings
		var position = result.Positions.Single();
		Assert.That(position.Residual, Is.EqualTo(0m));
		Assert.That(position.IsOpen, Is.False);
		var entry = result.ClosingEntries.Single();
		Assert.That(entry.Kind, Is.EqualTo(PositionClosingKind.ManualMark));
		Assert.That(entry.ClosedAt, Is.EqualTo(markedAt));
		Assert.That(entry.Quantity, Is.EqualTo(-0.01m));
		Assert.That(entry.Price, Is.EqualTo(42100m));
		Assert.That(entry.SourceKey, Is.EqualTo($"manual:{mark.Id}"));
		Assert.That(result.Warnings, Is.Empty);
	}

	[TestMethod]
	[Description("Ручная пометка без цены по умолчанию берёт последнюю известную марку")]
	public async Task TryIfManualMarkTakesLastMarkPriceByDefault()
	{
		// Arrange: позиция закрыта пометкой без цены; источник марок знает последнюю
		// марку инструмента.
		var construction = await _constructionService.CreateAsync("Пердл BTC", 300m);
		await AddLinearTradesAsync(("exec-linear-buy", "Buy", "0.01", ExecMs(2023, 12, 28, 10, 0)));
		await _bindingService.BindAsync(construction.Id, "exec-linear-buy");
		await _markService.AddAsync(
			construction.Id, LinearSymbol, new DateTimeOffset(2023, 12, 30, 10, 0, 0, TimeSpan.Zero));
		var markSource = Mock.Of<IInstrumentMarkSource>(source =>
			source.GetLastMarkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult<decimal?>(42123.45m));

		// Act: читаем позиции с источником марок.
		var readModel = new PositionReadModel(CreateOptions(), markSource);
		var result = await readModel.ListAsync();

		// Assert: цена пометки — последняя известная марка инструмента, подставленная
		// при чтении.
		// Требование: незаданная цена пометки по умолчанию — последняя марка.
		// Traceability: openspec:domain/constructions#scenario-manual-mark-default-last-mark
		var entry = result.ClosingEntries.Single();
		Assert.That(entry.Kind, Is.EqualTo(PositionClosingKind.ManualMark));
		Assert.That(entry.Price, Is.EqualTo(42123.45m));
	}

	[TestMethod]
	[Description("Цена пользователя побеждает последнюю марку")]
	public async Task TryIfManualMarkUserPriceWinsOverLastMark()
	{
		// Arrange: пометка с ценой пользователя; источник марок вернул бы другую цену.
		var construction = await _constructionService.CreateAsync("Пердл BTC", 300m);
		await AddLinearTradesAsync(("exec-linear-buy", "Buy", "0.01", ExecMs(2023, 12, 28, 10, 0)));
		await _bindingService.BindAsync(construction.Id, "exec-linear-buy");
		await _markService.AddAsync(
			construction.Id, LinearSymbol, new DateTimeOffset(2023, 12, 30, 10, 0, 0, TimeSpan.Zero), 41000m);
		var markSource = Mock.Of<IInstrumentMarkSource>(source =>
			source.GetLastMarkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult<decimal?>(99999m));

		// Act: читаем позиции с источником марок.
		var readModel = new PositionReadModel(CreateOptions(), markSource);
		var result = await readModel.ListAsync();

		// Assert: цена пометки — пользовательская, марка не подменяет её.
		// Требование: цена пометки задаётся пользователем.
		// Traceability: openspec:domain/constructions#requirement-position-close-lifecycle
		var entry = result.ClosingEntries.Single();
		Assert.That(entry.Price, Is.EqualTo(41000m));
	}

	[TestMethod]
	[Description("Количество пометки равно остатку на момент применения: сделка после пометки переоткрывает позицию")]
	public async Task TryIfManualMarkQuantityEqualsResidualAtApplicationTime()
	{
		// Arrange: покупка 0.0002, затем пометка, затем ещё покупка 0.0001 —
		// все в разное время.
		var construction = await _constructionService.CreateAsync("Пердл BTC", 300m);
		await AddLinearTradesAsync(
			("exec-buy-early", "Buy", "0.0002", ExecMs(2023, 12, 28, 10, 0)),
			("exec-buy-late", "Buy", "0.0001", ExecMs(2023, 12, 31, 10, 0)));
		await _bindingService.BindBatchAsync(construction.Id, new[] { "exec-buy-early", "exec-buy-late" });
		await _markService.AddAsync(
			construction.Id, LinearSymbol, new DateTimeOffset(2023, 12, 30, 10, 0, 0, TimeSpan.Zero), 42000m);

		// Act: читаем позиции.
		var readModel = new PositionReadModel(CreateOptions());
		var result = await readModel.ListAsync();

		// Assert: пометка закрыла остаток на свой момент (0.0002), а поздняя сделка
		// переоткрыла позицию остатком 0.0001 — хронология едина для сделок
		// и закрывающих записей.
		// Требование: количество пометки — остаток на момент применения по хронологии.
		// Traceability: change:add-core-domain/design#d3
		var entry = result.ClosingEntries.Single();
		Assert.That(entry.Quantity, Is.EqualTo(-0.0002m));
		var position = result.Positions.Single();
		Assert.That(position.Residual, Is.EqualTo(0.0001m));
		Assert.That(position.IsOpen, Is.True);
	}

	[TestMethod]
	[Description("Удаление пометки возвращает позицию в открытую с прежним остатком")]
	public async Task TryIfManualMarkRemovalReopensPosition()
	{
		// Arrange: позиция закрыта пометкой.
		var construction = await _constructionService.CreateAsync("Пердл BTC", 300m);
		await AddLinearTradesAsync(("exec-linear-buy", "Buy", "0.01", ExecMs(2023, 12, 28, 10, 0)));
		await _bindingService.BindAsync(construction.Id, "exec-linear-buy");
		var mark = await _markService.AddAsync(
			construction.Id, LinearSymbol, new DateTimeOffset(2023, 12, 30, 10, 0, 0, TimeSpan.Zero), 42100m);
		var readModel = new PositionReadModel(CreateOptions());
		var closed = await readModel.ListAsync();
		Assert.That(closed.Positions.Single().IsOpen, Is.False);

		// Act: удаляем пометку и читаем позиции снова.
		await _markService.DeleteAsync(mark.Id);
		var reopened = await readModel.ListAsync();

		// Assert: позиция снова открыта с прежним остатком — закрывающая запись
		// исчезла из потока, сделки никуда не делись.
		// Требование: удаление пометки возвращает позицию в открытое состояние.
		// Traceability: openspec:domain/constructions#scenario-mark-removal-reopens
		var position = reopened.Positions.Single();
		Assert.That(position.Residual, Is.EqualTo(0.01m));
		Assert.That(position.IsOpen, Is.True);
		Assert.That(reopened.ClosingEntries, Is.Empty);
		Assert.That(reopened.Warnings, Is.Empty);
	}

	[TestMethod]
	[Description("Пометка при уже нулевом остатке даёт предупреждение об избыточной записи")]
	public async Task TryIfManualMarkAtZeroResidualWarnsAsRedundant()
	{
		// Arrange: позиция уже закрыта встречными сделками, когда пользователь
		// ставит пометку.
		var construction = await _constructionService.CreateAsync("Скальп", 500m);
		await AddLinearTradesAsync(
			("exec-buy", "Buy", "0.01", ExecMs(2023, 12, 28, 10, 0)),
			("exec-sell", "Sell", "0.01", ExecMs(2023, 12, 28, 11, 0)));
		await _bindingService.BindBatchAsync(construction.Id, new[] { "exec-buy", "exec-sell" });
		var markedAt = new DateTimeOffset(2023, 12, 30, 10, 0, 0, TimeSpan.Zero);
		var mark = await _markService.AddAsync(construction.Id, LinearSymbol, markedAt, 42000m);

		// Act: читаем позиции.
		var readModel = new PositionReadModel(CreateOptions());
		var result = await readModel.ListAsync();

		// Assert: пометка не применяется и не переворачивает позицию — остаток
		// остаётся нулевым, а предупреждение называет избыточную запись.
		// Требование: избыточная закрывающая запись отражается предупреждением, не блокируя.
		// Traceability: change:add-core-domain/design#risks-trade-offs
		var position = result.Positions.Single();
		Assert.That(position.Residual, Is.EqualTo(0m));
		Assert.That(position.IsOpen, Is.False);
		Assert.That(result.ClosingEntries, Is.Empty);
		var warning = result.Warnings.Single();
		Assert.That(warning.ConstructionId, Is.EqualTo(construction.Id));
		Assert.That(warning.Symbol, Is.EqualTo(LinearSymbol));
		Assert.That(warning.Kind, Is.EqualTo(PositionClosingKind.ManualMark));
		Assert.That(warning.ClosedAt, Is.EqualTo(markedAt));
		Assert.That(warning.SourceKey, Is.EqualTo($"manual:{mark.Id}"));
	}

	[TestMethod]
	[Description("Поздно пришедшая delivery, конкурирующая с ручной пометкой, даёт предупреждение")]
	public async Task TryIfLateDeliveryCompetingWithManualMarkWarnsAsRedundant()
	{
		// Arrange: покупка опциона 0.0001 по 100; ручная пометка закрывает позицию
		// 29DEC23 07:00, а аккаунтовая delivery приходит на 08:00 того же дня.
		var construction = await _constructionService.CreateAsync("Колл BTC", 700m);
		await AddOptionTradeAsync("exec-call-buy", "Buy", "0.0001", "100");
		await _bindingService.BindAsync(construction.Id, "exec-call-buy");
		var markedAt = new DateTimeOffset(2023, 12, 29, 7, 0, 0, TimeSpan.Zero);
		await _markService.AddAsync(construction.Id, CallSymbol, markedAt, 50m);
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.RawDeliveries.Add(Delivery(deliveryPrice: "46000", strike: "45000", fee: "0", deliveryRpl: "0.09"));
			await db.SaveChangesAsync();
		}

		// Act: читаем позиции.
		var readModel = new PositionReadModel(CreateOptions());
		var result = await readModel.ListAsync();

		// Assert: обе записи претендовали на обнуление остатка — первая по хронологии
		// (пометка) закрыла позицию, вторая (delivery) стала избыточной: предупреждение
		// называет её, позиция не переворачивается.
		// Требование: конкуренция пометки с delivery отражается предупреждением, не блокирует.
		// Traceability: change:add-core-domain/design#risks-trade-offs
		// Traceability: adr:docs/adr/0002-option-expiry-closing-entries.md#option-expiry-closing-entries
		var position = result.Positions.Single();
		Assert.That(position.Residual, Is.EqualTo(0m));
		Assert.That(position.IsOpen, Is.False);
		var entry = result.ClosingEntries.Single();
		Assert.That(entry.Kind, Is.EqualTo(PositionClosingKind.ManualMark));
		Assert.That(entry.Quantity, Is.EqualTo(-0.0001m));
		Assert.That(entry.Price, Is.EqualTo(50m));
		var warning = result.Warnings.Single();
		Assert.That(warning.Kind, Is.EqualTo(PositionClosingKind.Delivery));
		Assert.That(warning.ClosedAt, Is.EqualTo(OptionDelivery));
		Assert.That(warning.SourceKey, Is.EqualTo($"{CallSymbol}|{OptionDeliveryMs}"));
	}

	[TestMethod]
	[Description("Чтение позиций неизвестной конструкции отказывает")]
	[ExpectedException(typeof(ConstructionNotFoundException))]
	public async Task ThrowOnUnknownConstruction()
	{
		// Arrange — Act: срез позиций несуществующей конструкции.
		var readModel = new PositionReadModel(CreateOptions());
		await readModel.ListAsync(999);
	}

	[TestMethod]
	[Description("Null-опции контекста отклоняются конструктором")]
	[ExpectedException(typeof(ArgumentNullException))]
	public void ThrowOnNullOptions()
	{
		// Arrange — Act — Assert
		new PositionReadModel(null!);
	}

	#region Помощники

	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Справочник инструментов: опцион BTC с delivery 29DEC23 08:00 UTC и линейный перп BTCUSDT.</summary>
	private static void SeedInstrumentCatalog(JournalDbContext db)
	{
		var optionPayload =
			$$"""{"symbol":"{{CallSymbol}}","baseCoin":"BTC","quoteCoin":"USD","settleCoin":"USDC","status":"Trading","optionsType":"Call","deliveryTime":"{{OptionDeliveryMs}}","deliveryFeeRate":"0.00015"}""";
		var linearPayload =
			"""{"symbol":"BTCUSDT","contractType":"LinearPerpetual","status":"Trading","baseCoin":"BTC","quoteCoin":"USDT","settleCoin":"USDT","deliveryTime":"0","optionsType":""}""";
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = CallSymbol,
			Category = "option",
			PayloadJson = optionPayload,
			FetchedAt = FetchedAt,
		});
		db.RawInstruments.Add(new RawInstrument
		{
			Symbol = LinearSymbol,
			Category = "linear",
			PayloadJson = linearPayload,
			FetchedAt = FetchedAt,
		});
		db.SaveChanges();
	}

	/// <summary>Добавляет сырые записи исполнения линейного перпа BTCUSDT.</summary>
	private async Task AddLinearTradesAsync(params (string ExecId, string Side, string ExecQty, long ExecTimeMs)[] executions)
	{
		using var db = new JournalDbContext(CreateOptions());
		foreach (var (execId, side, execQty, execTimeMs) in executions)
		{
			db.RawExecutions.Add(Raw(
				execId, "linear", LinearSymbol, execTimeMs,
				ExecutionPayload(execId, LinearSymbol, side, "42000", execQty, "0.0001", "BTC", execTimeMs)));
		}

		await db.SaveChangesAsync();
	}

	/// <summary>Добавляет сырую запись исполнения опциона BTC-29DEC23-45000-C.</summary>
	private async Task AddOptionTradeAsync(string execId, string side, string execQty, string execPrice)
	{
		var execTimeMs = ExecMs(2023, 12, 28, 10, 0);
		using var db = new JournalDbContext(CreateOptions());
		db.RawExecutions.Add(Raw(
			execId, "option", CallSymbol, execTimeMs,
			ExecutionPayload(execId, CallSymbol, side, execPrice, execQty, "0", "USDC", execTimeMs)));
		await db.SaveChangesAsync();
	}

	private static RawExecution Raw(string execId, string category, string symbol, long execTimeMs, string payloadJson) => new()
	{
		ExecId = execId,
		Category = category,
		Symbol = symbol,
		ExecTimeMs = execTimeMs,
		PayloadJson = payloadJson,
		FetchedAt = FetchedAt,
	};

	/// <summary>Запись исполнения в форме ответа execution-list: числа биржа шлёт строками.</summary>
	private static string ExecutionPayload(
		string execId,
		string symbol,
		string side,
		string execPrice,
		string execQty,
		string execFee,
		string? feeCurrency,
		long execTimeMs,
		bool isMaker = false)
	{
		var feeCurrencyJson = feeCurrency is null ? "null" : $"\"{feeCurrency}\"";
		var isMakerJson = isMaker ? "true" : "false";
		return $$"""{"symbol":"{{symbol}}","orderId":"order-{{execId}}","orderLinkId":"","side":"{{side}}","execFee":"{{execFee}}","execId":"{{execId}}","execPrice":"{{execPrice}}","execQty":"{{execQty}}","execType":"Trade","execTime":"{{execTimeMs}}","feeCurrency":{{feeCurrencyJson}},"isMaker":{{isMakerJson}}}""";
	}

	/// <summary>Delivery-запись колла в форме ответа delivery-record: числа биржа шлёт строками.</summary>
	private static RawDelivery Delivery(string deliveryPrice, string strike, string fee, string deliveryRpl)
	{
		var payload =
			$$"""{"symbol":"{{CallSymbol}}","side":"Buy","deliveryTime":"{{OptionDeliveryMs}}","entryPrice":"100","deliveryPrice":"{{deliveryPrice}}","strike":"{{strike}}","fee":"{{fee}}","position":"0.0001","deliveryRpl":"{{deliveryRpl}}"}""";
		return new RawDelivery
		{
			Symbol = CallSymbol,
			DeliveryTimeMs = OptionDeliveryMs,
			Category = "option",
			PayloadJson = payload,
			FetchedAt = FetchedAt,
		};
	}

	private static long ExecMs(int year, int month, int day, int hour, int minute) =>
		new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

	#endregion
}
