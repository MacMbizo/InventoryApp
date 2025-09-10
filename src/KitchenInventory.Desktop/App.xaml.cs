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
using KitchenInventory.Desktop.Constants;
using KitchenInventory.Desktop.Utilities;
using KitchenInventory.Domain.Entities;

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
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;
        try
        {
            var lang = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement), new FrameworkPropertyMetadata(lang));
            FrameworkContentElement.LanguageProperty.OverrideMetadata(typeof(FrameworkContentElement), new FrameworkPropertyMetadata(lang));
        }
        catch (ArgumentException ex)
        {
            // This can occur if metadata was already overridden in this AppDomain (e.g., due to multiple initializations in CI/headless)
            // Proceed without failing; default thread culture is already set.
            Serilog.Log.Warning(ex, "LanguageProperty metadata already registered; continuing without re-registering");
        }
        
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
        bool exportRequested = false;
        string? exportPath = null;
        var exportArg = e.Args.FirstOrDefault(a => a.StartsWith("--export-diagnostics", StringComparison.OrdinalIgnoreCase));
        if (exportArg != null)
        {
            var parts = exportArg.Split('=', 2);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                exportPath = parts[1].Trim('"');
            }
            // Presence of the flag (with or without a path) should trigger export
            exportRequested = true;
        }
        var envExport = Environment.GetEnvironmentVariable("INVENTORY_EXPORT_DIAGNOSTICS");
        if (!string.IsNullOrWhiteSpace(envExport))
        {
            exportRequested = true;
            if (string.IsNullOrWhiteSpace(exportPath))
            {
                exportPath = envExport.Trim('"');
            }
        }
        Log.Information("ExportDiagnostics: Requested={Requested}; Arg='{Arg}'; EnvVar='{EnvVar}'; Path='{Path}'", exportRequested, exportArg ?? "(none)", envExport ?? "(none)", exportPath ?? "(default)");

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
        if (!string.IsNullOrWhiteSpace(crashMode) && exportRequested)
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
        if (exportRequested)
        {
            try
            {
                var exporter = _host.Services.GetRequiredService<IDiagnosticsExporter>();
                if (string.IsNullOrWhiteSpace(exportPath))
                {
                    exportPath = Path.Combine(baseDir, VersionInfo.GetDefaultDiagnosticsFileName());
                }
                Log.Information("ExportDiagnostics: Resolved output path = {Path}", exportPath);
                exporter.ExportAsync(exportPath!).GetAwaiter().GetResult();
                Log.Information("Diagnostics bundle exported to {Path}", exportPath);
                Environment.ExitCode = ExitCodes.Success;
                Shutdown(ExitCodes.Success);
                return;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to export diagnostics bundle to {Path}", exportPath);
                Environment.ExitCode = ExitCodes.ExportError;
                Shutdown(ExitCodes.ExportError);
                return;
            }
        }

        // Headless Items CSV export trigger: env INVENTORY_EXPORT_ITEMS / INVENTORY_EXPORT_ITEMS_CSV or --export-items[=path] / --export-items-csv[=path]
        bool exportItemsRequested = false;
        string? exportItemsPath = null;
        var exportItemsArg = e.Args.FirstOrDefault(a => a.StartsWith("--export-items", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--export-items-csv", StringComparison.OrdinalIgnoreCase));
        if (exportItemsArg != null)
        {
            exportItemsRequested = true;
            var parts = exportItemsArg.Split('=', 2);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                exportItemsPath = parts[1].Trim('"');
            }
            else
            {
                var idx = Array.IndexOf(e.Args, exportItemsArg);
                if (idx >= 0 && idx + 1 < e.Args.Length)
                {
                    var candidate = e.Args[idx + 1];
                    if (!candidate.StartsWith("--"))
                    {
                        exportItemsPath = candidate.Trim('"');
                    }
                }
            }
        }
        var envExportItems = Environment.GetEnvironmentVariable("INVENTORY_EXPORT_ITEMS");
        var envExportItemsCsv = Environment.GetEnvironmentVariable("INVENTORY_EXPORT_ITEMS_CSV");
        if (!string.IsNullOrWhiteSpace(envExportItems) || !string.IsNullOrWhiteSpace(envExportItemsCsv))
        {
            exportItemsRequested = true;
            var candidate = !string.IsNullOrWhiteSpace(envExportItemsCsv) ? envExportItemsCsv : envExportItems;
            if (!string.IsNullOrWhiteSpace(candidate) && !string.Equals(candidate, "1", StringComparison.OrdinalIgnoreCase) && !string.Equals(candidate, "true", StringComparison.OrdinalIgnoreCase))
            {
                exportItemsPath ??= candidate!.Trim('"');
            }
        }

        if (exportItemsRequested)
        {
            try
            {
                Log.Information("Items CSV export requested. Path arg/env: {Path}", exportItemsPath ?? "(default)");
                if (string.IsNullOrWhiteSpace(exportItemsPath))
                {
                    var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                    exportItemsPath = Path.Combine(baseDir, $"items-{ts}.csv");
                }
                var outDir = Path.GetDirectoryName(exportItemsPath);
                if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

                var dbFactory = _host.Services.GetRequiredService<IDbContextFactory<KitchenInventory.Data.KitchenInventoryDbContext>>();
                using var db = dbFactory.CreateDbContext();
                var items = db.Items.AsNoTracking().Include(i => i.Category).OrderBy(i => i.Id).ToList();
                var csv = KitchenInventory.Desktop.Services.CsvExportService.ExportItems(items);

                System.IO.File.WriteAllText(exportItemsPath!, csv, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                Log.Information("Items CSV exported to {Path}. Count={Count}", exportItemsPath, items.Count);
                Environment.ExitCode = ExitCodes.Success;
                Shutdown(ExitCodes.Success);
                return;
            }
            catch (UnauthorizedAccessException ua)
            {
                Log.Fatal(ua, "Permission denied writing Items CSV to {Path}", exportItemsPath);
                Environment.ExitCode = ExitCodes.FileOperationError;
                Shutdown(ExitCodes.FileOperationError);
                return;
            }
            catch (IOException io)
            {
                Log.Fatal(io, "I/O error writing Items CSV to {Path}", exportItemsPath);
                Environment.ExitCode = ExitCodes.FileOperationError;
                Shutdown(ExitCodes.FileOperationError);
                return;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to export Items CSV to {Path}", exportItemsPath);
                Environment.ExitCode = ExitCodes.ExportError;
                Shutdown(ExitCodes.ExportError);
                return;
            }
        }

        // Headless Movements CSV export trigger: env INVENTORY_EXPORT_MOVEMENTS / INVENTORY_EXPORT_MOVEMENTS_CSV or --export-movements[=path] / --export-movements-csv[=path]
        bool exportMovementsRequested = false;
        string? exportMovementsPath = null;
        int movementsTake = 50;
        var exportMovementsArg = e.Args.FirstOrDefault(a => a.StartsWith("--export-movements", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--export-movements-csv", StringComparison.OrdinalIgnoreCase));
        if (exportMovementsArg != null)
        {
            exportMovementsRequested = true;
            var parts = exportMovementsArg.Split('=', 2);
            if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                exportMovementsPath = parts[1].Trim('"');
            }
            else
            {
                var idx = Array.IndexOf(e.Args, exportMovementsArg);
                if (idx >= 0 && idx + 1 < e.Args.Length)
                {
                    var candidate = e.Args[idx + 1];
                    if (!candidate.StartsWith("--"))
                    {
                        exportMovementsPath = candidate.Trim('"');
                    }
                }
            }
        }
        var envExportMovements = Environment.GetEnvironmentVariable("INVENTORY_EXPORT_MOVEMENTS");
        var envExportMovementsCsv = Environment.GetEnvironmentVariable("INVENTORY_EXPORT_MOVEMENTS_CSV");
        var envMovementsTake = Environment.GetEnvironmentVariable("INVENTORY_EXPORT_MOVEMENTS_TAKE");
        if (!string.IsNullOrWhiteSpace(envExportMovements) || !string.IsNullOrWhiteSpace(envExportMovementsCsv))
        {
            exportMovementsRequested = true;
            var candidate = !string.IsNullOrWhiteSpace(envExportMovementsCsv) ? envExportMovementsCsv : envExportMovements;
            if (!string.IsNullOrWhiteSpace(candidate) && !string.Equals(candidate, "1", StringComparison.OrdinalIgnoreCase) && !string.Equals(candidate, "true", StringComparison.OrdinalIgnoreCase))
            {
                exportMovementsPath ??= candidate!.Trim('"');
            }
        }
        if (!string.IsNullOrWhiteSpace(envMovementsTake) && int.TryParse(envMovementsTake, out var takeFromEnv) && takeFromEnv > 0)
        {
            movementsTake = takeFromEnv;
        }
        // Optional CLI take override: --take=NN or --movements-take=NN
        var takeArg = e.Args.FirstOrDefault(a => a.StartsWith("--movements-take", StringComparison.OrdinalIgnoreCase) || a.StartsWith("--take", StringComparison.OrdinalIgnoreCase));
        if (takeArg != null)
        {
            var parts = takeArg.Split('=', 2);
            if (parts.Length == 2 && int.TryParse(parts[1], out var t) && t > 0)
            {
                movementsTake = t;
            }
            else
            {
                var idx = Array.IndexOf(e.Args, takeArg);
                if (idx >= 0 && idx + 1 < e.Args.Length)
                {
                    var candidate = e.Args[idx + 1];
                    if (!candidate.StartsWith("--") && int.TryParse(candidate, out var t2) && t2 > 0)
                    {
                        movementsTake = t2;
                    }
                }
            }
        }

        if (exportMovementsRequested)
        {
            try
            {
                Log.Information("Movements CSV export requested. Path arg/env: {Path}; Take={Take}", exportMovementsPath ?? "(default)", movementsTake);
                if (string.IsNullOrWhiteSpace(exportMovementsPath))
                {
                    var ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                    exportMovementsPath = Path.Combine(baseDir, $"movements-{ts}.csv");
                }
                var outDir = Path.GetDirectoryName(exportMovementsPath);
                if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

                var dbFactory = _host.Services.GetRequiredService<IDbContextFactory<KitchenInventory.Data.KitchenInventoryDbContext>>();
                using var db = dbFactory.CreateDbContext();

                var movements = db.StockMovements.AsNoTracking().OrderByDescending(m => m.CreatedAt).Take(movementsTake).ToList();
                 var itemNames = db.Items.AsNoTracking().ToDictionary(i => i.Id, i => i.Name);
                 string MapName(int id) => itemNames.TryGetValue(id, out var n) ? n : string.Empty;
                 var csv = KitchenInventory.Desktop.Services.CsvExportService.ExportMovements(movements, MapName);
 
                 System.IO.File.WriteAllText(exportMovementsPath!, csv, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                Log.Information("Movements CSV exported to {Path}. Count={Count}", exportMovementsPath, movements.Count);
                Environment.ExitCode = ExitCodes.Success;
                Shutdown(ExitCodes.Success);
                return;
            }
            catch (UnauthorizedAccessException ua)
            {
                Log.Fatal(ua, "Permission denied writing Movements CSV to {Path}", exportMovementsPath);
                Environment.ExitCode = ExitCodes.FileOperationError;
                Shutdown(ExitCodes.FileOperationError);
                return;
            }
            catch (IOException io)
            {
                Log.Fatal(io, "I/O error writing Movements CSV to {Path}", exportMovementsPath);
                Environment.ExitCode = ExitCodes.FileOperationError;
                Shutdown(ExitCodes.FileOperationError);
                return;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Failed to export Movements CSV to {Path}", exportMovementsPath);
                Environment.ExitCode = ExitCodes.ExportError;
                Shutdown(ExitCodes.ExportError);
                return;
            }
        }

        // Headless CSV import trigger: env INVENTORY_IMPORT_CSV or --import-items[=path] / --import-csv[=path]
        string? importPath = null;
        for (int i = 0; i < e.Args.Length; i++)
        {
            var a = e.Args[i];
            if (a.StartsWith("--import-items", StringComparison.OrdinalIgnoreCase) ||
                a.StartsWith("--import-csv", StringComparison.OrdinalIgnoreCase))
            {
                // Support --flag=path
                var parts = a.Split('=', 2);
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[1]))
                {
                    importPath = parts[1].Trim('"');
                }
                else if (string.Equals(a, "--import-items", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(a, "--import-csv", StringComparison.OrdinalIgnoreCase))
                {
                    // Support --flag path (next arg)
                    if (i + 1 < e.Args.Length)
                    {
                        var candidate = e.Args[i + 1];
                        if (!candidate.StartsWith("--"))
                        {
                            importPath = candidate.Trim('"');
                        }
                    }
                }
                break; // stop after first match
            }
        }
         var envImport = Environment.GetEnvironmentVariable("INVENTORY_IMPORT_CSV");
         if (!string.IsNullOrWhiteSpace(envImport))
         {
             importPath ??= envImport;
         }

        if (!string.IsNullOrWhiteSpace(importPath))
        {
            try
            {
                Log.Information("CSV import requested from {Path}", importPath);
                if (!File.Exists(importPath))
                {
                    Log.Error("Import file not found: {Path}", importPath);
                    Environment.ExitCode = ExitCodes.FileOperationError;
                    Shutdown(ExitCodes.FileOperationError);
                    return;
                }

                var csvText = File.ReadAllText(importPath);
                if (string.IsNullOrWhiteSpace(csvText))
                {
                    Log.Warning("Import file is empty: {Path}", importPath);
                    Environment.ExitCode = ExitCodes.Success; // treat empty import as no-op success
                    Shutdown(ExitCodes.Success);
                    return;
                }

                // Resolve services
                var csvImporter = _host.Services.GetRequiredService<ICsvImportService>();
                var dbFactory = _host.Services.GetRequiredService<IDbContextFactory<KitchenInventory.Data.KitchenInventoryDbContext>>();

                using var db = dbFactory.CreateDbContext();
                using var tx = db.Database.BeginTransaction();

                // Load categories for mapping in parser (exclude sentinel if any)
                var categories = db.Categories.AsNoTracking().ToList();
                // Parse items
                var parsed = csvImporter.ParseItemsAsync(csvText, categories).GetAwaiter().GetResult();

                if (parsed.Count == 0)
                {
                    Log.Information("No items found in CSV: {Path}", importPath);
                    Environment.ExitCode = ExitCodes.Success;
                    Shutdown(ExitCodes.Success);
                    return;
                }

                // Load existing items and build lookup
                var existingItems = db.Items.ToList();
                var byId = existingItems.ToDictionary(i => i.Id, i => i);
                var byName = existingItems
                    .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                    .GroupBy(i => i.Name!.Trim().ToLowerInvariant())
                    .ToDictionary(g => g.Key, g => g.First());

                int addedCount = 0;
                int updatedCount = 0;
                var movements = new List<StockMovement>();
                var nowUtc = DateTime.UtcNow;

                foreach (var imp in parsed)
                {
                    var name = (imp.Name ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    Item? target = null;
                    if (imp.Id > 0 && byId.TryGetValue(imp.Id, out var byIdMatch))
                    {
                        target = byIdMatch;
                    }
                    else if (byName.TryGetValue(name.ToLowerInvariant(), out var byNameMatch))
                    {
                        target = byNameMatch;
                    }

                    if (target == null)
                    {
                        // New item
                        var entity = new Item
                        {
                            Name = name,
                            Quantity = imp.Quantity,
                            Unit = string.IsNullOrWhiteSpace(imp.Unit) ? "pcs" : imp.Unit!,
                            CategoryId = imp.CategoryId,
                            ExpiryDate = imp.ExpiryDate,
                            CreatedAtUtc = imp.CreatedAtUtc == default ? nowUtc : imp.CreatedAtUtc,
                            UpdatedAtUtc = nowUtc
                        };
                        db.Items.Add(entity);

                        if (entity.Quantity != 0)
                        {
                            movements.Add(new StockMovement
                            {
                                Item = entity,
                                Type = MovementType.Add,
                                Quantity = Math.Abs(entity.Quantity),
                                Reason = "Import add",
                                User = Environment.UserName,
                                TimestampUtc = nowUtc
                            });
                        }
                        addedCount++;
                    }
                    else
                    {
                        // Existing item -> update fields and record quantity delta
                        var oldQty = target.Quantity;
                        var newQty = imp.Quantity;
                        var delta = newQty - oldQty;

                        target.Unit = string.IsNullOrWhiteSpace(imp.Unit) ? target.Unit : imp.Unit!;
                        target.ExpiryDate = imp.ExpiryDate;
                        target.CategoryId = imp.CategoryId;
                        target.Quantity = newQty;
                        target.UpdatedAtUtc = nowUtc;

                        db.Attach(target);
                        db.Entry(target).State = EntityState.Modified;

                        if (delta != 0)
                        {
                            movements.Add(new StockMovement
                            {
                                ItemId = target.Id,
                                Type = delta > 0 ? MovementType.Add : MovementType.Consume,
                                Quantity = Math.Abs(delta),
                                Reason = "Import update",
                                User = Environment.UserName,
                                TimestampUtc = nowUtc
                            });
                        }

                        updatedCount++;
                    }
                }

                if (movements.Count > 0)
                {
                    db.StockMovements.AddRange(movements);
                }

                db.SaveChanges();
                tx.Commit();

                Log.Information("CSV import completed. Total: {Total}; Added: {Added}; Updated: {Updated}", parsed.Count, addedCount, updatedCount);
                Environment.ExitCode = ExitCodes.Success;
                Shutdown(ExitCodes.Success);
                return;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "CSV import failed from {Path}", importPath);
                Environment.ExitCode = ExitCodes.ImportError;
                Shutdown(ExitCodes.ImportError);
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
            Environment.ExitCode = ExitCodes.Success;
            Shutdown(ExitCodes.Success);
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
            Environment.ExitCode = ExitCodes.GeneralError;
            Shutdown(ExitCodes.GeneralError);
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
            Environment.ExitCode = ExitCodes.GeneralError;
            Shutdown(ExitCodes.GeneralError);
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
            Environment.ExitCode = ExitCodes.GeneralError;
            Shutdown(ExitCodes.GeneralError);
        }
    }

    private void App_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "UnobservedTaskException");
        SentrySdk.CaptureException(e.Exception);
        e.SetObserved();
        if (_headlessMode || _crashTest)
        {
            Environment.ExitCode = ExitCodes.GeneralError;
            Shutdown(ExitCodes.GeneralError);
        }
    }
}

