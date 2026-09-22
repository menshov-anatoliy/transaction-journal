using Microsoft.EntityFrameworkCore;
using TransactionJournal;
using TransactionJournal.Analytics;
using TransactionJournal.Bybit;
using TransactionJournal.Components;
using TransactionJournal.Components.Layout;
using TransactionJournal.Components.Pages;
using TransactionJournal.Data;
using TransactionJournal.Domain;
using TransactionJournal.Materialization;
using TransactionJournal.Sync;

// Локальный .env загружается до создания хоста и любых регистраций: значения файла
// попадают в переменные процесса раньше первого чтения конфигурации потребителями
// (сейчас — поставщиком учётных данных Bybit), отсутствующий файл не является ошибкой.
// Traceability: openspec:config/env-file#requirement-env-file-loaded-on-startup
AppEnvFile.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
	.AddInteractiveServerComponents();

// База журнала — SQLite в режиме WAL: единственное хранилище, параллельные
// читатели не блокируют пишущее веб-приложение (ADR-0003).
// Папка App_Data не пересекается с исходной папкой Data даже на файловых
// системах без учёта регистра.
var dataDirectory = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
Directory.CreateDirectory(dataDirectory);
var databasePath = Path.Combine(dataDirectory, "journal.db");
var connectionString = builder.Configuration.GetConnectionString("Journal")
	?? $"Data Source={databasePath}";
builder.Services.AddDbContext<JournalDbContext>(options => options.UseSqlite(connectionString));

// Поставщик ключа Bybit: dev-реализация из переменных окружения;
// постоянное место хранения секрета определит тикет #4.
builder.Services.AddSingleton<IBybitCredentialsProvider, EnvironmentBybitCredentialsProvider>();

// Read-модель блока подключения «Настроек»: забирает учётные данные у поставщика
// вместо компонента и отдаёт экрану только маску ключа — секрет не доходит до
// состояния Blazor и не может быть показан или введён через интерфейс.
// Traceability: openspec:ui/screens#scenario-settings-secret-never-displayed
builder.Services.AddSingleton<SettingsReadModel>();

// Read-модель журнала синхронизаций «Настроек»: строки запусков SyncRun новыми
// сверху — время, режим, результат и статус; предупреждения сверки дополняет экран.
// Модель живёт singleton-ом над собственными опциями контекста: каждый вызов создаёт
// короткоживущий контекст и не тянет scoped-сервисы в singleton.
// Traceability: openspec:ui/screens#scenario-settings-sync-log-mode-warnings
builder.Services.AddSingleton(sp => new SyncJournalReadModel(
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options));
builder.Services.AddSingleton<ISyncJournalReadModel>(sp => sp.GetRequiredService<SyncJournalReadModel>());

// Подсистема синхронизации с Bybit: подписанный read-only клиент с resilience,
// шлюз истории и публичных спецификаций, движки категорий, пополнитель справочника
// и оркестратор кнопки «Синхронизировать» на общей строке SyncRun.
var bybitBaseUrl = builder.Configuration["Bybit:BaseUrl"];
builder.Services.AddSingleton(new BybitClientOptions
{
	BaseUrl = string.IsNullOrWhiteSpace(bybitBaseUrl) ? BybitClientOptions.DefaultBaseUrl : bybitBaseUrl,
});
builder.Services.AddHttpClient<BybitApiClient>();
builder.Services.AddHttpClient<BybitTickersClient>();
builder.Services.AddTransient<BybitHistoryGateway>();
builder.Services.AddTransient<IBybitHistoryGateway>(sp => sp.GetRequiredService<BybitHistoryGateway>());
builder.Services.AddTransient<IBybitInstrumentSource>(sp => sp.GetRequiredService<BybitHistoryGateway>());
// Список базовых активов опционной доски строится над тем же шлюзом спецификаций;
// конфигурируемые дополнения (делистнутые доски) подключит задача 6.1 при чтении
// Sync:ExtraOptionBaseCoins из конфигурации.
// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
builder.Services.AddTransient<IOptionBaseCoinSource>(sp => new OptionBaseCoinSource(
	sp.GetRequiredService<IBybitInstrumentSource>()));
builder.Services.AddTransient<ExecutionWindowPass>();
builder.Services.AddTransient<DeliveryWindowPass>();
builder.Services.AddTransient<ExecutionCategorySync>();
builder.Services.AddTransient<DeliveryCategorySync>();
builder.Services.AddTransient<InstrumentReferenceSync>();

// Адаптер сырого хранилища живёт singleton-ом над собственными опциями контекста:
// каждый вызов создаёт короткоживущий контекст, поэтому длительная сессия Blazor Server
// не держит соединений между пачками записей.
builder.Services.AddSingleton(sp => new JournalSyncStore(
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options));
builder.Services.AddSingleton<IExecutionKnownIdProbe>(sp => sp.GetRequiredService<JournalSyncStore>());
builder.Services.AddSingleton<IExecutionSyncStateStore>(sp => sp.GetRequiredService<JournalSyncStore>());
builder.Services.AddSingleton<IRawExecutionBatchWriter>(sp => sp.GetRequiredService<JournalSyncStore>());
builder.Services.AddSingleton<IDeliveryKnownKeyProbe>(sp => sp.GetRequiredService<JournalSyncStore>());
builder.Services.AddSingleton<IRawDeliveryBatchWriter>(sp => sp.GetRequiredService<JournalSyncStore>());
builder.Services.AddSingleton<IInstrumentReferenceStore>(sp => sp.GetRequiredService<JournalSyncStore>());
builder.Services.AddSingleton<IJournalRawSnapshotStore>(sp => sp.GetRequiredService<JournalSyncStore>());
builder.Services.AddSingleton<ISyncRunJournal>(sp => sp.GetRequiredService<JournalSyncStore>());
builder.Services.AddSingleton<JournalMaterializer>();
builder.Services.AddTransient<IJournalSyncService, JournalSyncService>();

// Команда «Переразобрать сырые записи заново» на «Настройках»: полная пересборка
// доменных представлений из локального сырья без сетевых запросов; экран зависит
// от интерфейса, тесты экрана подменяют её заглушкой.
// Traceability: openspec:ui/screens#scenario-settings-reparse-confirmation
builder.Services.AddSingleton<IJournalReparseService, JournalReparseService>();

// Слой доменных операций: use-case сервисы над контекстом журнала; каждый вызов
// создаёт короткоживущий контекст, поэтому длительные сессии Blazor Server
// не держат соединений между операциями. Позиции и результаты — производные,
// мутирующий API ограничен пользовательскими записями домена.
builder.Services.AddSingleton(sp => new ConstructionService(
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options));
// Экран деталей выполняет действия конструкции по контракту сервиса: тонкий
// слой UI зависит от интерфейса, тесты экрана подменяют его заглушкой.
builder.Services.AddSingleton<IConstructionService>(sp => sp.GetRequiredService<ConstructionService>());
// Комментарии сделок, позиций и конструкции правятся по месту своих экранов:
// экран деталей зависит от интерфейса, тесты экрана подменяют его заглушкой.
builder.Services.AddSingleton(sp => new CommentService(
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options));
builder.Services.AddSingleton<ICommentService>(sp => sp.GetRequiredService<CommentService>());
builder.Services.AddSingleton(sp => new TradeBindingService(
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options));
// Перенос сделки в другую конструкцию и возврат во «Входящие» выполняются
// use-case сервисом привязки: экран деталей зависит от интерфейса, тесты
// экрана подменяют его заглушкой.
builder.Services.AddSingleton<ITradeBindingService>(sp => sp.GetRequiredService<TradeBindingService>());
builder.Services.AddSingleton(sp => new InboxReadModel(
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options));
builder.Services.AddSingleton(sp => new ManualCloseMarkService(
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options));
// Ручная пометка закрытия ставится из строки позиции и правится в закрывающих
// записях: экран деталей зависит от интерфейса, тесты экрана подменяют заглушкой.
// Traceability: openspec:ui/screens#requirement-manual-close-mark-from-position
builder.Services.AddSingleton<IManualCloseMarkService>(sp => sp.GetRequiredService<ManualCloseMarkService>());
// Внешние корректировки PnL добавляются формой и правятся строкой таблицы в
// деталях конструкции: экран зависит от интерфейса, тесты подменяют заглушкой.
// Traceability: openspec:ui/screens#requirement-adjustments-in-detail
builder.Services.AddSingleton(sp => new PnLAdjustmentService(
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options));
builder.Services.AddSingleton<IPnLAdjustmentService>(sp => sp.GetRequiredService<PnLAdjustmentService>());

// Read-модель каркаса «Терминала»: счётчик непривязанных сделок для бейджа
// «Входящих» и заголовок открытой конструкции для транзитной вкладки — тонкая
// композиция готовых проекций домена без собственных правил.
builder.Services.AddSingleton(sp => new FrameReadModel(
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options,
	sp.GetRequiredService<InboxReadModel>()));
builder.Services.AddSingleton<IFrameReadModel>(sp => sp.GetRequiredService<FrameReadModel>());

// Сигнал изменений журнала в границах circuit: экраны оповещают его после мутаций
// домена, каркас перечитывает панель — итог, бейдж «Входящих» и транзитную вкладку —
// без навигации.
builder.Services.AddScoped<JournalChangeSignal>();

// Провайдер марок аналитики: публичные тикеры без аутентификации и кэш последней
// известной марки со временем получения. Read-модель позиций получает его как
// источник дефолта цены ручных пометок закрытия без цены.
builder.Services.AddSingleton(sp => new InstrumentMarkProvider(
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options,
	sp.GetRequiredService<BybitTickersClient>()));
builder.Services.AddSingleton<IInstrumentMarkSource>(sp => sp.GetRequiredService<InstrumentMarkProvider>());

// Оценка нереализованного PnL открытых остатков: свежие марки берутся у провайдера
// тем же запросом, что и марка позиции, а недоступность тикеров деградирует только
// в null нереализованной части с отметкой времени марок — реализованные метрики
// читаются как есть.
builder.Services.AddSingleton<IFreshInstrumentMarkSource>(sp => sp.GetRequiredService<InstrumentMarkProvider>());
builder.Services.AddSingleton<UnrealizedPnlMarkEvaluator>();

// Read-модель позиций — производный запрос остатков без мутирующего API; источник
// последних марок подставляет цену ручным пометкам, заданным без цены пользователя.
builder.Services.AddSingleton(sp => new PositionReadModel(
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options,
	sp.GetRequiredService<IInstrumentMarkSource>()));

// Композиция метрик журнала для экранов UI: метрики позиций и итоги конструкций
// вычисляются из текущих данных при каждом чтении (контракт «аналитика при чтении»),
// нереализованная часть оценивается свежими марками того же провайдера, а постоянный
// итог панели «Терминала» читается отсюда же.
builder.Services.AddSingleton(sp => new JournalMetricsReadModel(
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options,
	sp.GetRequiredService<IFreshInstrumentMarkSource>(),
	sp.GetRequiredService<IInstrumentMarkSource>()));
builder.Services.AddSingleton<IJournalMetricsReadModel>(sp => sp.GetRequiredService<JournalMetricsReadModel>());

// Read-модель экрана «Конструкции»: соединяет метрики аналитики журнала с именами
// и ручными статусами конструкций, скрывая архивные из списка и его счётчика.
builder.Services.AddSingleton(sp => new ConstructionListReadModel(
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options,
	sp.GetRequiredService<IJournalMetricsReadModel>()));
builder.Services.AddSingleton<IConstructionListReadModel>(sp => sp.GetRequiredService<ConstructionListReadModel>());

// Read-модель экрана деталей конструкции: сводка метрик с периодом и отметкой
// марок, комментарий и таблицы позиций, сделок, закрывающих записей и корректировок —
// тонкое соединение метрик аналитики, потока закрывающих записей позиций и хранилища.
builder.Services.AddSingleton(sp => new ConstructionDetailReadModel(
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options,
	sp.GetRequiredService<IJournalMetricsReadModel>(),
	sp.GetRequiredService<PositionReadModel>()));
builder.Services.AddSingleton<IConstructionDetailReadModel>(sp => sp.GetRequiredService<ConstructionDetailReadModel>());

var app = builder.Build();

// Журнал разворачивается сам: применяем миграции и включаем WAL-режим SQLite.
using (var scope = app.Services.CreateScope())
{
	var database = scope.ServiceProvider.GetRequiredService<JournalDbContext>();
	database.Database.Migrate();
	database.Database.ExecuteSqlRaw("PRAGMA journal_mode = WAL;");
}

if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error", createScopeForErrors: true);
	app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
	.AddInteractiveServerRenderMode();

app.Run();

