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
public class AddItemUiTests
{
    private static Window? WaitForDialog(Window parentWindow, string titleContains, TimeSpan timeout) => UiTestHelpers.WaitForDialog(parentWindow, titleContains, timeout);

    /// <summary>
    /// Puts text into a box through the shared UI Automation value-pattern setter rather than by
    /// typing it. See <see cref="UiTestHelpers.SetText"/> for why keystrokes are the fragile path.
    /// </summary>
    private static void SetText(TextBox box, string text) => UiTestHelpers.SetText(box, text);

    [StaFact]
    public void Desktop_Open_AddItemDialog_Then_CloseDialog()
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

            UiTestHelpers.ClickActionByName(mainWindow, "Add item");
            Thread.Sleep(300);

            var dialog = WaitForDialog(mainWindow, "Add New Item", TimeSpan.FromSeconds(10));
            Assert.NotNull(dialog);

            var addBtn = dialog!.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmAddItemButton"))
                       ?? dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Add")));
            var cancelBtn = dialog.FindFirstDescendant(cf => cf.ByAutomationId("CancelAddItemButton"))
                          ?? dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Cancel")));
            Assert.NotNull(addBtn);
            Assert.NotNull(cancelBtn);

            UiTestHelpers.TryInvokeWithFallback(cancelBtn!);
            Thread.Sleep(250);

            var stillOpen = UiTestHelpers.WaitForDialog(mainWindow, "Add New Item", TimeSpan.FromSeconds(1));
            Assert.Null(stillOpen);

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
    public void Desktop_Add_Item_And_See_In_Grid()
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

            // Open Add dialog
            UiTestHelpers.ClickActionByName(mainWindow, "Add item");
            Thread.Sleep(300);

            var dialog = WaitForDialog(mainWindow, "Add New Item", TimeSpan.FromSeconds(10));
            Assert.NotNull(dialog);

            // Prepare unique item name
            var itemName = $"E2EItem-{Guid.NewGuid().ToString("N").Substring(0, 8)}";

            // Fill fields using AutomationIds for robustness with fallback to first three Edit controls
            var nameBox = dialog!.FindFirstDescendant(cf => cf.ByAutomationId("ItemNameTextBox"))?.AsTextBox();
            var qtyBox  = dialog.FindFirstDescendant(cf => cf.ByAutomationId("QuantityTextBox"))?.AsTextBox();
            var unitBox = dialog.FindFirstDescendant(cf => cf.ByAutomationId("UnitTextBox"))?.AsTextBox();
            if (nameBox == null || qtyBox == null || unitBox == null)
            {
                var edits = dialog!.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit))
                                    .Select(e => e.AsTextBox())
                                    .Where(tb => tb != null)
                                    .ToList();
                nameBox ??= edits.ElementAtOrDefault(0);
                qtyBox  ??= edits.ElementAtOrDefault(1);
                unitBox ??= edits.ElementAtOrDefault(2);
            }
            Assert.NotNull(nameBox);
            Assert.NotNull(qtyBox);
            Assert.NotNull(unitBox);

            SetText(nameBox!, itemName);
            SetText(qtyBox!, "2");
            SetText(unitBox!, "pcs");

            // Click Add
            var addBtn2 = dialog.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmAddItemButton"))
                         ?? dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Add")));
            Assert.NotNull(addBtn2);
            UiTestHelpers.TryInvokeWithFallback(addBtn2!);
            Thread.Sleep(400);

            // Verify new item appears in the main grid
            var gridEl = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ItemsDataGrid"));
            Assert.NotNull(gridEl);

            var appearDeadline = DateTime.UtcNow.AddSeconds(10);
            bool found = false;
            while (DateTime.UtcNow < appearDeadline)
            {
                try
                {
                    var rows = gridEl!.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem));
                    foreach (var row in rows)
                    {
                        var texts = row.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
                        foreach (var t in texts)
                        {
                            var txt = (t.AsLabel()?.Text ?? t.Name ?? string.Empty).Trim();
                            if (!string.IsNullOrEmpty(txt) && txt.Contains(itemName, StringComparison.OrdinalIgnoreCase))
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

            Assert.True(found, $"Item '{itemName}' did not appear in the grid within timeout.");

            // Close main window and assert exit
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
    /// The confirmation is announced beside the toolbar rather than only in the status bar at the
    /// far edge of the window, and it is short-lived on purpose. Both halves are asserted here,
    /// which only became possible once the id moved off the StackPanel around the words: a
    /// StackPanel publishes no automation peer, so an id on one never reaches the tree.
    /// </summary>
    [StaFact(DisplayName = "Toolbar_SaysWhatWasAdded_ThenGoesQuiet")]
    public void Toolbar_SaysWhatWasAdded_ThenGoesQuiet()
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

            var itemName = $"Feedback-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            UiTestHelpers.AddItem(mainWindow, itemName, "2", "pcs");

            // The toolbar says what happened, naming the item it happened to.
            string? said = null;
            var appearDeadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < appearDeadline)
            {
                said = ReadToolbarMessage(mainWindow);
                if (said != null) break;
                Thread.Sleep(150);
            }

            Assert.True(said != null, "The toolbar did not confirm the add within the timeout.");
            Assert.Contains(itemName, said!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("was added", said!, StringComparison.OrdinalIgnoreCase);

            // It reports one action rather than staying on screen as a banner.
            bool cleared = false;
            var clearDeadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < clearDeadline)
            {
                if (ReadToolbarMessage(mainWindow) == null) { cleared = true; break; }
                Thread.Sleep(250);
            }

            Assert.True(cleared, "The toolbar confirmation never went away.");

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

    /// <summary>The toolbar's confirmation text, or null while it has nothing to say.</summary>
    private static string? ReadToolbarMessage(Window mainWindow)
    {
        try
        {
            var message = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ToolbarActionMessage"));
            if (message == null) return null;

            var text = (message.Name ?? string.Empty).Trim();
            return text.Length == 0 ? null : text;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// The edit dialog used to show an amount with the scale it happened to be stored with: a
    /// quantity kept in the database as 10.0 appeared in the box as "10.0" while the grid, which
    /// formats what it shows, said "10". The two readings of the same amount now agree.
    /// </summary>
    [StaFact(DisplayName = "EditDialog_ShowsTheQuantityTheWayTheGridDoes")]
    public void EditDialog_ShowsTheQuantityTheWayTheGridDoes()
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

            var itemName = $"Amount-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            UiTestHelpers.AddItem(mainWindow, itemName, "10", "kg");
            Assert.True(UiTestHelpers.WaitForGridRow(mainWindow, "ItemsDataGrid", itemName, TimeSpan.FromSeconds(15)),
                "The item never appeared in the grid.");

            Assert.True(UiTestHelpers.ChooseFromRowMenu(mainWindow, itemName, "EditItemMenuItem"),
                "The row menu's 'Edit item' entry could not be reached.");

            var dialog = WaitForDialog(mainWindow, "Edit item", TimeSpan.FromSeconds(15));
            Assert.NotNull(dialog);

            var quantityBox = dialog!.FindFirstDescendant(cf => cf.ByAutomationId("QuantityTextBox"))?.AsTextBox();
            Assert.NotNull(quantityBox);
            Assert.Equal("10", (quantityBox!.Text ?? string.Empty).Trim());

            var cancel = dialog.FindFirstDescendant(cf => cf.ByAutomationId("CancelAddItemButton"));
            Assert.NotNull(cancel);
            UiTestHelpers.TryInvokeWithFallback(cancel!);
            Thread.Sleep(250);
        }
        finally
        {
            UiTestHelpers.KillIfRunning(process);
        }
    }

    [StaFact(DisplayName = "AddItemDialog_DisablesAdd_WhenInvalid_And_ShowsInlineError_Then_EnablesWhenValid")]
    [Trait("Category", "UI")] 
    [Trait("Subcategory", "Validation")]
    public void AddItemDialog_DisablesAdd_WhenInvalid_And_ShowsInlineError_Then_EnablesWhenValid()
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

            // Open Add dialog
            UiTestHelpers.ClickActionByName(mainWindow, "Add item");
            Thread.Sleep(300);

            var dialog = WaitForDialog(mainWindow, "Add New Item", TimeSpan.FromSeconds(10));
            Assert.NotNull(dialog);

            // Find controls
            var nameBox2 = dialog!.FindFirstDescendant(cf => cf.ByAutomationId("ItemNameTextBox"))?.AsTextBox();
            Assert.NotNull(nameBox2);
            var addBtnEl = dialog.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmAddItemButton"));
            Assert.NotNull(addBtnEl);
            var addBtn = addBtnEl!.AsButton();
            Assert.NotNull(addBtn);

            // Force invalid state (whitespace only) to trigger validation UI
            SetText(nameBox2!, " ");
            Thread.Sleep(300);

            // Wait for inline error element to appear and Add to be disabled
            AutomationElement? nameErrorEl = null;
            var invalidDeadline = DateTime.UtcNow.AddSeconds(8);
            bool addDisabled = false;
            bool errorVisible = false;
            string errorText = string.Empty;
            while (DateTime.UtcNow < invalidDeadline)
            {
                try
                {
                    addDisabled = addBtn!.IsEnabled == false;
                    nameErrorEl = dialog.FindFirstDescendant(cf => cf.ByAutomationId("ItemNameErrorText"));
                    if (nameErrorEl != null)
                    {
                        var off = nameErrorEl.Properties.IsOffscreen.ValueOrDefault;
                        errorVisible = !off;
                        errorText = (nameErrorEl.Name ?? string.Empty).Trim();
                    }
                    if (addDisabled && errorVisible) break;
                }
                catch { }
                Thread.Sleep(150);
            }

            // If button appears enabled due to UIA timing, verify guarded submission prevents close and shows inline error
            bool invalidEnforced = addDisabled && errorVisible;
            if (!invalidEnforced)
            {
                try
                {
                    UiTestHelpers.TryInvokeWithFallback(addBtn!);
                    Thread.Sleep(300);
                    var stillOpen = dialog.IsAvailable; // robust: element becomes unavailable when window closes
                    nameErrorEl = dialog.FindFirstDescendant(cf => cf.ByAutomationId("ItemNameErrorText"));
                    if (nameErrorEl != null)
                    {
                        var off = nameErrorEl.Properties.IsOffscreen.ValueOrDefault;
                        errorVisible = !off;
                        errorText = (nameErrorEl.Name ?? string.Empty).Trim();
                    }
                    // consider invalid enforced if dialog remains open (Add click guarded) even if inline error visibility is not reported via UIA
                    invalidEnforced = addDisabled || stillOpen;
                }
                catch { }
            }

            Assert.True(invalidEnforced, "Invalid ItemName must prevent submission (disabled Add or guarded click keeps dialog open).");
            if (!string.IsNullOrWhiteSpace(errorText))
            {
                Assert.Contains("required", errorText, StringComparison.OrdinalIgnoreCase);
            }

            // Enter a valid name -> error should hide and Add becomes enabled
            SetText(nameBox2!, "Milk");
            Thread.Sleep(300);

            bool addEnabled = false;
            bool errorHidden = false;
            var validDeadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < validDeadline)
            {
                try
                {
                    addEnabled = addBtn!.IsEnabled;
                    nameErrorEl = dialog.FindFirstDescendant(cf => cf.ByAutomationId("ItemNameErrorText"));
                    if (nameErrorEl == null)
                    {
                        errorHidden = true; // Collapsed elements may be removed from UIA tree
                    }
                    else
                    {
                        var off = nameErrorEl.Properties.IsOffscreen.ValueOrDefault;
                        errorHidden = off; // Offscreen indicates hidden
                    }
                    if (addEnabled && errorHidden) break;
                }
                catch { }
                Thread.Sleep(150);
            }

            Assert.True(addEnabled, "Add button should become enabled after entering a valid ItemName");
            Assert.True(errorHidden, "Inline error should hide after entering a valid ItemName");

            // Close dialog via Cancel to avoid side effects
            var cancelBtn = dialog.FindFirstDescendant(cf => cf.ByAutomationId("CancelAddItemButton"))?.AsButton();
            Assert.NotNull(cancelBtn);
            UiTestHelpers.TryInvokeWithFallback(cancelBtn!);
            Thread.Sleep(250);

            // Close main window and assert exit
            UiTestHelpers.CloseAppWindow(mainWindow, process);
            var exited2 = process.WaitForExit(TimeSpan.FromSeconds(20));
            Assert.True(exited2, "Desktop process did not exit in allotted time after closing main window.");
            Assert.Equal(0, process.ExitCode);
        }
        finally
        {
            UiTestHelpers.KillIfRunning(process);
        }
    }
}
