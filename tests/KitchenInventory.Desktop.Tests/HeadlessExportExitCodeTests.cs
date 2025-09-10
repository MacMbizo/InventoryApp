using System;
using System.Diagnostics;
using System.IO;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

[Collection("UI")]
[Trait("Category", "UI")]
public class HeadlessExportExitCodeTests
{
    private static string GetDesktopExePath() => UiTestHelpers.GetDesktopExePath();

    private static ProcessStartInfo CreateBasePsi(string exe)
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
}