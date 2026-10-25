using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

[Collection("UI")]
[Trait("Category", "UI")]
public class HeadlessExportExitCodeTests
{
    private static string GetDesktopExePath() => UiTestHelpers.GetDesktopExePath();

    private static ProcessStartInfo CreateBasePsi(string exe, string? sqliteConnectionString = null)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            Arguments = string.Empty,
        };

        // Ensure headless, suppress any first-run UI, and avoid unrelated triggers
        psi.Environment["CI"] = "true";
        psi.Environment["INVENTORY_HEADLESS"] = "1";
        psi.Environment["INVENTORY_SUPPRESS_FIRST_RUN"] = "1";
        // Keep all app state (daily log, preferences, default DB) beside an isolated
        // per-test DB under the repo instead of the developer's %LOCALAPPDATA%\InventoryApp.
        var connection = sqliteConnectionString ?? UiTestHelpers.CreateIsolatedSqliteConnectionString();
        psi.Environment["INVENTORY_DATA_DIR"] = UiTestHelpers.ResolveDataDirectory(connection);
        psi.Environment["INVENTORY_DB_PROVIDER"] = "sqlite";
        psi.Environment["INVENTORY_DB_CONNECTION"] = connection;
        psi.Environment["ConnectionStrings__KitchenDb"] = connection;
        psi.Environment["Database__Provider"] = "Sqlite";
        psi.Environment.Remove("INVENTORY_EXPORT_DIAGNOSTICS");
        psi.Environment.Remove("INVENTORY_CRASH_TEST");
        psi.Environment.Remove("INVENTORY_IMPORT_CSV");
        psi.Environment.Remove("INVENTORY_EXPORT_ITEMS");
        psi.Environment.Remove("INVENTORY_EXPORT_ITEMS_CSV");
        psi.Environment.Remove("INVENTORY_EXPORT_MOVEMENTS");
        psi.Environment.Remove("INVENTORY_EXPORT_MOVEMENTS_CSV");
        psi.Environment.Remove("INVENTORY_EXPORT_MOVEMENTS_TAKE");
        return psi;
    }

    [StaFact]
    public void ExportItems_WithExplicitPath_ExitsZero_AndCreatesFile()
    {
        var exe = GetDesktopExePath();
        var tempFile = Path.Combine(Path.GetTempPath(), $"ki-items-{Guid.NewGuid():N}.csv");

        // Ensure clean slate
        try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }

        var psi = CreateBasePsi(exe);
        psi.Arguments = $"--export-items \"{tempFile}\"";

        using var process = Process.Start(psi)!;
        var exited = process.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds);
        Assert.True(exited, "Process did not exit within timeout for items export.");
        Assert.Equal(0, process.ExitCode);

        Assert.True(File.Exists(tempFile), $"Expected items CSV to be created at {tempFile}.");
        var len = new FileInfo(tempFile).Length;
        Assert.True(len > 0, "Items CSV file is empty.");

        // Cleanup
        try { File.Delete(tempFile); } catch { }
    }

    [StaFact]
    public void ExportMovements_WithExplicitPath_ExitsZero_AndCreatesFile()
    {
        var exe = GetDesktopExePath();
        var tempFile = Path.Combine(Path.GetTempPath(), $"ki-movements-{Guid.NewGuid():N}.csv");

        // Ensure clean slate
        try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }

        var psi = CreateBasePsi(exe);
        // Keep take small to run fast regardless of DB size
        psi.Arguments = $"--export-movements \"{tempFile}\" --movements-take 1";

        using var process = Process.Start(psi)!;
        var exited = process.WaitForExit((int)TimeSpan.FromSeconds(30).TotalMilliseconds);
        Assert.True(exited, "Process did not exit within timeout for movements export.");
        Assert.Equal(0, process.ExitCode);

        Assert.True(File.Exists(tempFile), $"Expected movements CSV to be created at {tempFile}.");
        var len = new FileInfo(tempFile).Length;
        Assert.True(len > 0, "Movements CSV file is empty.");

        // Cleanup
        try { File.Delete(tempFile); } catch { }
    }

    [StaFact]
    public void ExportDiagnostics_WithExplicitPath_ExitsZero_AndCreatesTheSupportBundle()
    {
        var exe = GetDesktopExePath();
        var zipPath = Path.Combine(Path.GetTempPath(), $"ki-diag-{Guid.NewGuid():N}.zip");

        try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }

        var psi = CreateBasePsi(exe);
        psi.Arguments = $"--export-diagnostics \"{zipPath}\"";

        using var process = Process.Start(psi)!;
        var exited = process.WaitForExit((int)TimeSpan.FromSeconds(60).TotalMilliseconds);
        Assert.True(exited, "Process did not exit within timeout for the diagnostics export.");
        Assert.Equal(0, process.ExitCode);

        Assert.True(File.Exists(zipPath), $"Expected the support bundle to be created at {zipPath}.");

        // The bundle is what a school sends when something goes wrong, so it has to actually
        // carry the environment, the database facts and the log that explains the failure.
        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("environment.json", entries);
        Assert.Contains("database.json", entries);
        Assert.Contains(entries, e => e.StartsWith("logs/", StringComparison.Ordinal));

        try { File.Delete(zipPath); } catch { }
    }

    [StaFact]
    public void CrashTest_WithReportingNotAgreedTo_ExitsNonZero_AndSaysSoInTheLog()
    {
        var exe = GetDesktopExePath();
        var connection = UiTestHelpers.CreateIsolatedSqliteConnectionString();
        var dataDir = UiTestHelpers.ResolveDataDirectory(connection);

        var psi = CreateBasePsi(exe, connection);
        psi.Arguments = "--crash-test=sentry";
        // A brand-new data directory has no saved consent preference, which is exactly the
        // "not agreed to" state that must not send anything to Sentry.
        psi.Environment["INVENTORY_CRASH_TEST"] = "sentry";

        using var process = Process.Start(psi)!;
        var exited = process.WaitForExit((int)TimeSpan.FromSeconds(60).TotalMilliseconds);
        Assert.True(exited, "Process did not exit within timeout for the crash test.");
        Assert.NotEqual(0, process.ExitCode);

        var logsDir = Path.Combine(dataDir, "logs");
        Assert.True(Directory.Exists(logsDir), $"Expected the app to write its log under {logsDir}.");
        var log = File.ReadAllText(Directory.GetFiles(logsDir, "log-*.txt").OrderBy(f => f).Last());

        Assert.Contains("Synthetic crash", log);
        // The support bundle has to be able to explain why nothing was sent...
        Assert.Contains("Crash reporting is off", log);
        // ...and an empty Sentry event id is the proof that the SDK never accepted the crash.
        Assert.Contains("DispatcherUnhandledException (Sentry event 00000000000000000000000000000000)", log);
    }
}