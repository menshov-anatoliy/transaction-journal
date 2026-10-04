using Bunit;
using AngleSharp.Dom;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NUnit.Framework;
using TransactionJournal.Bybit;
using TransactionJournal.Components;
using TransactionJournal.Components.Layout;
using TransactionJournal.Components.Pages;
using TransactionJournal.Data;
using TransactionJournal.Domain.ConstructionAssembly;
using TransactionJournal.Domain.Data;
using TransactionJournal.Materialization;
using TransactionJournal.Ops;
using TransactionJournal.Sync;
using SettingsPage = TransactionJournal.Components.Pages.Settings;
using Assert = NUnit.Framework.Assert;
using Description = Microsoft.VisualStudio.TestTools.UnitTesting.DescriptionAttribute;

namespace TransactionJournal.Tests.Ui;

/// <summary>
/// Проверки экрана «Настройки»: API-ключ показан маскированно, секрет не отображается
/// нигде и не проходит в состояние компонента, экран указывает на локальный файл
/// секретов appsettings.Local.json как место хранения секрета; без настроенного
/// ключа показывается явное состояние
/// вместо маски; кнопка «Синхронизировать сейчас» пополняет журнал синхронизаций
/// строкой со временем, режимом, результатом и статусом, предупреждения сверки видны
/// и работу не блокируют; «Собрать конструкции» требует подтверждения с
/// перечнем безвозвратных потерь и сообщением об автоматической резервной копии
/// базы; «Собрать из „Входящих”» запускается сразу
/// без подтверждения; обе команды сборки показывают счётчики итога пересбора;
/// переключатель копирования перед синхронизацией включён по умолчанию и
/// сохраняет выключенное состояние в базу журнала.
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
	private CountingAssemblyService _assembly = null!;

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

		// Хранилище политики копирования настоящее — над временной базой:
		// переключатель читается и пишется в таблицу AppSetting, как в работе.
		_context.Services.AddSingleton<IBackupPolicyStore>(new BackupPolicyStore(_options));

		// Команда сборки конструкций подменяется считающей заглушкой: проверки
		// следят, что без подтверждения сборка не вызывалась вовсе.
		_assembly = new CountingAssemblyService();
		_context.Services.AddSingleton<IConstructionAssemblyService>(_assembly);

		// Сигнал изменений журнала: экран оповещает каркас после команд сборки;
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
	[Description("Ключ показан маскированно, секрет не отображается нигде, указан локальный файл секретов")]
	public void TryIfSecretIsNeverDisplayed()
	{
		// Act: пользователь открывает «Настройки» с настроенным ключом.
		var cut = _context.RenderComponent<SettingsPage>();

		// Assert: ключ виден только маской «первые четыре ······ последние четыре»,
		// полный ключ и секрет в разметке отсутствуют, а местом хранения секрета
		// назван локальный файл секретов appsettings.Local.json.
		// Требование: секрет не отображается на экране настроек.
		// Traceability: openspec:ui/screens#scenario-settings-secret-never-displayed
		Assert.That(cut.Markup, Does.Contain("abcd······wxyz"));
		Assert.That(cut.Markup, Does.Not.Contain(ApiKey));
		Assert.That(cut.Markup, Does.Not.Contain(ApiSecret));
		Assert.That(cut.Markup, Does.Contain(ConfigurationBybitCredentialsProvider.LocalFileName));
		Assert.That(cut.Markup, Does.Contain($"секция {ConfigurationBybitCredentialsProvider.SectionName}"));
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
	[Description("Без настроенного ключа экран показывает явное состояние с указанием файла и ключей")]
	public void TryIfUnconfiguredKeyShowsExplicitState()
	{
		// Arrange: локальный файл секретов не задаёт пару ключ/секрет —
		// поставщик отказывает.
		_context.Services.AddSingleton<IBybitCredentialsProvider>(
			new ThrowingCredentials());

		// Act: пользователь открывает «Настройки».
		var cut = _context.RenderComponent<SettingsPage>();

		// Assert: вместо маски — явное сообщение с именами файла и конфигурационных
		// ключей; никаких значений ключа и секрета на экране нет.
		// Traceability: openspec:ui/screens#scenario-settings-secret-never-displayed
		Assert.That(cut.Markup, Does.Contain("Ключ не настроен"));
		Assert.That(cut.Markup, Does.Contain(ConfigurationBybitCredentialsProvider.ApiKeyConfigKey));
		Assert.That(cut.Markup, Does.Contain(ConfigurationBybitCredentialsProvider.ApiSecretConfigKey));
		Assert.That(cut.Markup, Does.Contain(ConfigurationBybitCredentialsProvider.LocalFileName));
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
			// Даты хранятся в UTC и рендерятся локальным временем.
			// Traceability: openspec:ui/screens#scenario-sync-dates-shown-local
			Assert.That(cut.Markup, Does.Contain(DisplayTime.FormatMoment(Now)));
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
		var startedAt = new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero);
		using (var db = new JournalDbContext(_options))
		{
			db.SyncRuns.Add(new SyncRun
			{
				StartedAt = startedAt,
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
		Assert.That(cut.Markup, Does.Contain(DisplayTime.FormatMoment(startedAt)));
		Assert.That(cut.Markup, Does.Contain("инкрементальная догрузка"));
		Assert.That(cut.Markup, Does.Contain("прерван: retCode=10006 превышение частоты запросов"));
		Assert.That(cut.Markup, Does.Contain("ошибка"));
	}

	[TestMethod]
	[Description("Переключатель копирования перед синхронизацией включён по умолчанию")]
	public void TryIfSyncBackupToggleEnabledByDefault()
	{
		// Act: пользователь открывает «Настройки» на чистой базе — строки политики нет.
		var cut = _context.RenderComponent<SettingsPage>();

		// Assert: флажок копирования перед синхронизацией показан включённым —
		// отсутствие строки политики хранилище трактует как «включён», дефолт
		// записи не требует.
		// Требование: переключатель резервного копирования перед синхронизацией
		// включён по умолчанию.
		// Traceability: openspec:ui/screens#requirement-settings-screen
		cut.WaitForAssertion(() => Assert.That(
			cut.Find("input[type=checkbox]").HasAttribute("checked"), Is.True));
	}

	[TestMethod]
	[Description("Выключенный переключатель сохраняет политику без копии и запускает синхронизацию")]
	public void TryIfSyncBackupToggleOffPersistsPolicyAndRunsSync()
	{
		// Arrange: команда синхронизации закрывает строку журнала, как настоящий
		// сервис; ветвь «без копии» при выключенной политике покрывают тесты
		// декоратора — здесь проверяется проводка переключателя до политики.
		_context.Services.AddSingleton<IJournalSyncService>(
			new JournalWritingSyncService(_options, Now));
		var store = _context.Services.GetRequiredService<IBackupPolicyStore>();
		var cut = _context.RenderComponent<SettingsPage>();

		// Act: пользователь выключает переключатель — выбор уходит в базу, —
		// затем запускает синхронизацию.
		cut.Find("input[type=checkbox]").Change(false);
		cut.WaitForAssertion(() => Assert.That(
			store.IsBackupBeforeSyncEnabledAsync().GetAwaiter().GetResult(), Is.False));
		cut.FindAll("button").Single(button => button.TextContent.Contains("Синхронизировать")).Click();

		// Assert: политика сохранена выключенной, синхронизация запускается —
		// в журнале появляется строка запуска.
		// Требование: выключенный переключатель запускает синхронизацию без копии.
		// Traceability: openspec:ui/screens#scenario-settings-sync-backup-toggle
		cut.WaitForAssertion(() => Assert.That(cut.Markup, Does.Contain("успех")));
		Assert.That(store.IsBackupBeforeSyncEnabledAsync().GetAwaiter().GetResult(), Is.False);
	}

	[TestMethod]
	[Description("Даты строк журнала, хранимые в UTC, рендерятся локальным временем")]
	public void TryIfJournalDatesRenderedInLocalTime()
	{
		// Arrange: в журнале запуск, начатый в 23:00 UTC 2025-12-31 — при смещении
		// зоны +03:00 локальное время уже 2026-01-01.
		var startedAt = new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero);
		using (var db = new JournalDbContext(_options))
		{
			db.SyncRuns.Add(new SyncRun
			{
				StartedAt = startedAt,
				FinishedAt = startedAt.AddMinutes(1),
				Mode = SyncRunMode.Incremental,
				Status = SyncRunStatus.Succeeded,
			});
			db.SaveChanges();
		}

		// Act: пользователь открывает «Настройки».
		var cut = _context.RenderComponent<SettingsPage>();

		// Assert: время строки показано локальной зоной; при ненулевом смещении
		// UTC-стеночная часть в разметке отсутствует — хранимое значение не меняется.
		// Требование: даты из синхронизации показываются в локальном времени.
		// Traceability: openspec:ui/screens#scenario-sync-dates-shown-local
		Assert.That(cut.Markup, Does.Contain(DisplayTime.FormatMoment(startedAt)));
		if (TimeZoneInfo.Local.GetUtcOffset(startedAt) != TimeSpan.Zero)
		{
			Assert.That(cut.Markup, Does.Not.Contain(
				startedAt.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
		}
	}

	[TestMethod]
	[Description("Сборка конструкций требует явного подтверждения: без него команда не запускается, отмена закрывает вопрос")]
	// Сценарий: подтверждение показывает перечень безвозвратных потерь, отмена
	// ничего не запускает — данные остаются нетронутыми.
	// Traceability: change:refine-construction-assembly/specs/ui/screens/spec#scenario-assembly-requires-confirmation
	public void TryIfAssemblyRequiresExplicitConfirmation()
	{
		// Act: пользователь открывает «Настройки» и нажимает команду сборки.
		var cut = _context.RenderComponent<SettingsPage>();
		FindButton(cut, "Собрать конструкции").Click();

		// Assert: появился явный вопрос подтверждения с перечнем безвозвратных
		// потерь и выходом «Отмена»; сама команда не запускалась — сервис не вызывался.
		Assert.That(cut.Markup, Does.Contain("Безвозвратно удаляются"));
		Assert.That(cut.Markup, Does.Contain("все конструкции, привязки сделок"));
		Assert.That(cut.Markup, Does.Contain("внешние корректировки PnL и ручные пометки закрытия"));
		Assert.That(FindButton(cut, "Собрать").TextContent, Is.EqualTo("Собрать"));
		Assert.That(FindButton(cut, "Отмена").TextContent, Is.EqualTo("Отмена"));
		Assert.That(_assembly.CallCount, Is.EqualTo(0));

		// Act: пользователь отменяет опасное действие.
		FindButton(cut, "Отмена").Click();

		// Assert: вопрос закрыт, сборка так и не запускалась — без подтверждения
		// команда не начинает работу.
		Assert.That(cut.Markup, Does.Not.Contain("Безвозвратно удаляются"));
		Assert.That(_assembly.CallCount, Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Подтверждение пересбора сообщает об автоматической резервной копии базы перед пересбором")]
	// Сценарий: рядом с перечнем безвозвратных потерь диалог сообщает об
	// автоматической резервной копии — защита создаётся сама, без ручного шага.
	// Traceability: openspec:ui/screens#scenario-assembly-confirmation-mentions-backup
	public void TryIfAssemblyConfirmationMentionsBackup()
	{
		// Act: пользователь открывает «Настройки» и нажимает команду пересбора.
		var cut = _context.RenderComponent<SettingsPage>();
		FindButton(cut, "Собрать конструкции").Click();

		// Assert: подтверждение перечисляет потери и сообщает об автоматической
		// резервной копии базы журнала; сама команда не запускалась.
		Assert.That(cut.Markup, Does.Contain("Безвозвратно удаляются"));
		Assert.That(cut.Markup, Does.Contain("Перед пересбором автоматически создаётся резервная копия базы журнала"));
		Assert.That(_assembly.CallCount, Is.EqualTo(0));
	}

	[TestMethod]
	[Description("Сборка из «Входящих» запускается сразу, без диалога подтверждения; пересбор при этом не стартует")]
	// Сценарий: инкрементная сборка безопасна — обрабатывает только
	// нераспределённые записи и не требует подтверждения опасного действия.
	// Traceability: change:refine-construction-assembly/specs/ui/screens/spec#scenario-inbox-assembly-without-confirmation
	public void TryIfInboxAssemblyStartsWithoutConfirmation()
	{
		// Act: пользователь открывает «Настройки» и сразу нажимает команду
		// сборки из «Входящих» — никакого промежуточного вопроса нет.
		var cut = _context.RenderComponent<SettingsPage>();
		FindButton(cut, "Собрать из „Входящих”").Click();

		// Assert: инкрементная сборка выполнена ровно один раз без какого-либо
		// подтверждения; полный пересбор не запускался вовсе.
		cut.WaitForAssertion(() =>
		{
			Assert.That(_assembly.InboxCallCount, Is.EqualTo(1));
			Assert.That(_assembly.CallCount, Is.EqualTo(0));
			Assert.That(cut.Markup, Does.Not.Contain("Безвозвратно удаляются"));
		});
	}

	[TestMethod]
	[Description("Подтверждённая сборка запускается один раз и показывает счётчики итога пересбора")]
	// Сценарий: после завершения пересбора пользователь видит счётчики созданных
	// конструкций, привязанных сделок и сделок во «Входящих».
	// Traceability: change:refine-construction-assembly/specs/ui/screens/spec#scenario-assembly-shows-result
	public void TryIfConfirmedAssemblyRunsOnceAndShowsCounters()
	{
		// Arrange: команда вернёт итог контрольной истории: десять конструкций,
		// 1761 привязанная сделка и две сделки во «Входящих».
		_assembly.Result = new ConstructionRebuildResult
		{
			ConstructionsCount = 10,
			BoundCount = 1761,
			TradesInInbox = 2,
		};
		var cut = _context.RenderComponent<SettingsPage>();
		FindButton(cut, "Собрать конструкции").Click();

		// Act: пользователь явно подтверждает опасное действие.
		FindButton(cut, "Собрать").Click();

		// Assert: команда выполнена ровно один раз, счётчики итога показаны под
		// командой, вопрос подтверждения закрыт.
		cut.WaitForAssertion(() =>
		{
			Assert.That(_assembly.CallCount, Is.EqualTo(1));
			Assert.That(cut.Markup, Does.Contain("Сборка завершена"));
			Assert.That(cut.Markup, Does.Contain("конструкций 10"));
			Assert.That(cut.Markup, Does.Contain("привязано сделок 1761"));
			Assert.That(cut.Markup, Does.Contain("во «Входящих» 2"));
			Assert.That(cut.Markup, Does.Not.Contain("Безвозвратно удаляются"));
		});
	}

	[TestMethod]
	[Description("Сборка из «Входящих» показывает счётчики итога: создано, привязано, осталось")]
	// Сценарий: после завершения любой из команд сборки — и инкрементной, и
	// полного пересбора — пользователь видит одни и те же счётчики итога.
	// Traceability: change:refine-construction-assembly/specs/ui/screens/spec#scenario-assembly-shows-result
	public void TryIfInboxAssemblyShowsCounters()
	{
		// Arrange: сборка из «Входящих» вернёт счётчики инкремента: одна новая
		// конструкция, три привязанные сделки, одна осталась во «Входящих».
		_assembly.Result = new ConstructionRebuildResult
		{
			ConstructionsCount = 1,
			BoundCount = 3,
			TradesInInbox = 1,
		};
		var cut = _context.RenderComponent<SettingsPage>();

		// Act: пользователь запускает сборку из «Входящих» без подтверждения.
		FindButton(cut, "Собрать из „Входящих”").Click();

		// Assert: счётчики итога показаны под командой.
		cut.WaitForAssertion(() =>
		{
			Assert.That(_assembly.InboxCallCount, Is.EqualTo(1));
			Assert.That(cut.Markup, Does.Contain("Сборка завершена"));
			Assert.That(cut.Markup, Does.Contain("конструкций 1"));
			Assert.That(cut.Markup, Does.Contain("привязано сделок 3"));
			Assert.That(cut.Markup, Does.Contain("во «Входящих» 1"));
		});
	}

	/// <summary>Находит кнопку по её тексту: на «Настройках» несколько команд в разных блоках.</summary>
	private static IElement FindButton(IRenderedFragment cut, string text) =>
		cut.FindAll("button").Single(button => button.TextContent.Trim() == text);

	/// <summary>Заглушка команды сборки конструкций: считает вызовы и возвращает подготовленный итог.</summary>
	private sealed class CountingAssemblyService : IConstructionAssemblyService
	{
		/// <summary>Число запусков команды: проверки следят, что без подтверждения запуска нет.</summary>
		public int CallCount { get; private set; }

		/// <summary>Итог, возвращаемый при запуске; не задан — нулевые счётчики.</summary>
		public ConstructionRebuildResult? Result { get; set; }

		public Task<ConstructionRebuildResult> RebuildAsync(CancellationToken cancellationToken = default)
		{
			CallCount++;
			return Task.FromResult(Result ?? new ConstructionRebuildResult
			{
				ConstructionsCount = 0,
				BoundCount = 0,
				TradesInInbox = 0,
			});
		}

		/// <summary>Число запусков сборки из «Входящих»; подробно проверяется задачей UI-команды.</summary>
		public int InboxCallCount { get; private set; }

		public Task<ConstructionRebuildResult> AssembleInboxAsync(CancellationToken cancellationToken = default)
		{
			InboxCallCount++;
			return Task.FromResult(Result ?? new ConstructionRebuildResult
			{
				ConstructionsCount = 0,
				BoundCount = 0,
				TradesInInbox = 0,
			});
		}
	}

	/// <summary>Подменяет поставщика парой известных проверке значений ключа и секрета.</summary>
	private sealed class StubCredentials(string apiKey, string apiSecret) : IBybitCredentialsProvider
	{
		public BybitCredentials GetCredentials() => new(apiKey, apiSecret);
	}

	/// <summary>Подменяет отказ незастроенного локального файла секретов.</summary>
	private sealed class ThrowingCredentials : IBybitCredentialsProvider
	{
		public BybitCredentials GetCredentials() =>
			throw new InvalidOperationException(
				$"Не задан API-ключ Bybit: заполните {ConfigurationBybitCredentialsProvider.ApiKeyConfigKey} "
				+ $"в файле {ConfigurationBybitCredentialsProvider.LocalFileName}.");
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
