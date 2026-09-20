using Microsoft.EntityFrameworkCore;
using TransactionJournal.Bybit;
using TransactionJournal.Components;
using TransactionJournal.Data;

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

