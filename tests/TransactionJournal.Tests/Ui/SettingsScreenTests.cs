using Bunit;
using AngleSharp.Dom;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Bybit;
using TransactionJournal.Components.Layout;
using TransactionJournal.Components.Pages;
using TransactionJournal.Data;
using TransactionJournal.Materialization;
using TransactionJournal.Sync;
using SettingsPage = TransactionJournal.Components.Pages.Settings;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ui;

/// <summary>
/// Проверки экрана «Настройки»: API-ключ показан маскированно, секрет не отображается
/// нигде и не проходит в состояние компонента, экран указывает на переменные окружения
/// как место хранения секрета; без настроенного ключа показывается явное состояние
/// вместо маски; кнопка «Синхронизировать сейчас» пополняет журнал синхронизаций
/// строкой со временем, режимом, результатом и статусом, предупреждения сверки видны
/// и работу не блокируют; «Переразобрать сырые записи заново» запускается только
/// после явного подтверждения.
/// Traceability: openspec:ui/screens#requirement-settings-screen
/// </summary>
[TestClass]
public class SettingsScreenTests
{
	private const string ApiKey = "abcdEFGH1234wxyz";
	private const string ApiSecret = "top-secret-value-42";

	// Виртуальные часы фиксированы на 2026-01-01: время строки журнала детерминировано.
	private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	private Bunit.TestContext _context = null!;
	private string _databasePath = null!;
	private DbContextOptions<JournalDbContext> _options = null!;
	private CountingReparseService _reparse = null!;

	[TestInitialize]
	public void Initialize()
	{
		_context = new Bunit.TestContext();

		// Каждая проверка работает со своей пустой базой журнала: журнал синхронизаций
		// читается read-моделью из хранилища, как в работе.
		_databasePath = Path.Combine(Path.GetTempPath(), $"settings-screen-tests-{Guid.NewGuid():N}.db");
		using (var db = new JournalDbContext(CreateOptions()))
		{
			db.Database.Migrate();
		}

		_options = CreateOptions();

		// Поставщик учётных данных подменяется заглушкой: секрет известен проверке,
		// и она ищет его в разметке — настоящее чтение переменных окружения здесь
		// не участвует и глобальное состояние тестов не трогает.
		_context.Services.AddSingleton<IBybitCredentialsProvider>(
			new StubCredentials(ApiKey, ApiSecret));
		_context.Services.AddSingleton<SettingsReadModel>();

		// Read-модель журнала синхронизаций настоящая — над временной базой;
		// команда синхронизации по умолчанию не вызывается проверками секрета.
		_context.Services.AddSingleton(_options);
		_context.Services.AddSingleton<ISyncJournalReadModel, SyncJournalReadModel>();
		_context.Services.AddSingleton<IJournalSyncService>(new UnusedSyncService());

		// Команда переразбора подменяется считающей заглушкой: проверки следят,
		// что без подтверждения она не вызывалась вовсе.
		_reparse = new CountingReparseService();
		_context.Services.AddSingleton<IJournalReparseService>(_reparse);

		// Сигнал изменений журнала: экран оповещает каркас после переразбора;
		// без подписчиков в изолированном рендере он безопасно бездействует.
		_context.Services.AddScoped<JournalChangeSignal>();
	}

	[TestCleanup]
	public void Cleanup()
	{
		_context.Dispose();

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
	[Description("Ключ показан маскированно, секрет не отображается нигде, указаны переменные окружения")]
	public void TryIfSecretIsNeverDisplayed()
	{
		// Act: пользователь открывает «Настройки» с настроенным ключом.
		var cut = _context.RenderComponent<SettingsPage>();

		// Assert: ключ виден только маской «первые четыре ······ последние четыре»,
		// полный ключ и секрет в разметке отсутствуют, а местом хранения секрета
		// названы переменные окружения.
		// Требование: секрет не отображается на экране настроек.
		// Traceability: openspec:ui/screens#scenario-settings-secret-never-displayed
		Assert.That(cut.Markup, Does.Contain("abcd······wxyz"));
		Assert.That(cut.Markup, Does.Not.Contain(ApiKey));
		Assert.That(cut.Markup, Does.Not.Contain(ApiSecret));
		Assert.That(cut.Markup, Does.Contain(EnvironmentBybitCredentialsProvider.ApiKeyVariableName));
		Assert.That(cut.Markup, Does.Contain(EnvironmentBybitCredentialsProvider.ApiSecretVariableName));
	}

	[TestMethod]
	[Description("Короткий ключ маскируется целиком и не выдаёт ни одного знака")]
	public void TryIfShortKeyIsMaskedEntirely()
	{
		// Arrange: ключ короче восьми знаков — маскировать по краям нечего.
		_context.Services.AddSingleton<IBybitCredentialsProvider>(
			new StubCredentials("abc", ApiSecret));

		// Act: пользователь открывает «Настройки».
		var cut = _context.RenderComponent<SettingsPage>();

		// Assert: вместо маски по краям — сплошное сокрытие, знаков ключа нет.
		Assert.That(cut.Markup, Does.Contain("······"));
		Assert.That(cut.Markup, Does.Not.Contain("abc"));
		Assert.That(cut.Markup, Does.Not.Contain(ApiSecret));
	}

	[TestMethod]
	[Description("Без настроенного ключа экран показывает явное состояние с именами переменных окружения")]
	public void TryIfUnconfiguredKeyShowsExplicitState()
	{
		// Arrange: переменные окружения не заданы — поставщик отказывает.
		_context.Services.AddSingleton<IBybitCredentialsProvider>(
			new ThrowingCredentials());

		// Act: пользователь открывает «Настройки».
		var cut = _context.RenderComponent<SettingsPage>();

		// Assert: вместо маски — явное сообщение с именами переменных окружения;
		// никаких значений ключа и секрета на экране нет.
		Assert.That(cut.Markup, Does.Contain("Ключ не настроен"));
		Assert.That(cut.Markup, Does.Contain(EnvironmentBybitCredentialsProvider.ApiKeyVariableName));
		Assert.That(cut.Markup, Does.Contain(EnvironmentBybitCredentialsProvider.ApiSecretVariableName));
		Assert.That(cut.Markup, Does.Not.Contain("······"));
		Assert.That(cut.Markup, Does.Not.Contain(ApiSecret));
	}

	[TestMethod]
	[Description("Журнал синхронизаций показывает строку запуска со временем, режимом, результатом и статусом; предупреждения сверки видны и работу не блокируют")]
	public void TryIfSyncLogShowsModeResultStatusAndWarnings()
	{
		// Arrange: команда синхронизации закрывает строку запуска в базе — как
		// настоящий сервис — и возвращает итог с предупреждением сверки экспирации.
		_context.Services.AddSingleton<IJournalSyncService>(
			new JournalWritingSyncService(_options, Now));

		// Act: пользователь открывает «Настройки» и запускает синхронизацию.
		var cut = _context.RenderComponent<SettingsPage>();

		// Assert (исходное состояние): журнал пуст и говорит об этом явно.
		Assert.That(cut.Markup, Does.Contain("Синхронизаций ещё не было"));

		cut.Find("button").Click();

		// Assert: в журнале появилась строка завершившегося запуска — время, режим,
		// выбранный системой, результат (счётчики новых записей) и статус; предупреждение
		// сверки показано в журнале, кнопка снова доступна — работу это не блокирует.
		// Требование: журнал синхронизаций показывает режим и предупреждения.
		// Traceability: openspec:ui/screens#scenario-settings-sync-log-mode-warnings
		cut.WaitForAssertion(() =>
		{
			Assert.That(cut.Markup, Does.Contain("2026-01-01 00:00"));
			Assert.That(cut.Markup, Does.Contain("первичная загрузка (backfill)"));
			Assert.That(cut.Markup, Does.Contain("исполнений 2 · delivery 1 · инструментов 1"));
			Assert.That(cut.Markup, Does.Contain("успех"));
			Assert.That(cut.Markup, Does.Contain("BTC-29DEC23-45000-C"));
			Assert.That(cut.Markup, Does.Contain("расхождение -0.07"));
			Assert.That(cut.Find("button").TextContent, Does.Contain("Синхронизировать сейчас"));
		});
	}

	[TestMethod]
	[Description("Прерванный запуск виден строкой журнала с инкрементальным режимом, причиной и статусом «ошибка»")]
	public void TryIfFailedRunShowsErrorResultAndStatusInJournal()
	{
		// Arrange: в журнале уже есть прерванный запуск — режим система выбрала
		// инкрементальный, причиной прерывания стал отказ биржи.
		using (var db = new JournalDbContext(_options))
		{
			db.SyncRuns.Add(new SyncRun
			{
				StartedAt = new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero),
				FinishedAt = new DateTimeOffset(2025, 12, 31, 23, 5, 0, TimeSpan.Zero),
				Mode = SyncRunMode.Incremental,
				Status = SyncRunStatus.Failed,
				Error = "retCode=10006 превышение частоты запросов",
			});
			db.SaveChanges();
		}

		// Act: пользователь открывает «Настройки».
		var cut = _context.RenderComponent<SettingsPage>();

		// Assert: строка журнала показывает время, инкрементальный режим, причину
		// прерывания как результат и статус «ошибка» — прерванный запуск не исчезает.
		// Требование: журнал показывает время, режим, результат и статус каждой строки.
		// Traceability: openspec:ui/screens#scenario-settings-sync-log-mode-warnings
		Assert.That(cut.Markup, Does.Contain("2025-12-31 23:00"));
		Assert.That(cut.Markup, Does.Contain("инкрементальная догрузка"));
		Assert.That(cut.Markup, Does.Contain("прерван: retCode=10006 превышение частоты запросов"));
		Assert.That(cut.Markup, Does.Contain("ошибка"));
	}

	[TestMethod]
	[Description("Переразбор требует явного подтверждения: без него команда не запускается, отмена закрывает вопрос")]
	public void TryIfReparseRequiresExplicitConfirmation()
	{
		// Act: пользователь открывает «Настройки» и нажимает команду переразбора.
		var cut = _context.RenderComponent<SettingsPage>();
		FindButton(cut, "Переразобрать сырые записи заново").Click();

		// Assert: появился явный вопрос подтверждения с объяснением последствий
		// и выходом «Отмена»; сама команда не запускалась — сервис не вызывался.
		// Требование: переразбор требует подтверждения опасного действия.
		// Traceability: openspec:ui/screens#scenario-settings-reparse-confirmation
		Assert.That(cut.Markup, Does.Contain("Доменные представления будут перестроены"));
		Assert.That(FindButton(cut, "Переразобрать").TextContent, Is.EqualTo("Переразобрать"));
		Assert.That(FindButton(cut, "Отмена").TextContent, Is.EqualTo("Отмена"));
		Assert.That(_reparse.CallCount, Is.EqualTo(0));

		// Act: пользователь отменяет опасное действие.
		FindButton(cut, "Отмена").Click();

		// Assert: вопрос закрыт, переразбор так и не запускался — без
		// подтверждения команда не начинает работу.
		Assert.That(cut.Markup, Does.Not.Contain("Доменные представления будут перестроены"));
		Assert.That(_reparse.CallCount, Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Подтверждённый переразбор запускается один раз и показывает итог со счётчиками проекции")]
	public void TryIfConfirmedReparseRunsOnceAndReportsOutcome()
	{
		// Arrange: команда вернёт итог с двумя сделками и одним предупреждением сверки.
		_reparse.Result = CreateReparseResult();
		var cut = _context.RenderComponent<SettingsPage>();
		FindButton(cut, "Переразобрать сырые записи заново").Click();

		// Act: пользователь явно подтверждает опасное действие.
		FindButton(cut, "Переразобрать").Click();

		// Assert: команда выполнена ровно один раз, итог со счётчиками доменных
		// представлений и предупреждениями сверки показан под командой, вопрос
		// подтверждения закрыт.
		cut.WaitForAssertion(() =>
		{
			Assert.That(_reparse.CallCount, Is.EqualTo(1));
			Assert.That(cut.Markup, Does.Contain("Переразбор завершён"));
			Assert.That(cut.Markup, Does.Contain("сделок во «Входящих» 2"));
			Assert.That(cut.Markup, Does.Contain("закрывающих записей 0"));
			Assert.That(cut.Markup, Does.Contain("предупреждений сверки 1"));
			Assert.That(cut.Markup, Does.Not.Contain("Доменные представления будут перестроены"));
		});
	}

	/// <summary>Находит кнопку по её тексту: на «Настройках» несколько команд в разных блоках.</summary>
	private static IElement FindButton(IRenderedFragment cut, string text) =>
		cut.FindAll("button").Single(button => button.TextContent.Trim() == text);

	/// <summary>Итог переразбора: две сделки «Входящих» и одно предупреждение сверки.</summary>
	private static JournalMaterializationResult CreateReparseResult() => new()
	{
		InboxTrades =
		[
			new MaterializedTrade
			{
				ExecId = "exec-opt-buy",
				Category = "option",
				Symbol = "BTC-29DEC23-45000-C",
				ExecutedAt = new DateTimeOffset(2023, 12, 28, 10, 0, 0, TimeSpan.Zero),
				Quantity = 0.0001m,
				Price = 45000m,
				Fee = 0.01m,
				FeeCurrency = "USDT",
				IsMaker = true,
			},
			new MaterializedTrade
			{
				ExecId = "exec-linear-sell",
				Category = "linear",
				Symbol = "BTCUSDT",
				ExecutedAt = new DateTimeOffset(2023, 12, 28, 10, 30, 0, TimeSpan.Zero),
				Quantity = -0.01m,
				Price = 42000m,
				Fee = -0.0001m,
				FeeCurrency = "BTC",
				IsMaker = false,
			},
		],
		ExpiryClosingEntries = [],
		ReconciliationWarnings =
		[
			new ExpiryReconciliationWarning
			{
				Symbol = "BTC-29DEC23-45000-C",
				DeliveryTime = new DateTimeOffset(2023, 12, 29, 8, 0, 0, TimeSpan.Zero),
				DeliveryRpl = 0.2m,
				OwnResult = 0.27m,
				Difference = -0.07m,
				SourceKey = "BTC-29DEC23-45000-C|1703846400000",
			},
		],
	};

	/// <summary>Заглушка команды переразбора: считает вызовы и возвращает подготовленный итог.</summary>
	private sealed class CountingReparseService : IJournalReparseService
	{
		/// <summary>Число запусков команды: проверки следят, что без подтверждения запуска нет.</summary>
		public int CallCount { get; private set; }

		/// <summary>Итог, возвращаемый при запуске; не задан — пустая проекция.</summary>
		public JournalMaterializationResult? Result { get; set; }

		public Task<JournalMaterializationResult> ReparseAsync(CancellationToken cancellationToken = default)
		{
			CallCount++;
			return Task.FromResult(Result ?? new JournalMaterializationResult
			{
				InboxTrades = [],
				ExpiryClosingEntries = [],
				ReconciliationWarnings = [],
			});
		}
	}

	/// <summary>Подменяет поставщика парой известных проверке значений ключа и секрета.</summary>
	private sealed class StubCredentials(string apiKey, string apiSecret) : IBybitCredentialsProvider
	{
		public BybitCredentials GetCredentials() => new(apiKey, apiSecret);
	}

	/// <summary>Подменяет отказ не настроенных переменных окружения.</summary>
	private sealed class ThrowingCredentials : IBybitCredentialsProvider
	{
		public BybitCredentials GetCredentials() =>
			throw new InvalidOperationException(
				$"Не задана переменная окружения {EnvironmentBybitCredentialsProvider.ApiKeyVariableName}.");
	}

	/// <summary>Заглушка команды синхронизации: проверки блока подключения кнопку не нажимают.</summary>
	private sealed class UnusedSyncService : IJournalSyncService
	{
		public Task<JournalSyncResult> SyncAsync(CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("Синхронизация не ожидается в этой проверке.");
	}

	/// <summary>
	/// Заглушка команды синхронизации: закрывает строку запуска в базе, как настоящий
	/// сервис, и возвращает итог со счётчиками и предупреждением сверки экспирации.
	/// </summary>
	private sealed class JournalWritingSyncService(
		DbContextOptions<JournalDbContext> options,
		DateTimeOffset startedAt) : IJournalSyncService
	{
		public async Task<JournalSyncResult> SyncAsync(CancellationToken cancellationToken = default)
		{
			await using var db = new JournalDbContext(options);
			db.SyncRuns.Add(new SyncRun
			{
				StartedAt = startedAt,
				FinishedAt = startedAt.AddMinutes(5),
				Mode = SyncRunMode.Backfill,
				Status = SyncRunStatus.Succeeded,
				NewExecutions = 2,
				NewDeliveries = 1,
				NewInstruments = 1,
			});
			await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

			return CreateCompletedResult(startedAt);
		}
	}

	/// <summary>Создаёт опции контекста журнала над временной SQLite-базой проверки.</summary>
	private DbContextOptions<JournalDbContext> CreateOptions() =>
		new DbContextOptionsBuilder<JournalDbContext>()
			.UseSqlite($"Data Source={_databasePath}")
			.Options;

	/// <summary>Завершённый успешный запуск: счётчики новых записей и одно предупреждение сверки.</summary>
	private static JournalSyncResult CreateCompletedResult(DateTimeOffset startedAt) => new()
	{
		Mode = SyncRunMode.Backfill,
		Run = new SyncRun
		{
			StartedAt = startedAt,
			FinishedAt = startedAt.AddMinutes(5),
			Mode = SyncRunMode.Backfill,
			Status = SyncRunStatus.Succeeded,
			NewExecutions = 2,
			NewDeliveries = 1,
			NewInstruments = 1,
		},
		Executions = new Dictionary<string, ExecutionCategorySyncResult>(StringComparer.Ordinal),
		Deliveries = new Dictionary<string, DeliveryCategorySyncResult>(StringComparer.Ordinal),
		Projection = new JournalMaterializationResult
		{
			InboxTrades = [],
			ExpiryClosingEntries = [],
			ReconciliationWarnings =
			[
				new ExpiryReconciliationWarning
				{
					Symbol = "BTC-29DEC23-45000-C",
					DeliveryTime = new DateTimeOffset(2023, 12, 29, 8, 0, 0, TimeSpan.Zero),
					DeliveryRpl = 0.2m,
					OwnResult = 0.27m,
					Difference = -0.07m,
					SourceKey = "BTC-29DEC23-45000-C|1703846400000",
				},
			],
		},
	};
}
