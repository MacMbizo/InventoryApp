using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace KitchenInventory.Desktop.Services;

public class DiagnosticsExporter : IDiagnosticsExporter
{
    private readonly IDatabaseInfoService _dbInfo;
    private readonly IHostEnvironment _env;
    private readonly IConfiguration _config;

    public DiagnosticsExporter(IDatabaseInfoService dbInfo, IHostEnvironment env, IConfiguration config)
    {
        _dbInfo = dbInfo;
        _env = env;
        _config = config;
    }

    public async Task ExportAsync(string outputZipPath, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(outputZipPath)) throw new ArgumentException("Output path required", nameof(outputZipPath));
        var dir = Path.GetDirectoryName(outputZipPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // Prepare temp directory for assembling bundle
        var stagingDir = Path.Combine(Path.GetTempPath(), "inv-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDir);
        try
        {
            // 1) Collect environment/runtime info
            var envInfo = new
            {
                Machine = Environment.MachineName,
                OS = Environment.OSVersion.ToString(),
                Framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                ProcessArch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                OsArch = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
                AppEnvironment = _env.EnvironmentName,
                Version = typeof(DiagnosticsExporter).Assembly.GetName().Version?.ToString(),
                TimeUtc = DateTimeOffset.UtcNow,
                Variables = new
                {
                    CI = Environment.GetEnvironmentVariable("CI"),
                    INVENTORY_DB_PROVIDER = Environment.GetEnvironmentVariable("INVENTORY_DB_PROVIDER"),
                    INVENTORY_HEADLESS = Environment.GetEnvironmentVariable("INVENTORY_HEADLESS"),
                }
            };
            var envJson = JsonSerializer.Serialize(envInfo, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(stagingDir, "environment.json"), envJson, ct);

            // 2) Database info (redacted)
            var db = await _dbInfo.GetInfoAsync(ct);
            var dbJson = JsonSerializer.Serialize(db, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(Path.Combine(stagingDir, "database.json"), dbJson, ct);

            // 3) appsettings.json copy (non-secret) if available
            try
            {
                var appSettingsName = "appsettings.json";
                var baseDir = AppContext.BaseDirectory;
                var appSettingsPath = Path.Combine(baseDir, appSettingsName);
                if (File.Exists(appSettingsPath))
                {
                    File.Copy(appSettingsPath, Path.Combine(stagingDir, appSettingsName), overwrite: true);
                }
            }
            catch { }

            // 4) Logs: include last 5 log files (rolling File sink writes log-YYYYMMDD.txt)
            try
            {
                if (!string.IsNullOrEmpty(db.LogsDirectory) && Directory.Exists(db.LogsDirectory))
                {
                    var logsDest = Path.Combine(stagingDir, "logs");
                    Directory.CreateDirectory(logsDest);
                    var files = new DirectoryInfo(db.LogsDirectory)
                        .GetFiles("log-*.txt")
                        .OrderByDescending(f => f.LastWriteTimeUtc)
                        .Take(5);
                    foreach (var f in files)
                    {
                        var dest = Path.Combine(logsDest, f.Name);
                        f.CopyTo(dest, overwrite: true);
                    }
                }
            }
            catch { }

            // 5) SQLite DB copy (if used) - copy .db-shm and .db-wal if present (best-effort)
            try
            {
                if (!string.IsNullOrEmpty(db.DatabaseFilePath) && File.Exists(db.DatabaseFilePath))
                {
                    var dbDest = Path.Combine(stagingDir, "db");
                    Directory.CreateDirectory(dbDest);
                    var main = new FileInfo(db.DatabaseFilePath);
                    main.CopyTo(Path.Combine(dbDest, main.Name), overwrite: true);
                    var shm = db.DatabaseFilePath + "-shm";
                    var wal = db.DatabaseFilePath + "-wal";
                    if (File.Exists(shm)) File.Copy(shm, Path.Combine(dbDest, Path.GetFileName(shm)), overwrite: true);
                    if (File.Exists(wal)) File.Copy(wal, Path.Combine(dbDest, Path.GetFileName(wal)), overwrite: true);
                }
            }
            catch { }

            // Create zip
            if (File.Exists(outputZipPath)) File.Delete(outputZipPath);
            ZipFile.CreateFromDirectory(stagingDir, outputZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
        }
        finally
        {
            try { Directory.Delete(stagingDir, recursive: true); } catch { }
        }
    }
}