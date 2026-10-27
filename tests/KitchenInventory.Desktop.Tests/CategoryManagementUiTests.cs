using System;
using System.Diagnostics;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

[Collection("UI")]
[Trait("Category", "UI")]
public class CategoryManagementUiTests
{
    private static Window? WaitForDialog(Window parentWindow, string titleContains, TimeSpan timeout)
    {
        return UiTestHelpers.WaitForDialog(parentWindow, titleContains, timeout);
    }

    [StaFact]
    public void Desktop_Open_ManageCategories_Then_CloseDialog()
    {
        var connection = UiTestHelpers.CreateIsolatedSqliteConnectionString();
        using var process = UiTestHelpers.LaunchApp(connection);

        try
        {
            try { process.WaitForInputIdle(10000); } catch { }

            using var automation = new UIA3Automation();
            var mainWindow = UiTestHelpers.CompleteStartupGating(automation, process);

            // Focus the main window to ensure toolbar input
            try { mainWindow.Focus(); } catch { }
            try { mainWindow.SetForeground(); } catch { }

            // Use helper that handles toolbar overflow and desktop-level search
            UiTestHelpers.ClickActionByName(mainWindow, "Manage categories");
            Thread.Sleep(300);

            // Wait for the Manage Categories dialog
            var dialog = WaitForDialog(mainWindow, "Manage Categories", TimeSpan.FromSeconds(15));
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
            var stillThere = UiTestHelpers.WaitForDialog(mainWindow, "Manage Categories", TimeSpan.FromSeconds(1));
            Assert.Null(stillThere);

            // Close the main window and ensure process exits
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

    [StaFact]
    public void Desktop_Add_Category_And_See_In_Grid()
    {
        var connection = UiTestHelpers.CreateIsolatedSqliteConnectionString();
        using var process = UiTestHelpers.LaunchApp(connection);

        try
        {
            try { process.WaitForInputIdle(10000); } catch { }

            using var automation = new UIA3Automation();
            var mainWindow = UiTestHelpers.CompleteStartupGating(automation, process);

            try { mainWindow.Focus(); } catch { }
            try { mainWindow.SetForeground(); } catch { }

            UiTestHelpers.ClickActionByName(mainWindow, "Manage categories");
            Thread.Sleep(300);

            var dialog = WaitForDialog(mainWindow, "Manage Categories", TimeSpan.FromSeconds(15));
            Assert.NotNull(dialog);

            // Click Add
            var add = dialog!.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Add")));
            Assert.NotNull(add);
            UiTestHelpers.TryInvokeWithFallback(add!);

            // Wait for prompt window titled "Enter new category name"
            var prompt = WaitForDialog(dialog, "Enter new category name", TimeSpan.FromSeconds(10));
            Assert.NotNull(prompt);

            // Enter a unique category name
            var categoryName = $"E2E-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            var input = prompt!.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit).And(cf.ByAutomationId("Input")))?.AsTextBox();
            Assert.NotNull(input);
            UiTestHelpers.SetText(input, categoryName);

            // Click OK
            var ok = prompt.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("OK")));
            Assert.NotNull(ok);
            UiTestHelpers.TryInvokeWithFallback(ok!);

            // Verify the new category appears in the grid within 20 seconds
            var gridEl = dialog.FindFirstDescendant(cf => cf.ByAutomationId("CategoriesGrid"));
            Assert.NotNull(gridEl);

            var appearDeadline = DateTime.UtcNow.AddSeconds(25);
            bool found = false;
            while (DateTime.UtcNow < appearDeadline)
            {
                try
                {
                    // First, try to find by row name exposed via AutomationProperties.Name
                    var matchRow = gridEl!.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataItem).And(cf.ByName(categoryName)));
                    if (matchRow != null)
                    {
                        found = true;
                        break;
                    }

                    // Prefer scanning DataItem rows and both Text/Edit descendants (accounting for edit-mode cells)
                    var rows = gridEl!.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem));
                    foreach (var row in rows)
                    {
                        var cells = row.FindAllDescendants(cf => cf.ByControlType(ControlType.Text).Or(cf.ByControlType(ControlType.Edit)));
                        foreach (var cell in cells)
                        {
                            string txt = string.Empty;
                            try
                            {
                                txt = cell.AsLabel()?.Text ?? cell.AsTextBox()?.Text ?? cell.Name ?? string.Empty;
                                if (string.IsNullOrEmpty(txt) && cell.Patterns.Value.IsSupported)
                                {
                                    txt = cell.Patterns.Value.Pattern.Value ?? string.Empty;
                                }
                            }
                            catch { }
                            txt = (txt ?? string.Empty).Trim();
                            if (!string.IsNullOrEmpty(txt) && txt.Contains(categoryName, StringComparison.OrdinalIgnoreCase))
                            {
                                found = true;
                                break;
                            }
                        }
                        if (found) break;
                    }

                    if (!found)
                    {
                        // Fallback: scan any text/edit under the grid in case template differs
                        var all = gridEl!.FindAllDescendants(cf => cf.ByControlType(ControlType.Text).Or(cf.ByControlType(ControlType.Edit)));
                        foreach (var el in all)
                        {
                            string txt = string.Empty;
                            try
                            {
                                txt = el.AsLabel()?.Text ?? el.AsTextBox()?.Text ?? el.Name ?? string.Empty;
                                if (string.IsNullOrEmpty(txt) && el.Patterns.Value.IsSupported)
                                {
                                    txt = el.Patterns.Value.Pattern.Value ?? string.Empty;
                                }
                            }
                            catch { }
                            txt = (txt ?? string.Empty).Trim();
                            if (!string.IsNullOrEmpty(txt) && txt.Contains(categoryName, StringComparison.OrdinalIgnoreCase))
                            {
                                found = true;
                                break;
                            }
                        }
                    }
                    if (found) break;
                }
                catch { }
                Thread.Sleep(250);
            }

            Assert.True(found, $"Category '{categoryName}' did not appear in the grid within timeout.");

            // Close the dialog
            var close = dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Close")));
            Assert.NotNull(close);
            UiTestHelpers.TryInvokeWithFallback(close!);

            // Close app
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
}
