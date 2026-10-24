using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

[Collection("UI")]
[Trait("Category", "UI")]
public class UiSmokeTests
{
    [StaFact]
    public void Desktop_Launches_ShowsMainWindow_And_Closes_Cleanly()
    {
        var connection = UiTestHelpers.CreateIsolatedSqliteConnectionString();
        using var process = UiTestHelpers.LaunchApp(connection);

        try
        {
            // Let the UI initialize to a message loop state
            try { process.WaitForInputIdle(10000); } catch { /* ignore on fast-start or non-GUI edge */ }

            using var automation = new UIA3Automation();
            var window = UiTestHelpers.CompleteStartupGating(automation, process);

            Assert.NotNull(window);
            Assert.Contains("Kitchen Inventory", window.Title, StringComparison.OrdinalIgnoreCase);

            // Close the window and wait for clean exit
            UiTestHelpers.CloseAppWindow(window, process);

            // Give the app some time to shutdown
            var exited = process.WaitForExit(TimeSpan.FromSeconds(20));
            Assert.True(exited, "Desktop process did not exit in allotted time after Close().");
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            UiTestHelpers.KillIfRunning(process);
        }
    }

    [StaFact]
    public void Desktop_Open_Diagnostics_From_Menu_And_Close()
    {
        var connection = UiTestHelpers.CreateIsolatedSqliteConnectionString();
        using var process = UiTestHelpers.LaunchApp(connection);

        try
        {
            try { process.WaitForInputIdle(10000); } catch { }

            using var automation = new UIA3Automation();
            var window = UiTestHelpers.CompleteStartupGating(automation, process);
            Assert.NotNull(window);

            // Locate the menu bar
            var menuBar = window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Menu))?.AsMenu();
            Assert.NotNull(menuBar);

            // Ensure main window is focused/foreground to receive menu interactions
            try { window.Focus(); } catch { }
            try { window.SetForeground(); } catch { }

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
            Thread.Sleep(300); // brief pause to allow submenu to render

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

            // Open Diagnostics
            diagnosticsItem!.Click();

            // Wait for Diagnostics window
            var diag = UiTestHelpers.WaitForDialog(window, "Diagnostics", TimeSpan.FromSeconds(10));
            Assert.NotNull(diag);

            // Close Diagnostics
            var close = diag!.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Close")));
            Assert.NotNull(close);
            UiTestHelpers.TryInvokeWithFallback(close!);

            // Close app
            UiTestHelpers.CloseAppWindow(window, process);
            var exited = process.WaitForExit(TimeSpan.FromSeconds(20));
            Assert.True(exited, "Desktop process did not exit in allotted time after closing main window.");
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            UiTestHelpers.KillIfRunning(process);
        }
    }
}
