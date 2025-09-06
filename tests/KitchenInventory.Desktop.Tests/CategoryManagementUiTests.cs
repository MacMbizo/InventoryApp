using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

[Collection("UI")]
[Trait("Category", "UI")]
public class CategoryManagementUiTests
{
    private static string GetDesktopExePath()
    {
        return UiTestHelpers.GetDesktopExePath();
    }

    private static string GetRepoRoot()
    {
        return UiTestHelpers.GetRepoRoot();
    }

    private static Window? TryGetMainWindow(Application app, UIA3Automation automation)
    {
        return UiTestHelpers.TryGetMainWindow(app, automation);
    }

    private static Window? WaitForDialog(Window parentWindow, string titleContains, TimeSpan timeout)
    {
        return UiTestHelpers.WaitForDialog(parentWindow, titleContains, timeout);
    }

    [StaFact]
    public void Desktop_Open_ManageCategories_Then_CloseDialog()
    {
        // Ensure headless mode is disabled for UI smoke
        Environment.SetEnvironmentVariable("INVENTORY_HEADLESS", "0");
        Environment.SetEnvironmentVariable("CI", "false");

        var exe = GetDesktopExePath();

        // Use isolated SQLite DB under repo artifacts to avoid polluting real user data
        var repo = GetRepoRoot();
        var dbDir = Path.Combine(repo, "artifacts", "ui-test-dbs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, "kitchen.e2e.db");
        var sqliteConn = $"Data Source={dbPath}";

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            Arguments = string.Empty,
        };

        // Environment isolation for child process
        psi.Environment["CI"] = "false";
        psi.Environment["INVENTORY_HEADLESS"] = "0";
        psi.Environment["INVENTORY_SUPPRESS_FIRST_RUN"] = "1";
        psi.Environment["INVENTORY_DB_PROVIDER"] = "sqlite";
        psi.Environment["INVENTORY_DB_CONNECTION"] = sqliteConn;
        psi.Environment.Remove("INVENTORY_EXPORT_DIAGNOSTICS");
        psi.Environment.Remove("INVENTORY_CRASH_TEST");

        using var process = Process.Start(psi)!;

        try { process.WaitForInputIdle(10000); } catch { }

        using var automation = new UIA3Automation();
        using var app = Application.Attach(process.Id);

        // Wait for the main window and verify
        Window? mainWindow = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                Assert.Fail($"Desktop exited early with code {process.ExitCode} before main window appeared.");
            }

            mainWindow = TryGetMainWindow(app, automation);
            if (mainWindow != null && !string.IsNullOrEmpty(mainWindow.Title))
            {
                break;
            }

            try
            {
                var candidate = app.GetAllTopLevelWindows(automation)
                                   .Select(a => a.AsWindow())
                                   .FirstOrDefault(w => !string.IsNullOrEmpty(w.Title));
                if (candidate != null)
                {
                    mainWindow = candidate;
                    break;
                }
            }
            catch { }

            Thread.Sleep(250);
        }

        Assert.NotNull(mainWindow);

        // Focus the main window to ensure toolbar input
        try { mainWindow!.Focus(); } catch { }
        try { mainWindow!.SetForeground(); } catch { }

        // Use helper that handles toolbar overflow and desktop-level search
        UiTestHelpers.ClickActionByName(mainWindow!, "Manage Categories");
        Thread.Sleep(300);

        // Wait for the Manage Categories dialog
        var dialog = WaitForDialog(mainWindow!, "Manage Categories", TimeSpan.FromSeconds(15));
        Assert.NotNull(dialog);

        // Basic sanity: dialog contains Add/Rename/Delete buttons and Close button
        var add = dialog!.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Add")));
        var rename = dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Rename")));
        var delete = dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Delete")));
        var close = dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Close")));
        Assert.NotNull(add);
        Assert.NotNull(rename);
        Assert.NotNull(delete);
        Assert.NotNull(close);

        // Close the dialog
        UiTestHelpers.TryInvokeWithFallback(close!);
        Thread.Sleep(200);

        // Ensure dialog closed (no modal windows left with that title)
        var stillThere = UiTestHelpers.WaitForDialog(mainWindow!, "Manage Categories", TimeSpan.FromSeconds(1));
        Assert.Null(stillThere);

        // Close the main window and ensure process exits
        try { mainWindow.Close(); } catch { }
        var exited = process.WaitForExit(TimeSpan.FromSeconds(20));
        Assert.True(exited, "Desktop process did not exit in allotted time after closing main window.");
        Assert.Equal(0, process.ExitCode);
    }

    [StaFact]
    public void Desktop_Add_Category_And_See_In_Grid()
    {
        Environment.SetEnvironmentVariable("INVENTORY_HEADLESS", "0");
        Environment.SetEnvironmentVariable("CI", "false");

        var exe = GetDesktopExePath();
        var repo = GetRepoRoot();
        var dbDir = Path.Combine(repo, "artifacts", "ui-test-dbs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, "kitchen.e2e.db");
        var sqliteConn = $"Data Source={dbPath}";

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            Arguments = string.Empty,
        };
        psi.Environment["CI"] = "false";
        psi.Environment["INVENTORY_HEADLESS"] = "0";
        psi.Environment["INVENTORY_SUPPRESS_FIRST_RUN"] = "1";
        psi.Environment["INVENTORY_DB_PROVIDER"] = "sqlite";
        psi.Environment["INVENTORY_DB_CONNECTION"] = sqliteConn;
        psi.Environment.Remove("INVENTORY_EXPORT_DIAGNOSTICS");

        using var process = Process.Start(psi)!;
        try { process.WaitForInputIdle(10000); } catch { }

        using var automation = new UIA3Automation();
        using var app = Application.Attach(process.Id);

        Window? mainWindow = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                Assert.Fail($"Desktop exited early with code {process.ExitCode} before main window appeared.");
            }
            mainWindow = TryGetMainWindow(app, automation);
            if (mainWindow != null && !string.IsNullOrEmpty(mainWindow.Title)) break;
            Thread.Sleep(200);
        }
        Assert.NotNull(mainWindow);

        try { mainWindow!.Focus(); } catch { }
        try { mainWindow!.SetForeground(); } catch { }

        UiTestHelpers.ClickActionByName(mainWindow!, "Manage Categories");
        Thread.Sleep(300);

        var dialog = WaitForDialog(mainWindow!, "Manage Categories", TimeSpan.FromSeconds(15));
        Assert.NotNull(dialog);

        // Click Add
        var add = dialog!.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Add")));
        Assert.NotNull(add);
        UiTestHelpers.TryInvokeWithFallback(add!);

        // Wait for prompt window titled "Enter category name"
        var prompt = WaitForDialog(dialog, "Enter new category name", TimeSpan.FromSeconds(10));
        Assert.NotNull(prompt);

         // Enter a unique category name
         var categoryName = $"E2E-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
         var input = prompt!.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit).And(cf.ByAutomationId("Input")))?.AsTextBox();
         Assert.NotNull(input);
         input!.Enter(categoryName);

        // Click OK
        var ok = prompt.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("OK")));
        Assert.NotNull(ok);
        UiTestHelpers.TryInvokeWithFallback(ok!);

        // Verify the new category appears in the grid within 10 seconds
        var gridEl = dialog.FindFirstDescendant(cf => cf.ByAutomationId("CategoriesGrid"));
        Assert.NotNull(gridEl);

        var appearDeadline = DateTime.UtcNow.AddSeconds(10);
        bool found = false;
        while (DateTime.UtcNow < appearDeadline)
        {
            try
            {
                // DataGrid rows are DataItem controls; scan their text descendants
                var rows = gridEl!.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem));
                foreach (var row in rows)
                {
                    var texts = row.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
                    foreach (var t in texts)
                    {
                        var txt = (t.AsLabel()?.Text ?? t.Name ?? string.Empty).Trim();
                        if (!string.IsNullOrEmpty(txt) && txt.Contains(categoryName, StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                            break;
                        }
                    }
                    if (found) break;
                }
                if (found) break;
            }
            catch { }
            Thread.Sleep(200);
        }

        Assert.True(found, $"Category '{categoryName}' did not appear in the grid within timeout.");

        // Close the dialog
        var close = dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Close")));
        Assert.NotNull(close);
        UiTestHelpers.TryInvokeWithFallback(close!);

        // Close app
        try { mainWindow!.Close(); } catch { }
        var exited = process.WaitForExit(TimeSpan.FromSeconds(20));
        Assert.True(exited, "Desktop process did not exit in allotted time after closing main window.");
        Assert.Equal(0, process.ExitCode);
    }
}