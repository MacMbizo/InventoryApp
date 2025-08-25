using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Linq;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

[Collection("UI")]
[Trait("Category", "UI")]
public class UiSmokeTests
{
    private static string GetDesktopExePath()
    {
        var repoRoot = GetRepoRoot();
        var exe = Path.Combine(repoRoot, "src", "KitchenInventory.Desktop", "bin", "Release", "net8.0-windows", "KitchenInventory.Desktop.exe");
        if (!File.Exists(exe))
        {
            throw new FileNotFoundException($"Desktop exe not found at {exe}. Ensure Release build ran before tests.");
        }
        return exe;
    }

    private static string GetRepoRoot()
    {
        // Walk up from current test directory to find InventoryApp.sln
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "InventoryApp.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    private static Window? TryGetMainWindow(Application app, UIA3Automation automation)
    {
        try
        {
            var win = app.GetMainWindow(automation);
            return win?.AsWindow();
        }
        catch
        {
            return null;
        }
    }

    [StaFact]
    public void Desktop_Launches_ShowsMainWindow_And_Closes_Cleanly()
    {
        // Ensure headless mode is disabled for UI smoke (in parent process as a fallback)
        Environment.SetEnvironmentVariable("INVENTORY_HEADLESS", "0");
        Environment.SetEnvironmentVariable("CI", "false");

        var exe = GetDesktopExePath();
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            Arguments = string.Empty,
        };

        // Ensure child process is not headless even if CI=true in the environment
        psi.Environment["CI"] = "false";
        psi.Environment["INVENTORY_HEADLESS"] = "0";
        psi.Environment.Remove("INVENTORY_EXPORT_DIAGNOSTICS");
        psi.Environment.Remove("INVENTORY_CRASH_TEST");

        using var process = Process.Start(psi)!;

        // Let the UI initialize to a message loop state
        try { process.WaitForInputIdle(10000); } catch { /* ignore on fast-start or non-GUI edge */ }

        using var automation = new UIA3Automation();
        using var app = Application.Attach(process.Id);

        // Wait for the main window and verify the title
        Window? window = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                Assert.True(false, $"Desktop process exited early with code {process.ExitCode} before main window appeared.");
            }

            // Try via GetMainWindow first
            window = TryGetMainWindow(app, automation);
            if (window != null && !string.IsNullOrEmpty(window.Title) && window.Title.Contains("Kitchen Inventory", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            // Fallback: search all top-level windows for expected title
            try
            {
                var candidate = app.GetAllTopLevelWindows(automation)
                                   .Select(a => a.AsWindow())
                                   .FirstOrDefault(w => !string.IsNullOrEmpty(w.Title) && w.Title.Contains("Kitchen Inventory", StringComparison.OrdinalIgnoreCase));
                if (candidate != null)
                {
                    window = candidate;
                    break;
                }
            }
            catch { /* ignore transient */ }

            Thread.Sleep(250);
        }

        Assert.NotNull(window);
        Assert.Contains("Kitchen Inventory", window!.Title, StringComparison.OrdinalIgnoreCase);

        // Close the window and wait for clean exit
        try { window.Close(); } catch { /* ignore if already closing */ }

        // Give the app some time to shutdown
        var exited = process.WaitForExit(TimeSpan.FromSeconds(20));
        Assert.True(exited, "Desktop process did not exit in allotted time after Close().");
        Assert.Equal(0, process.ExitCode);
    }

    [StaFact]
    public void Desktop_Open_Diagnostics_From_Menu_And_Close()
    {
        // Ensure headless mode is disabled for UI smoke (in parent process as a fallback)
        Environment.SetEnvironmentVariable("INVENTORY_HEADLESS", "0");
        Environment.SetEnvironmentVariable("CI", "false");

        var exe = GetDesktopExePath();
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            Arguments = string.Empty,
        };

        // Ensure child process is not headless even if CI=true in the environment
        psi.Environment["CI"] = "false";
        psi.Environment["INVENTORY_HEADLESS"] = "0";
        psi.Environment.Remove("INVENTORY_EXPORT_DIAGNOSTICS");
        psi.Environment.Remove("INVENTORY_CRASH_TEST");

        using var process = Process.Start(psi)!;
        try { process.WaitForInputIdle(10000); } catch { }

        using var automation = new UIA3Automation();
        using var app = Application.Attach(process.Id);

        // Wait for the main window (more generous timeout for first-launch migrations)
        Window? window = null;
        var deadline = DateTime.UtcNow.AddSeconds(40);
        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                Assert.True(false, $"Desktop process exited early with code {process.ExitCode} before main window appeared.");
            }

            window = TryGetMainWindow(app, automation);
            if (window != null && !string.IsNullOrEmpty(window.Title))
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
                    window = candidate;
                    break;
                }
            }
            catch { }

            Thread.Sleep(250);
        }
        Assert.NotNull(window);

        // Locate the menu bar
        var menuBar = window!.FindFirstDescendant(cf => cf.ByControlType(ControlType.Menu))?.AsMenu();
        Assert.NotNull(menuBar);

        // Find the Help menu item (supports both "Help" and "_Help")
        MenuItem? helpItem = null;
        foreach (var item in menuBar!.Items)
        {
            var name = item.Properties.Name?.Value ?? string.Empty;
            if (string.Equals(name.Replace("_", string.Empty), "Help", StringComparison.OrdinalIgnoreCase))
            {
                helpItem = item;
                break;
            }
        }
        Assert.NotNull(helpItem);

        // Open Help menu
        helpItem!.Click();
        Thread.Sleep(200); // brief pause to allow submenu to render

        // Find Diagnostics submenu item
        MenuItem? diagnosticsItem = null;
        foreach (var sub in helpItem.Items)
        {
            var name = sub.Properties.Name?.Value ?? string.Empty;
            if (string.Equals(name.Replace("_", string.Empty), "Diagnostics", StringComparison.OrdinalIgnoreCase))
            {
                diagnosticsItem = sub;
                break;
            }
        }
        Assert.NotNull(diagnosticsItem);

        diagnosticsItem!.Click();

        // Wait for Diagnostics window to appear
        AutomationElement? diagElement = null;
        var diagDeadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < diagDeadline)
        {
            if (process.HasExited)
            {
                Assert.True(false, $"Desktop process exited early with code {process.ExitCode} before diagnostics window appeared.");
            }

            try
            {
                diagElement = app.GetAllTopLevelWindows(automation)
                                  .FirstOrDefault(w => w.AsWindow().Title?.Contains("Diagnostics", StringComparison.OrdinalIgnoreCase) == true);
                if (diagElement != null) break;
            }
            catch { /* ignore transient*/ }
            Thread.Sleep(200);
        }
        Assert.NotNull(diagElement);

        var diagWindow = diagElement!.AsWindow();
        Assert.Contains("Diagnostics", diagWindow.Title, StringComparison.OrdinalIgnoreCase);

        // Close Diagnostics via the "Close" button
        var closeBtn = diagWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Close")))?.AsButton();
        Assert.NotNull(closeBtn);
        try { closeBtn!.Invoke(); } catch { }

        // Finally close the main window and ensure clean exit
        try { window.Close(); } catch { }
        var exited = process.WaitForExit(TimeSpan.FromSeconds(20));
        Assert.True(exited, "Desktop process did not exit in allotted time after closing windows.");
        Assert.Equal(0, process.ExitCode);
    }
}