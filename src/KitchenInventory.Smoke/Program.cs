using System;
using System.IO;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Npgsql.EntityFrameworkCore.PostgreSQL;

// KitchenInventory.Smoke: Headless console smoke test for startup + DB migrations

var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
var baseDir = Path.Combine(appData, "InventoryApp");
Directory.CreateDirectory(baseDir);
var logsDir = Path.Combine(baseDir, "logs");
Directory.CreateDirectory(logsDir);
var logPath = Path.Combine(logsDir, "log-.txt");

using IHost host = Host.CreateDefaultBuilder()
    .UseSerilog((ctx, services, loggerConfiguration) =>
    {
        loggerConfiguration
            .ReadFrom.Configuration(ctx.Configuration)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day);
    })
    .ConfigureServices((ctx, services) =>
    {
        var provider = ctx.Configuration["Database:Provider"]
                       ?? Environment.GetEnvironmentVariable("INVENTORY_DB_PROVIDER")
                       ?? "Sqlite";

        var configuredCs = ctx.Configuration.GetConnectionString("KitchenDb");
        var sqlitePath = Path.Combine(baseDir, "kitchen.db");
        var sqliteCs = $"Data Source={sqlitePath};Cache=Shared;Pooling=True";

        void Configure(DbContextOptionsBuilder options)
        {
            if (string.Equals(provider, "Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                var cs = !string.IsNullOrWhiteSpace(configuredCs)
                    ? configuredCs
                    : Environment.GetEnvironmentVariable("INVENTORY_DB_CONNECTION");
                if (string.IsNullOrWhiteSpace(cs))
                {
                    Log.Warning("[Smoke] No KitchenDb connection string provided for Npgsql; falling back to SQLite at {Path}", sqlitePath);
                    options.UseSqlite(sqliteCs);
                }
                else
                {
                    options.UseNpgsql(cs);
                }
            }
            else
            {
                var cs = !string.IsNullOrWhiteSpace(configuredCs) ? configuredCs : sqliteCs;
                options.UseSqlite(cs);
            }
        }

        services.AddDbContext<KitchenInventory.Data.KitchenInventoryDbContext>(Configure);
        services.AddDbContextFactory<KitchenInventory.Data.KitchenInventoryDbContext>(Configure);
    })
    .Build();

try
{
    await host.StartAsync();
    Log.Information("[Smoke] Host started, beginning DB migration checks...");

    using var scope = host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<KitchenInventory.Data.KitchenInventoryDbContext>();

    try
    {
        if (db.Database.IsSqlite())
        {
            db.Database.OpenConnection();
            db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
            db.Database.ExecuteSqlRaw("PRAGMA busy_timeout=5000;");
            db.Database.ExecuteSqlRaw("PRAGMA foreign_keys=ON;");
            db.Database.ExecuteSqlRaw("PRAGMA synchronous=NORMAL;");
        }

        await db.Database.MigrateAsync();

        var providerName = db.Database.ProviderName ?? "(unknown)";
        var connStr = db.Database.GetDbConnection().ConnectionString ?? string.Empty;
        string target;
        if (db.Database.IsSqlite())
        {
            var m = System.Text.RegularExpressions.Regex.Match(connStr, @"(?i)(Data Source|DataSource)\s*=\s*([^;]+)");
            target = m.Success ? m.Groups[2].Value : "(unknown)";
        }
        else
        {
            static string Extract(string s, string key)
            {
                var m = System.Text.RegularExpressions.Regex.Match(s, $@"(?i){System.Text.RegularExpressions.Regex.Escape(key)}\s*=\s*([^;]+)");
                return m.Success ? m.Groups[1].Value : string.Empty;
            }
            var hostName = Extract(connStr, "Host");
            var port = Extract(connStr, "Port");
            var database = Extract(connStr, "Database");
            target = string.IsNullOrEmpty(hostName) ? "(unknown)" : $"{hostName}:{(string.IsNullOrEmpty(port) ? "5432" : port)}/{(string.IsNullOrEmpty(database) ? "(unknown)" : database)}";
        }

        var applied = (await db.Database.GetAppliedMigrationsAsync()).Count();
        var pending = (await db.Database.GetPendingMigrationsAsync()).Count();
        Log.Information("[Smoke] DB Provider: {Provider}; Target: {Target}; Applied={Applied}; Pending={Pending}", providerName, target, applied, pending);

        // Light query to validate read path
        Log.Information("[Smoke] Loading items...");
        var itemsCount = await db.Set<KitchenInventory.Domain.Entities.Item>().CountAsync();
        Log.Information("[Smoke] Items in DB: {Count}", itemsCount);

        Log.Information("[Smoke] Success: migrations applied and basic query succeeded. Exiting 0.");
        Environment.ExitCode = 0;
    }
    finally
    {
        if (db.Database.GetDbConnection().State == System.Data.ConnectionState.Open)
        {
            db.Database.CloseConnection();
        }
    }
}
catch (Exception ex)
{
    Log.Fatal(ex, "[Smoke] Failure during startup/migration");
    Environment.ExitCode = 1;
}
finally
{
    Log.Information("[Smoke] Shutting down");
    await host.StopAsync(TimeSpan.FromSeconds(5));
    host.Dispose();
    Log.CloseAndFlush();
}