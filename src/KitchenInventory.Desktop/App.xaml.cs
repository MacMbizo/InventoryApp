using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Markup;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Threading;
// Add ViewModels namespace
using KitchenInventory.Desktop.ViewModels;
using KitchenInventory.Desktop.Services;
using Npgsql.EntityFrameworkCore.PostgreSQL;
using Sentry;
using Sentry.Serilog;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System.Text.Json;
using System.IO.Compression;

namespace KitchenInventory.Desktop;

public partial class App : Application
{
    private IHost? _host;
    private bool _headlessMode = false;
    private IDisposable? _sentry;
    private bool _crashTest = false;
    public IServiceProvider Services => _host?.Services ?? throw new InvalidOperationException("Host not initialized");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Ensure the application only exits when we explicitly call Shutdown (important for headless mode)
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Ensure bindings/validation use current culture for numbers and dates
        var lang = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(lang));
        FrameworkContentElement.LanguageProperty.OverrideMetadata(typeof(FrameworkContentElement), new FrameworkPropertyMetadata(lang));

        // Global exception logging hooks so unexpected errors surface in logs/console
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += App_UnhandledException;
        TaskScheduler.UnobservedTaskException += App_UnobservedTaskException;

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
                    .WriteTo.Console()
                    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day);

                var dsn = Environment.GetEnvironmentVariable("SENTRY_DSN") ?? ctx.Configuration["Sentry:Dsn"];
                if (!string.IsNullOrWhiteSpace(dsn))
                {
                    _sentry = SentrySdk.Init(o =>
                    {
                        o.Dsn = dsn;
                        o.Release = typeof(App).Assembly.GetName().Version?.ToString();
                        o.Environment = ctx.HostingEnvironment.EnvironmentName;
                        o.AutoSessionTracking = true;
                        o.TracesSampleRate = 0.0; // disable APM by default
                    });

                    loggerConfiguration.WriteTo.Sentry(o =>
                    {
                        o.MinimumBreadcrumbLevel = Serilog.Events.LogEventLevel.Information;
                        o.MinimumEventLevel = Serilog.Events.LogEventLevel.Error;
                    });
                }
            })
            .ConfigureServices((ctx, services) =>
            {
                // Resolve provider and connection string or compute default for SQLite
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
                            Log.Warning("No KitchenDb connection string provided for Npgsql; falling back to SQLite at {Path}", sqlitePath);
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

                // Keep AddDbContext for migration scope usage
                services.AddDbContext<KitchenInventory.Data.KitchenInventoryDbContext>(Configure);

                // Add factory for WPF usage (create contexts on-demand per operation)
                services.AddDbContextFactory<KitchenInventory.Data.KitchenInventoryDbContext>(Configure);

                // Diagnostics service and window
                services.AddSingleton<IDatabaseInfoService, DatabaseInfoService>();
                services.AddTransient<DiagnosticsWindow>();
                services.AddSingleton<IDiagnosticsExporter, DiagnosticsExporter>();
                
                // Preferences and Settings
                services.AddSingleton<IPreferencesService, PreferencesService>();
                services.AddTransient<SettingsWindow>();

                services.AddSingleton<IFileSaveService, FileSaveService>();
                services.AddSingleton<IFileOpenService, FileOpenService>();
                services.AddSingleton<ICsvImportService, CsvImportService>();
                services.AddTransient<ItemsViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        // Start host (sync to keep OnStartup non-async)
        _host.Start();

        Log.Information("Application starting up");

        // Diagnostics export trigger: env INVENTORY_EXPORT_DIAGNOSTICS or --export-diagnostics[=path]
        string? exportPath = null;
        var exportArg = e.Args.FirstOrDefault(a => a.StartsWith("--export-diagnostics", StringComparison.OrdinalIgnoreCase));
        if (exportArg != null)
        {
            var parts = exportArg.Split('=', 2);
            exportPath = parts.Length == 2 ? parts[1] : null;
        }
        var envExport = Environment.GetEnvironmentVariable("INVENTORY_EXPORT_DIAGNOSTICS");
        if (!string.IsNullOrWhiteSpace(envExport))
        {
            exportPath ??= envExport;
        }

        // Synthetic crash triggers for validation: env INVENTORY_CRASH_TEST or --crash-test[=sentry|dump]
        string? crashMode = null;
        var crashArg = e.Args.FirstOrDefault(a => a.StartsWith("--crash-test", StringComparison.OrdinalIgnoreCase));
        if (crashArg != null)
        {
            var parts = crashArg.Split('=', 2);
            crashMode = parts.Length == 2 ? parts[1] : "sentry";
        }
        var envCrash = Environment.GetEnvironmentVariable("INVENTORY_CRASH_TEST");
        if (!string.IsNullOrWhiteSpace(envCrash))
        {
            if (string.Equals(envCrash, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(envCrash, "true", StringComparison.OrdinalIgnoreCase))
            {
                crashMode ??= "sentry";
            }
            else
            {
                crashMode ??= envCrash;
            }
        }
        if (!string.IsNullOrWhiteSpace(crashMode) && !string.IsNullOrWhiteSpace(exportPath))
        {
            Log.Warning("Both crash-test and export-diagnostics requested. Proceeding with diagnostics export and skipping crash-test.");
            crashMode = null;
        }

        if (!string.IsNullOrWhiteSpace(crashMode))
        {
            _crashTest = true;
            Log.Error("Synthetic crash requested: {Mode}", crashMode);
            if (string.Equals(crashMode, "dump", StringComparison.OrdinalIgnoreCase))
            {
                try { SentrySdk.CaptureMessage("Synthetic hard crash via Environment.FailFast"); } catch { }
                Environment.FailFast("Synthetic hard crash (Dump)");
            }
            else
            {
                throw new InvalidOperationException("Synthetic crash (Sentry)");
            }
        }

        // Ensure database exists on first run and apply migrations
        using (var scope = _host!.Services.CreateScope())
        {
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

                db.Database.Migrate();

                // Startup diagnostics logging
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
                    string Extract(string key)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(connStr, $@"(?i){System.Text.RegularExpressions.Regex.Escape(key)}\s*=\s*([^;]+)");
                        return m.Success ? m.Groups[1].Value : string.Empty;
                    }
                    var host = Extract("Host");
                    var port = Extract("Port");
                    var database = Extract("Database");
                    target = string.IsNullOrEmpty(host) ? "(unknown)" : $"{host}:{(string.IsNullOrEmpty(port) ? "5432" : port)}/{(string.IsNullOrEmpty(database) ? "(unknown)" : database)}";
                }
                var applied = db.Database.GetAppliedMigrations().Count();
                var pending = db.Database.GetPendingMigrations().Count();
                Log.Information("DB Provider: {Provider}; Target: {Target}; Applied={Applied}; Pending={Pending}", providerName, target, applied, pending);
            }
            finally
            {
                if (db.Database.GetDbConnection().State == System.Data.ConnectionState.Open)
                {
                    db.Database.CloseConnection();
                }
            }
        }

        // If diagnostics export requested, perform it and exit
        if (!string.IsNullOrWhiteSpace(exportPath))
        {
            try
            {
                var exporter = _host.Services.GetRequiredService<IDiagnosticsExporter>();
                if (string.IsNullOrWhiteSpace(exportPath))
                {
                    var ts = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
                    exportPath = Path.Combine(baseDir, $"diagnostics-{ts}.zip");
                }
                exporter.ExportAsync(exportPath!).GetAwaiter().GetResult();
                Log.Information("Diagnostics bundle exported to {Path}", exportPath);
                Environment.ExitCode = 0;
                Shutdown(0);
                return;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to export diagnostics bundle to {Path}", exportPath);
                Environment.ExitCode = 1;
                Shutdown(1);
                return;
            }
        }

        // Headless/CI smoke mode: don't create UI, just verify startup and DB and exit 0
        var headless = e.Args.Any(a => string.Equals(a, "--headless", StringComparison.OrdinalIgnoreCase))
                       || string.Equals(Environment.GetEnvironmentVariable("INVENTORY_HEADLESS"), "1", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);
        _headlessMode = headless;
        if (headless)
        {
            Log.Information("Headless mode: startup + DB migration succeeded; exiting without UI");
            Environment.ExitCode = 0;
            Shutdown(0);
            return;
        }

        try
        {
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            // In UI mode, revert to normal shutdown behavior tied to main window
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Failed to show MainWindow");
            if (!_headlessMode)
            {
                MessageBox.Show($"Failed to start application: {ex.Message}", "Kitchen Inventory", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.Information("Application shutting down");
        Log.Information("OnExit: ExitCode={ExitCode}, Headless={Headless}", e.ApplicationExitCode, _headlessMode);
        try { SentrySdk.FlushAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult(); } catch { }
        _sentry?.Dispose();
        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "DispatcherUnhandledException");
        SentrySdk.CaptureException(e.Exception);
        if (!_headlessMode)
        {
            MessageBox.Show($"Unexpected error: {e.Exception.Message}", "Kitchen Inventory", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        e.Handled = true; // prevent crash when possible
        if (_headlessMode || _crashTest)
        {
            Shutdown(1);
        }
    }

    private void App_UnhandledException(object? sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Log.Fatal(ex, "UnhandledException");
            SentrySdk.CaptureException(ex);
        }
        else
        {
            Log.Fatal("UnhandledException: {Error}", e.ExceptionObject);
            SentrySdk.CaptureMessage($"UnhandledException (non-Exception): {e.ExceptionObject}", SentryLevel.Fatal);
        }
        if (_headlessMode || _crashTest)
        {
            Shutdown(1);
        }
    }

    private void App_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "UnobservedTaskException");
        SentrySdk.CaptureException(e.Exception);
        e.SetObserved();
        if (_headlessMode || _crashTest)
        {
            Shutdown(1);
        }
    }
}

