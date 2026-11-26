using System.Text;
using DocOrganizer.DataLayer.Data;
using DocOrganizer.DataLayer.DataAccess.Ado;
using DocOrganizer.DataLayer.DataAccess.Ef;
using DocOrganizer.DataLayer.Demo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Кирилиця в консолі Windows. Пропускаємо, якщо потоки перенаправлено (наприклад, під час dotnet ef).
if (!Console.IsOutputRedirected)
    Console.OutputEncoding = Encoding.UTF8;
if (!Console.IsInputRedirected)
    Console.InputEncoding = Encoding.UTF8;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets<Program>(optional: true);
builder.Logging.SetMinimumLevel(LogLevel.Warning);   // SQL показуємо через LogTo/SqlLog, а не через логер хоста

var connectionString = DocumentsDbContextSetup.GetConnectionString(builder.Configuration);

// У консольному застосунку життєвий цикл контексту контролюємо явно — через фабрику.
builder.Services.AddDbContextFactory<DocumentsDbContext>(options => DocumentsDbContextSetup.Configure(options, connectionString));

builder.Services.AddSingleton(sp => new AdoDocumentRepository(
    connectionString, sp.GetRequiredService<ILogger<AdoDocumentRepository>>()));
builder.Services.AddSingleton<EfDocumentService>();
builder.Services.AddSingleton<EfDocumentQueries>();
builder.Services.AddSingleton<DemoRunner>();

using var host = builder.Build();

// Ctrl+C → скасування поточної операції через CancellationToken.
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var factory = host.Services.GetRequiredService<IDbContextFactory<DocumentsDbContext>>();
await using (var db = await factory.CreateDbContextAsync(cts.Token))
{
    // Застосовує ще не застосовані міграції та викликає UseAsyncSeeding (ідемпотентно).
    await db.Database.MigrateAsync(cts.Token);
}

await host.Services.GetRequiredService<DemoRunner>().RunAsync(cts.Token);