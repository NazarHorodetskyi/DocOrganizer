using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DocOrganizer.DataLayer.Data;

/// <summary>
/// Єдине місце налаштування DbContext — використовується і застосунком (DI),
/// і інструментом dotnet ef (DesignTimeDbContextFactory).
/// </summary>
public static class DocumentsDbContextSetup
{
    public static void Configure(DbContextOptionsBuilder options, string connectionString)
    {
        options
            .UseSqlServer(connectionString)
            // Логуємо тільки виконані SQL-команди; показ вмикається/вимикається через SqlLog.Enabled.
            .LogTo(SqlLog.Write, new[] { DbLoggerCategory.Database.Command.Name }, LogLevel.Information)
            .EnableDetailedErrors()
            // Тестові дані: синхронний і асинхронний варіанти з однаковою логікою.
            .UseSeeding((context, _) => SeedData.Seed(context))
            .UseAsyncSeeding((context, _, ct) => SeedData.SeedAsync(context, ct));
    }

    public static string GetConnectionString(IConfiguration configuration)
        => configuration.GetConnectionString("DocumentsDb")
           ?? throw new InvalidOperationException("Connection string DocumentsDb is missing.");
}

/// <summary>
/// Фабрика для dotnet ef (migrations add, database update, migrations script).
/// Читає appsettings.json + user secrets, тому пароль не потрапляє в код.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DocumentsDbContext>
{
    public DocumentsDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddUserSecrets<DesignTimeDbContextFactory>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var builder = new DbContextOptionsBuilder<DocumentsDbContext>();
        DocumentsDbContextSetup.Configure(builder, DocumentsDbContextSetup.GetConnectionString(configuration));
        return new DocumentsDbContext(builder.Options);
    }
}

/// <summary>Перемикач виводу SQL у консоль і лічильник виконаних команд (для перевірки N+1).</summary>
public static class SqlLog
{
    private static int _commandCount;

    public static bool Enabled { get; set; }
    public static int CommandCount => Volatile.Read(ref _commandCount);

    public static void Reset() => Interlocked.Exchange(ref _commandCount, 0);

    public static void Write(string message)
    {
        if (message.Contains("Executed DbCommand", StringComparison.Ordinal))
            Interlocked.Increment(ref _commandCount);

        if (!Enabled)
            return;

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}