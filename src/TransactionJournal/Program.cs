using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using TransactionJournal;
using TransactionJournal.Application;
using TransactionJournal.Application.Analytics;
using TransactionJournal.Application.Bybit;
using TransactionJournal.Application.Materialization;
using TransactionJournal.Application.Ops;
using TransactionJournal.Application.Sync;
using TransactionJournal.Infrastructure.Analytics;
using TransactionJournal.Infrastructure.Bybit;
using TransactionJournal.Infrastructure.Sync;
using TransactionJournal.Infrastructure.Data;
using TransactionJournal.Domain.Data;
using TransactionJournal.Domain.Sync;
using TransactionJournal.Components;
using TransactionJournal.Components.Layout;
using TransactionJournal.Components.Pages;
using TransactionJournal.Infrastructure.Ops;
using TransactionJournal.Infrastructure.Hints;
using TransactionJournal.Hints;
using TransactionJournal.Hints.Corpus;
using TransactionJournal.Hints.Display;
using TransactionJournal.Hints.Ports;
using TransactionJournal.Infrastructure.ReadModels;
using TransactionJournal.Infrastructure.UseCases;
using TransactionJournal.Infrastructure.Consultations;
using TransactionJournal.Consultations;
using TransactionJournal.Consultations.Ports;
using TransactionJournal.Domain;

var builder = WebApplication.CreateBuilder(args);

// Локальный файл секретов подключается последним провайдером конфигурации и потому
// перекрывает appsettings.json, переменные окружения и аргументы командной строки;
// файл необязательный — его отсутствие штатно, а потребитель секрета сообщит о
// незастроенном значении только в момент обращения к нему.
// Traceability: openspec:config/local-secrets#requirement-local-secrets-single-file
builder.Configuration.AddJsonFile(
	BybitCredentialsConfig.LocalFileName, optional: true, reloadOnChange: false);

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

// Поставщик ключа Bybit: читает ключи из конфигурации, куда они попадают из
// локального файла секретов appsettings.Local.json.
// Traceability: openspec:config/local-secrets#requirement-local-secrets-single-file
builder.Services.AddSingleton<IBybitCredentialsProvider, ConfigurationBybitCredentialsProvider>();

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
// Опции движков синхронизации собираются из конфигурации при старте: глубина backfill
// Sync:MaxBackfillDepthDays (дефолт 730 дней) и дополнительные базовые активы опционной
// доски Sync:ExtraOptionBaseCoins (делистнутые доски с торговой историей); без секции
// Sync в конфигурации приложение работает на дефолтах кода.
// Traceability: openspec:sync/bybit-history#requirement-backfill-full-history
// Traceability: openspec:sync/bybit-history#scenario-delisted-base-coin-from-config
var syncEngineOptions = SyncOptionsReader.Read(builder.Configuration);
builder.Services.AddSingleton(syncEngineOptions.Execution);
builder.Services.AddSingleton(syncEngineOptions.Delivery);
// Список базовых активов опционной доски строится над тем же шлюзом спецификаций,
// сырьевым хранилищем и конфигурационными дополнениями: безфильтровое перечисление
// справочника покрывает только доску по умолчанию, сырьё называет остальные доски.
// Traceability: openspec:sync/bybit-history#requirement-option-base-coin-coverage
builder.Services.AddSingleton<IOptionRawBaseCoinReader>(sp => sp.GetRequiredService<JournalSyncStore>());
builder.Services.AddTransient<IOptionBaseCoinSource>(sp => new OptionBaseCoinSource(
	sp.GetRequiredService<IBybitInstrumentSource>(),
	sp.GetRequiredService<IOptionRawBaseCoinReader>(),
	syncEngineOptions.ExtraOptionBaseCoins));
builder.Services.AddTransient<ExecutionWindowPass>();
builder.Services.AddTransient<DeliveryWindowPass>();
builder.Services.AddTransient<ExecutionCategorySync>();
builder.Services.AddTransient<DeliveryCategorySync>();
builder.Services.AddTransient<InstrumentReferenceSync>();

// Подсистема резервных копий журнала: консистентный снапшот работающей базы через
// VACUUM INTO в настроенный каталог с ротацией по количеству копий. Опции читаются
// из конфигурации при старте: Backup:Directory (дефолт App_Data/backups) и
// Backup:RetentionLimit (дефолт 15); относительный каталог разрешается от корня
// содержимого. Сервис живёт singleton-ом над собственными опциями контекста:
// каждый вызов создаёт короткоживущий контекст.
// Traceability: openspec:ops/db-backup#requirement-backup-consistent-snapshot
// Traceability: openspec:ops/db-backup#requirement-backup-retention
var configuredBackupOptions = BackupOptionsReader.Read(builder.Configuration);
builder.Services.AddSingleton(new JournalBackupOptions
{
	Directory = Path.IsPathRooted(configuredBackupOptions.Directory)
		? configuredBackupOptions.Directory
		: Path.Combine(builder.Environment.ContentRootPath, configuredBackupOptions.Directory),
	RetentionLimit = configuredBackupOptions.RetentionLimit,
});
builder.Services.AddSingleton(sp => new JournalBackupService(
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options,
	sp.GetRequiredService<JournalBackupOptions>()));
builder.Services.AddSingleton<IJournalBackupService>(sp => sp.GetRequiredService<JournalBackupService>());

// Хранилище политики опционального копирования: переключатель «копия перед синхронизацией»
// живёт в таблице AppSetting базы журнала и переживает перезапуск; отсутствие строки
// читается как «включён». Стор — singleton над собственными опциями контекста: каждый
// вызов создаёт короткоживущий контекст.
// Traceability: openspec:ops/db-backup#requirement-backup-optional-operations
builder.Services.AddSingleton(sp => new BackupPolicyStore(
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options));
builder.Services.AddSingleton<IBackupPolicyStore>(sp => sp.GetRequiredService<BackupPolicyStore>());

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
// Команда «Синхронизировать» идёт через защитный декоратор: при включённой политике
// копия базы с причиной «sync» создаётся до запуска, неудача копии блокирует запуск
// до записи строки SyncRun, выключенная политика пускает внутренний сервис напрямую.
// Экраны зависят от интерфейса IJournalSyncService и тестируются заглушками без изменений.
// Traceability: openspec:ops/db-backup#requirement-backup-optional-operations
builder.Services.AddTransient<JournalSyncService>();
builder.Services.AddTransient<IJournalSyncService>(sp => new BackupGuardedSyncService(
	sp.GetRequiredService<IJournalBackupService>(),
	sp.GetRequiredService<IBackupPolicyStore>(),
	sp.GetRequiredService<JournalSyncService>()));

// Сервис переразбора сырых записей: полная пересборка доменных представлений из
// локального сырья без сетевых запросов. Команда снята с экрана «Настройки»,
// сервис оставлен зарегистрированным в DI.
// Traceability: openspec:ui/screens#scenario-settings-reparse-confirmation
builder.Services.AddSingleton<IJournalReparseService, JournalReparseService>();

// Команда «Собрать конструкции» на «Настройках»: полный пересбор конструкций
// и привязок из локального сырья одной транзакцией; пересбор стартует только после
// обязательной резервной копии, неудача копии блокирует операцию. Экран зависит
// от интерфейса, тесты экрана подменяют команду заглушкой.
// Traceability: change:add-construction-auto-assembly/specs/domain/construction-assembly/spec#requirement-full-rebuild-semantics
// Traceability: openspec:ops/db-backup#requirement-backup-mandatory-before-rebuild
builder.Services.AddSingleton(sp => new ConstructionAssemblyService(
	sp.GetRequiredService<IJournalRawSnapshotStore>(),
	sp.GetRequiredService<IJournalBackupService>(),
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options));
builder.Services.AddSingleton<IConstructionAssemblyService>(sp => sp.GetRequiredService<ConstructionAssemblyService>());

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

// Окружение агента подсказок: порты объявлены в проекте Hints, адаптеры живут
// здесь и в Infrastructure (направление «адаптер → порт»); домен о подсказках
// не знает (ADR-0007). Хранилище подсказок и читатель снапшота — singleton над
// собственными опциями контекста: каждый вызов создаёт короткоживущий контекст.
// Traceability: openspec:architecture/solution-structure#requirement-dependencies-point-inward
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IHintStore>(sp => new HintStore(
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options));
builder.Services.AddSingleton<IJournalSnapshotReader>(sp => new JournalSnapshotReader(
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options,
	sp.GetRequiredService<IJournalMetricsReadModel>()));
builder.Services.AddSingleton<IMarkSource>(sp => new HintsMarkSource(
	sp.GetRequiredService<IFreshInstrumentMarkSource>()));
// Каталог корпуса правил: дефолт rules/ рядом с exe, путь переопределяется
// настройкой Hints:RulesPath; правки карточек действуют со следующего прохода.
// Traceability: openspec:hints/rules-corpus#requirement-corpus-snapshot-per-pass
builder.Services.AddSingleton(_ => new RulesCorpusLoader(
	builder.Configuration["Hints:RulesPath"]
	?? Path.Combine(AppContext.BaseDirectory, "rules")));
builder.Services.AddSingleton<HintAgentPass>();
builder.Services.AddSingleton<IHintPassRunner>(sp => sp.GetRequiredService<HintAgentPass>());
// Read-модель отображения подсказок: панель субъекта, индикаторы списка и
// общий журнал группируются при чтении поверх хранилища подсказок; кнопки
// «Применено»/«Отклонено» и первый показ идут через неё же.
// Traceability: openspec:ui/screens#requirement-ui-hint-section-groups
builder.Services.AddSingleton<IHintDisplayReadModel, HintDisplayReadModel>();

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

// Консультации: порты объявлены в проекте Consultations, адаптеры живут здесь
// и в Infrastructure (направление «адаптер → порт»); снимок контекста собирается
// кодом поверх read-моделей журнала, живые подсказки движка в него не входят.
// Traceability: openspec:architecture/solution-structure#requirement-dependencies-point-inward
// Traceability: openspec:consultations/context#requirement-context-deterministic-snapshot
builder.Services.AddSingleton<IRuleCorpusReader, RulesCorpusConsultationAdapter>();
builder.Services.AddSingleton<IConsultationContextReader, ConsultationContextReader>();
// Инструкции агента консультаций: дефолт consultation-prompt.md рядом с
// rules/, путь переопределяется настройкой Consultations:InstructionsPath;
// валидный файл переопределяет встроенный дефолт, отсутствие или битость
// файла чат не ломает — правки владельца действуют со следующего сообщения.
// Traceability: openspec:consultations/context#requirement-context-agent-instructions-file
builder.Services.AddSingleton(_ => new ConsultationInstructions(
	builder.Configuration["Consultations:InstructionsPath"]
	?? Path.Combine(AppContext.BaseDirectory, ConsultationInstructions.DefaultFileName)));

// Модель чата консультаций: секция Consultations:ChatModel задаёт провайдера,
// модель и OpenAI-совместимый эндпоинт (дефолт — z.ai GLM-5.3); смена
// провайдера или модели выполняется правкой конфигурации без правки кода,
// без секции чат работает на дефолтах кода.
// Traceability: openspec:consultations/tools#requirement-tools-chat-model-configurable
var chatModelOptions = ConsultationChatModelOptions.Resolve(
	builder.Configuration["Consultations:ChatModel:Provider"],
	builder.Configuration["Consultations:ChatModel:Model"],
	builder.Configuration["Consultations:ChatModel:BaseUrl"],
	builder.Configuration["Consultations:ChatModel:ApiKey"] ?? builder.Configuration["Llm:ApiKey"]);
// Клиент строится лениво при первом обращении к агентному циклу: незастроенный
// ключ доступа не мешает остальному журналу работать.
builder.Services.AddKeyedChatClient(ConsultationAgent.ChatClientServiceKey, _ => CreateConsultationChatClient(chatModelOptions));
// Агентный цикл: инструменты реестра и keyed клиент соединяются здесь;
// рыночные данные агента читаются из Bybit через адаптер Infrastructure, а при
// сбое биржи адаптер деградирует в кэш марок провайдера с явным as-of — чат
// продолжается по журналу и корпусу.
// Traceability: openspec:consultations/tools#requirement-tools-single-request-per-call
// Traceability: openspec:consultations/tools#requirement-tools-degradation-cached-asof
builder.Services.AddSingleton<IConsultationMarketReader>(sp => new BybitConsultationMarketReader(
	sp.GetRequiredService<BybitTickersClient>(),
	new DbContextOptionsBuilder<JournalDbContext>().UseSqlite(connectionString).Options));
builder.Services.AddSingleton<ConsultationTools>();
builder.Services.AddSingleton<ConsultationAgent>();

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

// Адаптер OpenAI-совместимого провайдера модели чата: ключ обязателен — без
// него запрос к модели невозможен, ошибка сообщается в момент первого вопроса.
static IChatClient CreateConsultationChatClient(ConsultationChatModelOptions options)
{
	if (string.IsNullOrWhiteSpace(options.ApiKey))
	{
		throw new InvalidOperationException(
			"Ключ модели чата консультаций не задан: заполните Consultations:ChatModel:ApiKey " +
			"(или запасной Llm:ApiKey) в appsettings.Local.json.");
	}

	return new OpenAIClient(new ApiKeyCredential(options.ApiKey), new OpenAIClientOptions { Endpoint = new Uri(options.BaseUrl) })
		.GetChatClient(options.Model)
		.AsIChatClient();
}

