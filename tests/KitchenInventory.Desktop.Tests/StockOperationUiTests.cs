using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;
using Xunit.Abstractions;
using System.Collections.Generic;
using FlaUI.Core.Patterns;

namespace KitchenInventory.Desktop.Tests;

[Collection("UI")]
[Trait("Category", "UI")]
public class StockOperationUiTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public StockOperationUiTests(ITestOutputHelper output)
    {
        _output = output;
    }

    public void Dispose()
    {
        // Clean up any resources if needed
    }

    // Note: Use UiTestHelpers directly for toolbar overflow, debug, action lookup, and clicks.
    // Example:
    // UiTestHelpers.TryOpenToolbarOverflow(mainWindow, _output);
    // UiTestHelpers.DebugAllButtons(mainWindow, _output);
    // var el = UiTestHelpers.TryFindActionElement(mainWindow, "Add Stock", _output);
    // UiTestHelpers.ClickActionByName(mainWindow, "Add Stock", _output);
    // UiTestHelpers.WaitForAndClickButtonByIdOrName(mainWindow, "SaveChangesButton", TimeSpan.FromSeconds(10), _output);

    /// <summary>
    /// Regression guard: ShowStockOperationDialogAsync read SelectedItem.Name *after*
    /// awaiting LoadAsync(), which clears Items and nulls SelectedItem. Every successful
    /// stock operation therefore threw a NullReferenceException that was logged as
    /// "Failed to open stock operation dialog" and shown in the status bar, even though
    /// the stock change itself had committed.
    /// </summary>
    [StaFact]
    public void Desktop_AddStock_CompletesAndReportsSuccess()
    {
        var connection = UiTestHelpers.CreateIsolatedSqliteConnectionString();
        using var process = UiTestHelpers.LaunchApp(connection);

        try
        {
            try { process.WaitForInputIdle(10000); } catch { }

            using var automation = new UIA3Automation();
            var mainWindow = UiTestHelpers.CompleteStartupGating(automation, process, _output);

            try { mainWindow.Focus(); } catch { }
            try { mainWindow.SetForeground(); } catch { }

            // Seed one item so the grid has a row to select (a fresh DB starts empty).
            var itemName = $"E2EStock-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            UiTestHelpers.AddItem(mainWindow, itemName, "20", "l", _output);
            Assert.True(UiTestHelpers.WaitForGridRow(mainWindow, "ItemsDataGrid", itemName, TimeSpan.FromSeconds(15)),
                $"Seeded item '{itemName}' did not appear in the items grid.");

            // Selecting the row sets SelectedItem, which enables the stock commands.
            var grid = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ItemsDataGrid"));
            Assert.NotNull(grid);
            var row = grid!.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem)).FirstOrDefault();
            Assert.NotNull(row);
            SelectRow(row!);
            Thread.Sleep(300);

            // The stock commands stay disabled until a row is selected; wait for that
            // rather than clicking a disabled button.
            var addStockBtn = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("AddStockButton"));
            Assert.True(addStockBtn != null, "The 'Add Stock' toolbar button was not found.");
            var enabledDeadline = DateTime.UtcNow.AddSeconds(10);
            while (!addStockBtn!.IsEnabled && DateTime.UtcNow < enabledDeadline)
            {
                Thread.Sleep(200);
            }
            Assert.True(addStockBtn.IsEnabled,
                "The 'Add Stock' button stayed disabled, so no grid row was selected.");
            _output.WriteLine("'Add Stock' button is enabled after selecting the row.");

            UiTestHelpers.ClickActionByName(mainWindow, "Add Stock", _output);

            Window? dialog = null;
            var dialogDeadline = DateTime.UtcNow.AddSeconds(15);
            while (dialog == null && DateTime.UtcNow < dialogDeadline)
            {
                dialog = UiTestHelpers.WaitForDialog(mainWindow, "Add Stock", TimeSpan.FromSeconds(1));
            }
            if (dialog == null)
            {
                DumpAppWindows(mainWindow);
            }
            Assert.True(dialog != null, "The 'Add Stock' dialog did not open.");

            var qty = dialog!.FindFirstDescendant(cf => cf.ByAutomationId("QuantityTextBox"))?.AsTextBox();
            Assert.NotNull(qty);
            qty!.Enter("10");
            Thread.Sleep(250);

            var execute = dialog.FindFirstDescendant(cf => cf.ByAutomationId("ExecuteButton"));
            Assert.NotNull(execute);
            UiTestHelpers.TryInvokeWithFallback(execute!);

            // Wait for the dialog to close once the operation has been applied.
            var closeDeadline = DateTime.UtcNow.AddSeconds(20);
            while (UiTestHelpers.WaitForDialog(mainWindow, "Add Stock", TimeSpan.FromSeconds(1)) != null
                   && DateTime.UtcNow < closeDeadline)
            {
                Thread.Sleep(200);
            }
            Assert.Null(UiTestHelpers.WaitForDialog(mainWindow, "Add Stock", TimeSpan.FromSeconds(1)));

            var status = UiTestHelpers.GetStatusText(mainWindow);
            _output.WriteLine($"Status after Add Stock: '{status}'");

            Assert.DoesNotContain("Failed to open", status, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Add Stock completed", status, StringComparison.OrdinalIgnoreCase);

            UiTestHelpers.CloseAppWindow(mainWindow, process);
            var exited = process.WaitForExit(TimeSpan.FromSeconds(20));
            Assert.True(exited, "Desktop process did not exit in allotted time after closing main window.");
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            UiTestHelpers.KillIfRunning(process);
        }
    }

    /// <summary>
    /// Selects a grid row. The selection pattern is preferred because a synthetic mouse
    /// click lands at the element's screen centre and is lost when the window is not
    /// foreground at that moment.
    /// </summary>
    private static void SelectRow(AutomationElement row)
    {
        try
        {
            if (row.Patterns.SelectionItem.IsSupported)
            {
                row.Patterns.SelectionItem.Pattern.Select();
                return;
            }
        }
        catch { }

        try { row.Click(); } catch { }
    }

    private void DumpAppWindows(Window mainWindow)
    {
        _output.WriteLine("Top-level windows in the app process:");
        try
        {
            var desktop = mainWindow.Automation.GetDesktop();
            var pid = mainWindow.Properties.ProcessId.ValueOrDefault;
            foreach (var w in desktop.FindAllDescendants(cf => cf.ByProcessId(pid).And(cf.ByControlType(ControlType.Window))))
            {
                _output.WriteLine($"  Name='{w.Name}' Class='{w.ClassName}'");
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"  (window dump failed: {ex.Message})");
        }
    }
}
