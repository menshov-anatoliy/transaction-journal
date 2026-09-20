using Microsoft.EntityFrameworkCore;
using TransactionJournal.Bybit;
using TransactionJournal.Components;
using TransactionJournal.Data;
using TransactionJournal.Materialization;
using TransactionJournal.Sync;

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

// Подсистема синхронизации с Bybit: подписанный read-only клиент с resilience,
// шлюз истории и публичных спецификаций, движки категорий, пополнитель справочника
// и оркестратор кнопки «Синхронизировать» на общей строке SyncRun.
var bybitBaseUrl = builder.Configuration["Bybit:BaseUrl"];
builder.Services.AddSingleton(new BybitClientOptions
{
	BaseUrl = string.IsNullOrWhiteSpace(bybitBaseUrl) ? BybitClientOptions.DefaultBaseUrl : bybitBaseUrl,
});
builder.Services.AddHttpClient<BybitApiClient>();
builder.Services.AddTransient<BybitHistoryGateway>();
builder.Services.AddTransient<IBybitHistoryGateway>(sp => sp.GetRequiredService<BybitHistoryGateway>());
builder.Services.AddTransient<IBybitInstrumentSource>(sp => sp.GetRequiredService<BybitHistoryGateway>());
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

