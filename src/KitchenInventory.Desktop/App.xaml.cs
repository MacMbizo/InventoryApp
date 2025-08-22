using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.IO;
using System.Windows;
// Add ViewModels namespace
using KitchenInventory.Desktop.ViewModels;

namespace KitchenInventory.Desktop;

public partial class App : Application
{
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDir = Path.Combine(appData, "InventoryApp");
        Directory.CreateDirectory(baseDir);

        var logsDir = Path.Combine(baseDir, "logs");
        Directory.CreateDirectory(logsDir);
        var logPath = Path.Combine(logsDir, "log-.txt");

        _host = Host.CreateDefaultBuilder()
            .UseSerilog((ctx, services, loggerConfiguration) =>
            {
                loggerConfiguration
                    .ReadFrom.Configuration(ctx.Configuration)
                    .Enrich.FromLogContext()
                    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day);
            })
            .ConfigureServices((ctx, services) =>
            {
                // Resolve connection string or compute default
                var connectionString = ctx.Configuration.GetConnectionString("KitchenDb");
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    var dbPath = Path.Combine(baseDir, "kitchen.db");
                    connectionString = $"Data Source={dbPath}";
                }

                // Keep AddDbContext for migration scope usage
                services.AddDbContext<KitchenInventory.Data.KitchenInventoryDbContext>(options =>
                    options.UseSqlite(connectionString));

                // Add factory for WPF usage (create contexts on-demand per operation)
                services.AddDbContextFactory<KitchenInventory.Data.KitchenInventoryDbContext>(options =>
                    options.UseSqlite(connectionString));

                // ViewModels & Windows
                services.AddSingleton<ItemsViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        // Start host (sync to keep OnStartup non-async)
        _host.Start();

        Log.Information("Application starting up");

        // Ensure database exists on first run and apply migrations
        using (var scope = _host!.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<KitchenInventory.Data.KitchenInventoryDbContext>();
            db.Database.Migrate();
        }

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application shutting down");

        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}

