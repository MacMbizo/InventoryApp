using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

/// <summary>
/// Drives the real <c>Your data</c> window against a launched app: the menu entry must open it,
/// the automatic backup must already be there, and "Back up now" must leave a second copy.
/// A menu entry that does nothing is exactly the defect this covers.
/// </summary>
[Collection("UI")]
[Trait("Category", "UI")]
public class DatabaseMaintenanceUiTests
{
    private static string ReadStatus(Window dialog)
    {
        try
        {
            var box = dialog.FindFirstDescendant(cf => cf.ByAutomationId("DatabaseStatusBox"));
            if (box == null) return string.Empty;
            if (box.Patterns.Value.IsSupported)
            {
                return (box.Patterns.Value.Pattern.Value ?? string.Empty).Trim();
            }
            return (box.Name ?? string.Empty).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool WaitForStatus(Window dialog, string contains, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (ReadStatus(dialog).Contains(contains, StringComparison.OrdinalIgnoreCase)) return true;
            Thread.Sleep(200);
        }
        return false;
    }

    private static int CountBackups(Window dialog)
    {
        try
        {
            var list = dialog.FindFirstDescendant(cf => cf.ByAutomationId("BackupsList"));
            if (list == null) return 0;
            return list.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem)).Length;
        }
        catch
        {
            return 0;
        }
    }

    [StaFact]
    public void YourData_Opens_ShowsTheAutomaticBackup_AndTakesAnotherOnDemand()
    {
        var connection = UiTestHelpers.CreateIsolatedSqliteConnectionString();
        var psi = UiTestHelpers.CreateStartInfo(connection);

        using var process = Process.Start(psi)!;
        try
        {
            using var automation = new UIA3Automation();
            var mainWindow = UiTestHelpers.CompleteStartupGating(automation, process);

            // Act: open the window from the menu bar.
            UiTestHelpers.ClickActionByName(mainWindow, "Your data");
            var dialog = WaitForWindow(process, automation, "Your inventory data", TimeSpan.FromSeconds(20));
            Assert.True(dialog != null, "The 'Your data' window did not open from the menu entry.");

            // The window must name the file it is looking after — not a placeholder.
            var pathText = dialog!.FindFirstDescendant(cf => cf.ByAutomationId("DatabasePathText"))?.Name ?? string.Empty;
            Assert.Contains("kitchen.e2e.db", pathText, StringComparison.OrdinalIgnoreCase);

            // The launch itself takes a backup, so one must already be listed.
            var initialCount = CountBackups(dialog);
            Assert.True(initialCount >= 1,
                $"Expected the automatic startup backup to be listed; found {initialCount} backup(s).");

            // The integrity check must come back clean for a freshly created database.
            UiTestHelpers.TryInvokeWithFallback(
                dialog.FindFirstDescendant(cf => cf.ByAutomationId("CheckDatabaseButton"))!);
            Assert.True(WaitForStatus(dialog, "No problems found", TimeSpan.FromSeconds(30)),
                $"Integrity check did not report a healthy database. Status was: '{ReadStatus(dialog)}'");

            // Back up now: writes into the backups folder and must be reflected in the list.
            UiTestHelpers.TryInvokeWithFallback(
                dialog.FindFirstDescendant(cf => cf.ByAutomationId("BackUpNowButton"))!);
            Assert.True(WaitForStatus(dialog, "copy of your data was saved", TimeSpan.FromSeconds(30)),
                $"On-demand backup did not report success. Status was: '{ReadStatus(dialog)}'");
            Assert.True(CountBackups(dialog) > initialCount,
                "The on-demand backup was not added to the list of backups.");

            // The backup folder must actually contain the files the window claims exist.
            var backupsDir = Path.Combine(UiTestHelpers.ResolveDataDirectory(connection), "backups");
            var files = Directory.Exists(backupsDir) ? Directory.GetFiles(backupsDir, "*.db") : Array.Empty<string>();
            Assert.True(files.Length >= 2,
                $"Expected at least 2 backup files in '{backupsDir}'; found {files.Length}.");

            // Cleanup: close the dialog, then the app.
            UiTestHelpers.TryInvokeWithFallback(
                dialog.FindFirstDescendant(cf => cf.ByAutomationId("CloseDatabaseWindowButton"))!);
            Thread.Sleep(400);

            UiTestHelpers.CloseAppWindow(mainWindow, process);
            Assert.True(process.WaitForExit(TimeSpan.FromSeconds(20)),
                "Desktop process did not exit after closing the main window.");
        }
        finally
        {
            UiTestHelpers.KillIfRunning(process);
        }
    }

    private static Window? WaitForWindow(Process process, UIA3Automation automation, string titleContains, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            var found = UiTestHelpers.FindProcessWindowByTitle(process, automation, titleContains);
            if (found != null) return found;
            Thread.Sleep(200);
        }
        return null;
    }
}
