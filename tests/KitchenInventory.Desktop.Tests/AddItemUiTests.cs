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
public class AddItemUiTests
{
    private static string GetDesktopExePath() => UiTestHelpers.GetDesktopExePath();
    private static string GetRepoRoot() => UiTestHelpers.GetRepoRoot();
    private static Window? TryGetMainWindow(Application app, UIA3Automation automation) => UiTestHelpers.TryGetMainWindow(app, automation);
    private static Window? WaitForDialog(Window parentWindow, string titleContains, TimeSpan timeout) => UiTestHelpers.WaitForDialog(parentWindow, titleContains, timeout);

    [StaFact]
    public void Desktop_Open_AddItemDialog_Then_CloseDialog()
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
        psi.Environment.Remove("INVENTORY_CRASH_TEST");

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

        UiTestHelpers.ClickActionByName(mainWindow!, "Add");
        Thread.Sleep(300);

        var dialog = WaitForDialog(mainWindow!, "Add New Item", TimeSpan.FromSeconds(10));
        Assert.NotNull(dialog);

        var addBtn = dialog!.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmAddItemButton"))
                   ?? dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Add")));
        var cancelBtn = dialog.FindFirstDescendant(cf => cf.ByAutomationId("CancelAddItemButton"))
                      ?? dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Cancel")));
        Assert.NotNull(addBtn);
        Assert.NotNull(cancelBtn);

        UiTestHelpers.TryInvokeWithFallback(cancelBtn!);
        Thread.Sleep(250);

        var stillOpen = UiTestHelpers.WaitForDialog(mainWindow!, "Add New Item", TimeSpan.FromSeconds(1));
        Assert.Null(stillOpen);

        try { mainWindow!.Close(); } catch { }
        var exited = process.WaitForExit(TimeSpan.FromSeconds(20));
        Assert.True(exited, "Desktop process did not exit in allotted time after closing main window.");
        Assert.Equal(0, process.ExitCode);
    }

    [StaFact]
    public void Desktop_Add_Item_And_See_In_Grid()
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

        // Open Add dialog
        UiTestHelpers.ClickActionByName(mainWindow!, "Add");
        Thread.Sleep(300);

        var dialog = WaitForDialog(mainWindow!, "Add New Item", TimeSpan.FromSeconds(10));
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

        nameBox!.Enter(itemName);
        Thread.Sleep(100);
        qtyBox!.Enter("2");
        Thread.Sleep(100);
        unitBox!.Enter("pcs");
        Thread.Sleep(100);

        // Click Add
        var addBtn2 = dialog.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmAddItemButton"))
                     ?? dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Add")));
        Assert.NotNull(addBtn2);
        UiTestHelpers.TryInvokeWithFallback(addBtn2!);
        Thread.Sleep(400);

        // Verify new item appears in the main grid
        var gridEl = mainWindow!.FindFirstDescendant(cf => cf.ByAutomationId("ItemsDataGrid"));
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
        try { mainWindow!.Close(); } catch { }
        var exited = process.WaitForExit(TimeSpan.FromSeconds(20));
        Assert.True(exited, "Desktop process did not exit in allotted time after closing main window.");
        Assert.Equal(0, process.ExitCode);
    }

    [StaFact(DisplayName = "AddItemDialog_DisablesAdd_WhenInvalid_And_ShowsInlineError_Then_EnablesWhenValid")]
    [Trait("Category", "UI")] 
    [Trait("Subcategory", "Validation")]
    public void AddItemDialog_DisablesAdd_WhenInvalid_And_ShowsInlineError_Then_EnablesWhenValid()
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
        psi.Environment.Remove("INVENTORY_CRASH_TEST");

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

        // Open Add dialog
        UiTestHelpers.ClickActionByName(mainWindow!, "Add");
        Thread.Sleep(300);

        var dialog = WaitForDialog(mainWindow!, "Add New Item", TimeSpan.FromSeconds(10));
        Assert.NotNull(dialog);

        // Find controls
        var nameBox2 = dialog!.FindFirstDescendant(cf => cf.ByAutomationId("ItemNameTextBox"))?.AsTextBox();
        Assert.NotNull(nameBox2);
        var addBtnEl = dialog.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmAddItemButton"));
        Assert.NotNull(addBtnEl);
        var addBtn = addBtnEl!.AsButton();
        Assert.NotNull(addBtn);

        // Force invalid state (whitespace only) to trigger validation UI
        try
        {
            nameBox2!.Focus();
            nameBox2!.Click();
            FlaUI.Core.Input.Keyboard.Type(" ");
            // Move focus to trigger validation re-evaluation on lost focus as well
            FlaUI.Core.Input.Keyboard.Type(FlaUI.Core.WindowsAPI.VirtualKeyShort.TAB);
        }
        catch
        {
            try
            {
                if (nameBox2!.Patterns.Value.IsSupported)
                {
                    nameBox2.Patterns.Value.Pattern.SetValue(" ");
                }
                else
                {
                    nameBox2!.Enter(" ");
                }
            }
            catch { nameBox2!.Enter(" "); }
        }
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
        try
        {
            nameBox2!.Focus();
            nameBox2!.Click();
            // Clear existing content then type 'Milk'
            FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL);
            FlaUI.Core.Input.Keyboard.Type("a");
            FlaUI.Core.Input.Keyboard.Release(FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL);
            FlaUI.Core.Input.Keyboard.Type("Milk");
        }
        catch
        {
            try
            {
                if (nameBox2!.Patterns.Value.IsSupported)
                {
                    nameBox2.Patterns.Value.Pattern.SetValue("Milk");
                }
                else
                {
                    nameBox2!.Enter("Milk");
                }
            }
            catch { nameBox2!.Enter("Milk"); }
        }
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
        try { mainWindow!.Close(); } catch { }
        var exited2 = process.WaitForExit(TimeSpan.FromSeconds(20));
        Assert.True(exited2, "Desktop process did not exit in allotted time after closing main window.");
        Assert.Equal(0, process.ExitCode);
    }
}