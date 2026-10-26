using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Patterns;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Xunit;
using Xunit.Abstractions;

namespace KitchenInventory.Desktop.Tests;

public static class UiTestHelpers
{
    public static string GetDesktopExePath()
    {
        var repoRoot = GetRepoRoot();
        var exe = Path.Combine(repoRoot, "src", "KitchenInventory.Desktop", "bin", "Release", "net8.0-windows", "KitchenInventory.Desktop.exe");
        if (!File.Exists(exe))
        {
            throw new FileNotFoundException($"Desktop exe not found at {exe}. Ensure Release build ran before tests.");
        }
        return exe;
    }

    public static string GetRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "InventoryApp.sln")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }

    public static Window? TryGetMainWindow(Application app, UIA3Automation automation)
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

    public static Window? WaitForDialog(Window parentWindow, string titleContains, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                // Primary: check modal windows owned by the parent
                var modal = parentWindow.ModalWindows?.FirstOrDefault(w =>
                    !string.IsNullOrEmpty(w.Title) &&
                    w.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase));
                if (modal != null)
                {
                    return modal;
                }

                // Fallback: search all top-level windows in the same process (some frameworks don't expose ModalWindows reliably)
                try
                {
                    var automation = parentWindow.Automation;
                    var desktop = automation.GetDesktop();
                    var pid = parentWindow.Properties.ProcessId.ValueOrDefault;
                    var topLevel = desktop.FindAllDescendants(cf => cf.ByProcessId(pid).And(cf.ByControlType(ControlType.Window)));
                    var byName = topLevel.FirstOrDefault(w => !string.IsNullOrEmpty(w.Name) && w.Name.Contains(titleContains, StringComparison.OrdinalIgnoreCase));
                    if (byName != null)
                    {
                        return byName.AsWindow();
                    }

                    // If not matched by Name, attempt casting to Window and compare Title
                    var byTitle = topLevel
                        .Select(e => {
                            try { return e.AsWindow(); } catch { return null; }
                        })
                        .Where(w => w != null)
                        .FirstOrDefault(w => !string.IsNullOrEmpty(w!.Title) && w!.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase));
                    if (byTitle != null)
                    {
                        return byTitle!;
                    }
                }
                catch { }
            }
            catch { }
            Thread.Sleep(100);
        }
        return null;
    }

    public static void CloseDialog(Window dialog, string buttonText)
    {
        var closeBtn = dialog.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.Button).And(cf.ByName(buttonText)))?.AsButton();
        Assert.NotNull(closeBtn);
        closeBtn!.Invoke();
        Thread.Sleep(200);
    }

    /// <summary>Reads the text shown in the main window's status bar (empty when not found).</summary>
    public static string GetStatusText(Window mainWindow)
    {
        try
        {
            var statusBar = mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.StatusBar));
            if (statusBar == null) return string.Empty;

            var text = statusBar.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)).FirstOrDefault();
            return (text?.Name ?? string.Empty).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Opens the Add Item dialog, fills name/quantity/unit and submits it. The dialog
    /// closes on success, so callers can then wait for the row to appear in the grid.
    /// </summary>
    public static void AddItem(Window mainWindow, string name, string quantity, string unit, ITestOutputHelper? output = null)
    {
        ClickActionByName(mainWindow, "Add item", output);
        Thread.Sleep(300);

        var dialog = WaitForDialog(mainWindow, "Add New Item", TimeSpan.FromSeconds(15));
        Assert.True(dialog != null, "Add Item dialog did not appear.");

        var nameBox = dialog!.FindFirstDescendant(cf => cf.ByAutomationId("ItemNameTextBox"))?.AsTextBox();
        var qtyBox = dialog.FindFirstDescendant(cf => cf.ByAutomationId("QuantityTextBox"))?.AsTextBox();
        var unitBox = dialog.FindFirstDescendant(cf => cf.ByAutomationId("UnitTextBox"))?.AsTextBox();
        if (nameBox == null || qtyBox == null || unitBox == null)
        {
            var edits = dialog.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit))
                              .Select(e => e.AsTextBox())
                              .Where(tb => tb != null)
                              .ToList();
            nameBox ??= edits.ElementAtOrDefault(0);
            qtyBox ??= edits.ElementAtOrDefault(1);
            unitBox ??= edits.ElementAtOrDefault(2);
        }

        Assert.True(nameBox != null && qtyBox != null && unitBox != null,
            "Could not locate the Add Item dialog text boxes.");

        nameBox!.Enter(name);
        Thread.Sleep(100);
        qtyBox!.Enter(quantity);
        Thread.Sleep(100);
        unitBox!.Enter(unit);
        Thread.Sleep(150);

        var addBtn = dialog.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmAddItemButton"))
                  ?? dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Add")));
        Assert.True(addBtn != null, "Could not locate the Add button in the Add Item dialog.");
        TryInvokeWithFallback(addBtn!);
        Thread.Sleep(400);
    }

    /// <summary>
    /// Waits until a row whose cell text contains <paramref name="contains"/> appears in
    /// the given grid. WPF data grids expose rows as <c>DataItem</c> control types.
    /// </summary>
    public static bool WaitForGridRow(Window mainWindow, string gridAutomationId, string contains, TimeSpan timeout)
    {
        var grid = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId(gridAutomationId));
        if (grid == null) return false;

        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var rows = grid.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem));
                foreach (var row in rows)
                {
                    var cells = row.FindAllDescendants(cf => cf.ByControlType(ControlType.Text).Or(cf.ByControlType(ControlType.Edit)));
                    foreach (var cell in cells)
                    {
                        var txt = ReadCellText(cell);
                        if (!string.IsNullOrEmpty(txt) && txt.Contains(contains, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            Thread.Sleep(200);
        }
        return false;
    }

    private static string ReadCellText(AutomationElement cell)
    {
        try
        {
            var txt = cell.AsLabel()?.Text ?? cell.AsTextBox()?.Text ?? cell.Name ?? string.Empty;
            if (string.IsNullOrEmpty(txt) && cell.Patterns.Value.IsSupported)
            {
                txt = cell.Patterns.Value.Pattern.Value ?? string.Empty;
            }
            return (txt ?? string.Empty).Trim();
        }
        catch
        {
            return string.Empty;
        }
    }

    public static bool TryOpenToolbarOverflow(Window mainWindow, ITestOutputHelper? output = null)
    {
        // The "More" drawer toggles open and closed on each press, so opening it twice
        // would leave it shut. Collapsed WPF elements are absent from the automation
        // tree, so the drawer's own "Save changes" button being present means it is
        // already open and there is nothing to do.
        try
        {
            if (mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SaveChangesButton")) != null)
            {
                return true;
            }
        }
        catch { }

        try
        {
            var directOverflow = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("OverflowButton"))?.AsButton();
            if (directOverflow != null && directOverflow.IsEnabled)
            {
                output?.WriteLine($"Attempting to open toolbar overflow via element: Name='{directOverflow.Name}', Id='{directOverflow.AutomationId}', Type='{directOverflow.ControlType}'");
                TryInvokeWithFallback(directOverflow);
                Thread.Sleep(400);
                try { DebugAllButtons(mainWindow, output); } catch { }
                return true;
            }

            var toolbars = mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.ToolBar));
            foreach (var tb in toolbars)
            {
                var tbOverflow = tb.FindFirstDescendant(cf => cf.ByAutomationId("OverflowButton"))?.AsButton();
                if (tbOverflow != null && tbOverflow.IsEnabled)
                {
                    output?.WriteLine($"Attempting to open toolbar overflow via toolbar element: Name='{tbOverflow.Name}', Id='{tbOverflow.AutomationId}', Type='{tbOverflow.ControlType}'");
                    TryInvokeWithFallback(tbOverflow);
                    Thread.Sleep(400);
                    try { DebugAllButtons(mainWindow, output); } catch { }
                    return true;
                }

                var candidates = tb.FindAllDescendants(cf =>
                    cf.ByControlType(ControlType.Button)
                      .Or(cf.ByControlType(ControlType.SplitButton))
                      .Or(cf.ByControlType(ControlType.MenuItem)));

                foreach (var el in candidates)
                {
                    var name = el.Name ?? string.Empty;
                    var id = el.AutomationId ?? string.Empty;
                    if (name.Contains("Overflow", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("More", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("…") || name.Contains("⋯")
                        || name.Contains(">>")
                        || id.Contains("Overflow", StringComparison.OrdinalIgnoreCase)
                        || id.Contains("More", StringComparison.OrdinalIgnoreCase))
                    {
                        output?.WriteLine($"Attempting to open toolbar overflow via element: Name='{name}', Id='{id}', Type='{el.ControlType}'");
                        TryInvokeWithFallback(el);
                        Thread.Sleep(400);
                        try { DebugAllButtons(mainWindow, output); } catch { }
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            output?.WriteLine($"TryOpenToolbarOverflow encountered exception: {ex.Message}");
        }
        return false;
    }

    public static void DebugAllButtons(Window mainWindow, ITestOutputHelper? output = null)
    {
        try
        {
            output?.WriteLine("== DEBUG: All buttons in main window ==");
            var buttons = mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
            foreach (var btn in buttons)
            {
                output?.WriteLine($"Button: '{btn.Name}' | Id: '{btn.AutomationId}' | Enabled: {btn.IsEnabled} | Visible: {btn.IsOffscreen == false}");
            }

            output?.WriteLine("== DEBUG: All menuitems in main window ==");
            var menuItems = mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem));
            foreach (var mi in menuItems)
            {
                output?.WriteLine($"MenuItem: '{mi.Name}' | Id: '{mi.AutomationId}' | Enabled: {mi.IsEnabled} | Visible: {mi.IsOffscreen == false}");
            }

            output?.WriteLine("== DEBUG: All buttons on desktop ==");
            var desktop = mainWindow.Automation?.GetDesktop();
            if (desktop != null)
            {
                var desktopButtons = desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
                foreach (var btn in desktopButtons.Take(20))
                {
                    output?.WriteLine($"Desktop Button: '{btn.Name}' | Id: '{btn.AutomationId}' | Enabled: {btn.IsEnabled}");
                }

                var desktopMenuItems = desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem));
                foreach (var mi in desktopMenuItems.Take(20))
                {
                    output?.WriteLine($"Desktop MenuItem: '{mi.Name}' | Id: '{mi.AutomationId}' | Enabled: {mi.IsEnabled}");
                }
            }
        }
        catch (Exception ex)
        {
            output?.WriteLine($"DebugAllButtons failed: {ex.Message}");
        }
    }

    public static AutomationElement? TryFindActionElement(Window mainWindow, string actionName, ITestOutputHelper? output = null)
    {
        var candidates = new List<string> { actionName };
        if (actionName.Equals("Save", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add("Save Changes");
        }

        foreach (var name in candidates)
        {
            var byName = mainWindow.FindFirstDescendant(cf => cf.ByName(name))
                       ?? mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(name)))
                       ?? mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(name)));
            if (byName != null) return byName;

            // Fuzzy match in main window for Buttons/MenuItems whose Name contains the target text
            var approxInWindow = mainWindow.FindAllDescendants(cf =>
                cf.ByControlType(ControlType.Button).Or(cf.ByControlType(ControlType.MenuItem)));
            foreach (var el in approxInWindow)
            {
                var n = el.Name ?? string.Empty;
                if (!string.IsNullOrEmpty(n) && n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return el;
                }
            }
        }

        var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Save", "SaveChangesButton" },
            { "Save Changes", "SaveChangesButton" },
            { "Add", "AddItemButton" },
            { "Add item", "AddItemButton" },
            { "Delete", "DeleteItemButton" },
            { "Remove item", "DeleteItemButton" },
            { "Add Stock", "AddStockButton" },
            { "Take in stock", "AddStockButton" },
            { "Consume Stock", "ConsumeStockButton" },
            { "Take out stock", "ConsumeStockButton" },
            { "Adjust Stock", "AdjustStockButton" },
            { "Recount", "AdjustStockButton" },
            { "Manage Categories", "ManageCategoriesButton" }
        };

        foreach (var name in candidates)
        {
            if (idMap.TryGetValue(name, out var targetId))
            {
                var byId = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId(targetId));
                if (byId != null) return byId;
            }
        }

        var toolbars = mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.ToolBar));
        foreach (var tb in toolbars)
        {
            foreach (var name in candidates)
            {
                var inTb = tb.FindFirstDescendant(cf => cf.ByName(name))
                         ?? tb.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(name)))
                         ?? tb.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(name)));
                if (inTb != null) return inTb;

                // Fuzzy match inside toolbar
                var approxInTb = tb.FindAllDescendants(cf => cf.ByControlType(ControlType.Button)
                                                            .Or(cf.ByControlType(ControlType.MenuItem)));
                foreach (var el in approxInTb)
                {
                    var n = el.Name ?? string.Empty;
                    if (!string.IsNullOrEmpty(n) && n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return el;
                    }
                }

                if (idMap.TryGetValue(name, out var tbId))
                {
                    var inTbById = tb.FindFirstDescendant(cf => cf.ByAutomationId(tbId));
                    if (inTbById != null) return inTbById;
                }
            }
        }

        var popups = mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Menu)
                                                   .Or(cf.ByControlType(ControlType.Pane))
                                                   .Or(cf.ByClassName("ToolBarOverflowPanel")));
        foreach (var popup in popups)
        {
            foreach (var name in candidates)
            {
                var inPopup = popup.FindFirstDescendant(cf => cf.ByName(name))
                             ?? popup.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(name)))
                             ?? popup.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(name)));
                if (inPopup != null) return inPopup;

                // Fuzzy match in popups
                var approxInPopup = popup.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem)
                                                                   .Or(cf.ByControlType(ControlType.Button)));
                foreach (var el in approxInPopup)
                {
                    var n = el.Name ?? string.Empty;
                    if (!string.IsNullOrEmpty(n) && n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return el;
                    }
                }
            }
        }

        try
        {
            var automation = mainWindow.Automation;
            var desktop = automation.GetDesktop();
            var pid = mainWindow.Properties.ProcessId.ValueOrDefault;

            var hostPopups = desktop.FindAllDescendants(cf => cf.ByProcessId(pid)
                .And(cf.ByControlType(ControlType.Window)
                    .Or(cf.ByControlType(ControlType.Menu))
                    .Or(cf.ByClassName("PopupRoot"))
                    .Or(cf.ByClassName("ToolTip"))
                    .Or(cf.ByClassName("ToolBarOverflowPanel"))));

            foreach (var host in hostPopups)
            {
                foreach (var name in candidates)
                {
                    var found = host.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(name)))
                             ?? host.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(name)))
                             ?? host.FindFirstDescendant(cf => cf.ByName(name));
                    if (found != null) return found;

                    // Also try by AutomationId if we have a mapping
                    if (idMap.TryGetValue(name, out var autoId))
                    {
                        var byAutoId = host.FindFirstDescendant(cf => cf.ByAutomationId(autoId));
                        if (byAutoId != null) return byAutoId;
                    }

                    var allActions = host.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem)
                                                                .Or(cf.ByControlType(ControlType.Button)));
                    foreach (var el in allActions)
                    {
                        var n = el.Name ?? string.Empty;
                        if (n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return el;
                        }
                    }
                }
            }

            // Final attempt: search anywhere in the process by AutomationId
            foreach (var kv in idMap)
            {
                if (candidates.Any(c => string.Equals(c, kv.Key, StringComparison.OrdinalIgnoreCase)))
                {
                    var byIdAnywhere = desktop.FindFirstDescendant(cf => cf.ByProcessId(pid).And(cf.ByAutomationId(kv.Value)));
                    if (byIdAnywhere != null) return byIdAnywhere;
                }
            }
        }
        catch (Exception ex)
        {
            output?.WriteLine($"Desktop-level search failed: {ex.Message}");
        }

        output?.WriteLine($"Action '{actionName}' not found yet. Trying to open overflow and retry.");
        if (TryOpenToolbarOverflow(mainWindow, output))
        {
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(250));
            Thread.Sleep(300);
            var retryPopups = mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Menu)
                                                               .Or(cf.ByControlType(ControlType.Pane))
                                                               .Or(cf.ByClassName("ToolBarOverflowPanel")));
            foreach (var popup in retryPopups)
            {
                foreach (var name in candidates)
                {
                    var inPopup = popup.FindFirstDescendant(cf => cf.ByName(name))
                                 ?? popup.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(name)))
                                 ?? popup.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(name)));
                    if (inPopup != null) return inPopup;

                    // Fuzzy match in popups after opening overflow
                    var approxInPopup = popup.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem)
                                                                       .Or(cf.ByControlType(ControlType.Button)));
                    foreach (var el in approxInPopup)
                    {
                        var n = el.Name ?? string.Empty;
                        if (!string.IsNullOrEmpty(n) && n.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return el;
                        }
                    }
                }
            }

            try
            {
                var automation = mainWindow.Automation;
                var desktop = automation.GetDesktop();
                var pid = mainWindow.Properties.ProcessId.ValueOrDefault;
                var hostPopups = desktop.FindAllDescendants(cf => cf.ByProcessId(pid)
                    .And(cf.ByControlType(ControlType.Window)
                        .Or(cf.ByControlType(ControlType.Menu))
                        .Or(cf.ByClassName("PopupRoot"))
                        .Or(cf.ByClassName("ToolTip"))
                        .Or(cf.ByClassName("ToolBarOverflowPanel"))));

                foreach (var host in hostPopups)
                {
                    foreach (var name in candidates)
                    {
                        var found = host.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(name)))
                                 ?? host.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(name)))
                                 ?? host.FindFirstDescendant(cf => cf.ByName(name));
                        if (found != null) return found;
                    }
                }
            }
            catch (Exception ex)
            {
                output?.WriteLine($"Desktop-level retry failed: {ex.Message}");
            }
        }

        return null;
    }

    public static void TryInvokeWithFallback(AutomationElement el)
    {
        try
        {
            var btn = el.AsButton();
            if (btn != null)
            {
                btn.Invoke();
                return;
            }
        }
        catch { }

        try
        {
            var invoke = el.Patterns?.Invoke;
            if (invoke != null && invoke.IsSupported)
            {
                invoke.Pattern.Invoke();
                return;
            }
        }
        catch { }

        try
        {
            var legacy = el.Patterns?.LegacyIAccessible;
            if (legacy != null && legacy.IsSupported)
            {
                legacy.Pattern.DoDefaultAction();
                return;
            }
        }
        catch { }

        try
        {
            el.Click();
        }
        catch { }
    }

    public static void WaitForAndClickButtonByIdOrName(Window mainWindow, string automationIdOrName, TimeSpan? timeout = null, ITestOutputHelper? output = null)
    {
        var deadline = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(20));

        // Ensure window is visible and foreground
        try
        {
            var winPattern = mainWindow.Patterns?.Window;
            if (winPattern != null && winPattern.IsSupported)
            {
                var state = winPattern.Pattern.WindowVisualState.Value;
                if (state != WindowVisualState.Maximized)
                {
                    winPattern.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
                    Thread.Sleep(250);
                }
            }
        }
        catch { }
        try { mainWindow.Focus(); } catch { }
        try { mainWindow.SetForeground(); } catch { }

        AutomationElement? found = null;
        while (DateTime.UtcNow < deadline)
        {
            // 1) Exact AutomationId in window
            found = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId(automationIdOrName)
                .And(cf.ByControlType(ControlType.Button)));
            if (found != null) goto CLICK;

            // 2) Exact Name in window
            found = mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(automationIdOrName)));
            if (found != null) goto CLICK;

            // 3) Fuzzy Name contains in window
            var candidates = mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
            foreach (var el in candidates)
            {
                var n = el.Name ?? string.Empty;
                if (!string.IsNullOrEmpty(n) && n.IndexOf(automationIdOrName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = el;
                    break;
                }
            }
            if (found != null) goto CLICK;

            // 4) Look in popups/menus/overflow panels
            var popups = mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Menu)
                                                           .Or(cf.ByControlType(ControlType.Pane))
                                                           .Or(cf.ByClassName("ToolBarOverflowPanel")));
            foreach (var host in popups)
            {
                found = host.FindFirstDescendant(cf => cf.ByAutomationId(automationIdOrName).And(cf.ByControlType(ControlType.Button)))
                     ?? host.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(automationIdOrName)));
                if (found != null) goto CLICK;

                var approx = host.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
                foreach (var el in approx)
                {
                    var n = el.Name ?? string.Empty;
                    if (!string.IsNullOrEmpty(n) && n.IndexOf(automationIdOrName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found = el;
                        break;
                    }
                }
                if (found != null) goto CLICK;
            }

            // 5) Desktop-level search within the same process
            try
            {
                var automation = mainWindow.Automation;
                var desktop = automation.GetDesktop();
                var pid = mainWindow.Properties.ProcessId.ValueOrDefault;

                found = desktop.FindFirstDescendant(cf => cf.ByProcessId(pid)
                    .And(cf.ByAutomationId(automationIdOrName))
                    .And(cf.ByControlType(ControlType.Button)))
                     ?? desktop.FindFirstDescendant(cf => cf.ByProcessId(pid)
                         .And(cf.ByControlType(ControlType.Button))
                         .And(cf.ByName(automationIdOrName)));
                if (found != null) goto CLICK;

                var approx = desktop.FindAllDescendants(cf => cf.ByProcessId(pid).And(cf.ByControlType(ControlType.Button)));
                foreach (var el in approx)
                {
                    var n = el.Name ?? string.Empty;
                    if (!string.IsNullOrEmpty(n) && n.IndexOf(automationIdOrName, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        found = el;
                        break;
                    }
                }
                if (found != null) goto CLICK;
            }
            catch (Exception ex)
            {
                output?.WriteLine($"Desktop-level search error: {ex.Message}");
            }

            // Try opening overflow and retry
            var opened = TryOpenToolbarOverflow(mainWindow, output);
            if (!opened)
            {
                try { mainWindow.Focus(); } catch { }
                try { mainWindow.SetForeground(); } catch { }
            }

            Thread.Sleep(300);
            continue;

        CLICK:
            try
            {
                output?.WriteLine($"Clicking element via '{(found.AutomationId ?? found.Name)}' | Name='{found.Name}' | Id='{found.AutomationId}' | Type='{found.ControlType}'");
                TryInvokeWithFallback(found);
                Thread.Sleep(250);
                return;
            }
            catch (Exception ex)
            {
                output?.WriteLine($"Click failed: {ex.Message}");
                // try again in next loop
                found = null;
            }
        }

        // Final diagnostics
        output?.WriteLine($"FAILED to find button by Id or Name: '{automationIdOrName}'. Dumping diagnostics...");
        try { DebugAllButtons(mainWindow, output); } catch { }
        Assert.Fail($"Could not locate clickable button with AutomationId or Name matching '{automationIdOrName}'.");
    }

    public static void ClickActionByName(Window mainWindow, string actionName, ITestOutputHelper? output = null)
    {
        // Try to make all toolbar buttons visible by maximizing and focusing the window
        try
        {
            var winPattern = mainWindow.Patterns?.Window;
            if (winPattern != null && winPattern.IsSupported)
            {
                var state = winPattern.Pattern.WindowVisualState.Value;
                if (state != WindowVisualState.Maximized)
                {
                    winPattern.Pattern.SetWindowVisualState(WindowVisualState.Maximized);
                    Thread.Sleep(300);
                }
            }
        }
        catch { }
        try { mainWindow.Focus(); } catch { }
        try { mainWindow.SetForeground(); } catch { }

        var deadline = DateTime.UtcNow.AddSeconds(25);
        AutomationElement? el = null;
        while (DateTime.UtcNow < deadline)
        {
            el = TryFindActionElement(mainWindow, actionName, output);
            if (el != null)
            {
                output?.WriteLine($"Found action '{actionName}' via element: Name='{el.Name}', Id='{el.AutomationId}', Type='{el.ControlType}'");
                TryInvokeWithFallback(el);
                Thread.Sleep(300);
                return;
            }

            output?.WriteLine($"Action '{actionName}' not found yet. Trying to open overflow and retry.");
            var opened = TryOpenToolbarOverflow(mainWindow, output);
            if (!opened)
            {
                try { mainWindow.Focus(); } catch { }
                try { mainWindow.SetForeground(); } catch { }
                Thread.Sleep(300);
            }

            Thread.Sleep(500);
        }

        el = TryFindActionElement(mainWindow, actionName, output);
        Assert.True(el != null, $"Could not find action '{actionName}' in main window or overflow menus.");
        TryInvokeWithFallback(el!);
        Thread.Sleep(300);
    }

    // ------------------------------------------------------------------
    // Startup gating helpers
    //
    // On launch the app can show up to three modal dialogs before/around the
    // main window:
    //   1. Crash Reporting privacy consent (UI mode, first run only)
    //   2. Authentication gate: "Initial Admin Setup" (fresh DB) or "Sign In"
    //   3. "Create Categories" first-run prompt (shown after the main window
    //      loads when the DB has no user categories)
    // These helpers drive those real dialogs (no auth bypass) so tests can
    // reach the "Kitchen Inventory" main window.
    // ------------------------------------------------------------------

    public const string DefaultAdminUsername = "admin";
    public const string DefaultAdminPassword = "Admin123!";

    /// <summary>
    /// Builds an isolated SQLite connection string pointing at a brand new
    /// per-test database under artifacts/ui-test-dbs/{guid}/kitchen.e2e.db.
    /// </summary>
    public static string CreateIsolatedSqliteConnectionString()
    {
        var repo = GetRepoRoot();
        var dbDir = Path.Combine(repo, "artifacts", "ui-test-dbs", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, "kitchen.e2e.db");
        return $"Data Source={dbPath}";
    }

    /// <summary>
    /// Builds a ProcessStartInfo for the desktop app with the standard,
    /// isolated test environment (non-headless, SQLite, per-test DB).
    /// </summary>
    public static ProcessStartInfo CreateStartInfo(string sqliteConnectionString)
    {
        var exe = GetDesktopExePath();
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            Arguments = string.Empty,
        };

        psi.Environment["CI"] = "false";
        psi.Environment["INVENTORY_HEADLESS"] = "0";
        psi.Environment["INVENTORY_DB_PROVIDER"] = "sqlite";
        psi.Environment["INVENTORY_DB_CONNECTION"] = sqliteConnectionString;
        // The app's SQLite branch reads the "KitchenDb" connection string from .NET
        // configuration (INVENTORY_DB_CONNECTION is only honoured for Npgsql). Feed the
        // isolated per-test DB through the standard configuration env var so tests never
        // touch the developer's %LOCALAPPDATA%\InventoryApp\kitchen.db.
        psi.Environment["ConnectionStrings__KitchenDb"] = sqliteConnectionString;
        psi.Environment["Database__Provider"] = "Sqlite";
        // Keep ALL app state (daily log, preferences, default DB) beside the isolated
        // test DB under the repo. Without this the app would write to
        // %LOCALAPPDATA%\InventoryApp, which is outside the repo and is blocked in
        // sandboxed runs (surfacing as "access to the path ... is denied").
        psi.Environment["INVENTORY_DATA_DIR"] = ResolveDataDirectory(sqliteConnectionString);
        // Remove triggers that would change app behaviour during the test run.
        psi.Environment.Remove("INVENTORY_EXPORT_DIAGNOSTICS");
        psi.Environment.Remove("INVENTORY_CRASH_TEST");
        psi.Environment.Remove("INVENTORY_SUPPRESS_FIRST_RUN");
        return psi;
    }

    /// <summary>Launches the desktop app with the standard isolated environment.</summary>
    public static Process LaunchApp(string sqliteConnectionString)
    {
        var psi = CreateStartInfo(sqliteConnectionString);
        return Process.Start(psi)!;
    }

    /// <summary>
    /// Derives the per-test app-data directory (INVENTORY_DATA_DIR) from the connection
    /// string's DB folder so the app's log/preferences/default-DB tree stays beside the
    /// test database. Tests can use this to inspect files the app wrote.
    /// </summary>
    public static string ResolveDataDirectory(string sqliteConnectionString)
    {
        var repo = GetRepoRoot();
        string? dbDir = null;
        foreach (var part in sqliteConnectionString.Split(';'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase))
            {
                var trimmed = kv[1].Trim();
                if (trimmed.Length > 0) dbDir = Path.GetDirectoryName(trimmed);
                break;
            }
        }

        if (string.IsNullOrEmpty(dbDir))
        {
            dbDir = Path.Combine(repo, "artifacts", "ui-test-dbs", Guid.NewGuid().ToString("N"));
        }

        var dataDir = Path.Combine(dbDir, "appdata");
        Directory.CreateDirectory(dataDir);
        return dataDir;
    }

    /// <summary>Finds a top-level window owned by <paramref name="process"/> whose title contains the given text.</summary>
    public static Window? FindProcessWindowByTitle(Process process, UIA3Automation automation, string titleContains, bool excludeWin32ErrorDialogs = false)
    {
        try
        {
            var desktop = automation.GetDesktop();
            var windows = desktop.FindAllDescendants(cf => cf.ByProcessId(process.Id).And(cf.ByControlType(ControlType.Window)));
            foreach (var w in windows)
            {
                if (w == null) continue;
                var name = w.Name ?? string.Empty;
                if (name.Length == 0 || !name.Contains(titleContains, StringComparison.OrdinalIgnoreCase)) continue;

                Window win;
                try { win = w.AsWindow(); } catch { continue; }
                if (win == null) continue;

                if (excludeWin32ErrorDialogs)
                {
                    // The global exception handler shows a MessageBox whose caption is
                    // also "Kitchen Inventory"; it is a Win32 dialog (class #32770).
                    try
                    {
                        if (string.Equals(win.ClassName, "#32770", StringComparison.Ordinal)) continue;
                    }
                    catch { }
                }
                return win;
            }
        }
        catch { }
        return null;
    }

    private static void ClickButtonByNameOrId(Window dialog, string[] idsOrNames, ITestOutputHelper? output)
    {
        AutomationElement? btn = null;
        foreach (var key in idsOrNames)
        {
            btn = dialog.FindFirstDescendant(cf => cf.ByAutomationId(key).And(cf.ByControlType(ControlType.Button)))
               ?? dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(key)));
            if (btn != null) break;
        }

        if (btn == null)
        {
            foreach (var b in dialog.FindAllDescendants(cf => cf.ByControlType(ControlType.Button)))
            {
                var n = b.Name ?? string.Empty;
                if (idsOrNames.Any(key => n.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    btn = b;
                    break;
                }
            }
        }

        Assert.True(btn != null, $"Could not locate button matching [{string.Join(", ", idsOrNames)}] in dialog '{dialog.Title}'.");
        output?.WriteLine($"Clicking dialog button: Name='{btn!.Name}' Id='{btn.AutomationId}'");
        TryInvokeWithFallback(btn!);
    }

    private static void SetTextValue(AutomationElement? el, string value, ITestOutputHelper? output)
    {
        Assert.NotNull(el);
        try { el!.Focus(); } catch { }

        bool set = false;
        try
        {
            if (el!.Patterns.Value.IsSupported)
            {
                el.Patterns.Value.Pattern.SetValue(value);
                set = true;
            }
        }
        catch (Exception ex)
        {
            output?.WriteLine($"SetValue failed ({ex.Message}); falling back to keyboard input.");
            set = false;
        }

        if (!set)
        {
            try { el!.Click(); } catch { }
            try { FlaUI.Core.Input.Keyboard.Type(value); } catch { }
        }
        Thread.Sleep(150);
    }

    private static void FillAndSubmitAdminSetup(Window dialog, ITestOutputHelper? output)
    {
        try { dialog.SetForeground(); } catch { }
        try { dialog.Focus(); } catch { }

        var edits = dialog.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit)).ToList();
        output?.WriteLine($"Admin setup: {edits.Count} edit control(s) found.");

        var username = dialog.FindFirstDescendant(cf => cf.ByAutomationId("AdminUsernameTextBox")) ?? edits.ElementAtOrDefault(0);
        var password = dialog.FindFirstDescendant(cf => cf.ByAutomationId("AdminPasswordBox"))
                    ?? dialog.FindFirstDescendant(cf => cf.ByAutomationId("PasswordBox"))
                    ?? edits.ElementAtOrDefault(1);
        var confirm  = dialog.FindFirstDescendant(cf => cf.ByAutomationId("AdminConfirmPasswordBox"))
                    ?? dialog.FindFirstDescendant(cf => cf.ByAutomationId("ConfirmBox"))
                    ?? edits.ElementAtOrDefault(2);

        SetTextValue(username, DefaultAdminUsername, output);
        SetTextValue(password, DefaultAdminPassword, output);
        SetTextValue(confirm, DefaultAdminPassword, output);
        Thread.Sleep(150);

        ClickButtonByNameOrId(dialog, new[] { "CreateAdminButton", "Create Admin" }, output);
    }

    private static void FillAndSubmitLogin(Window dialog, ITestOutputHelper? output)
    {
        try { dialog.SetForeground(); } catch { }
        try { dialog.Focus(); } catch { }

        var edits = dialog.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit)).ToList();
        output?.WriteLine($"Sign in: {edits.Count} edit control(s) found.");

        var username = dialog.FindFirstDescendant(cf => cf.ByAutomationId("LoginUsernameTextBox")) ?? edits.ElementAtOrDefault(0);
        var password = dialog.FindFirstDescendant(cf => cf.ByAutomationId("LoginPasswordBox"))
                    ?? dialog.FindFirstDescendant(cf => cf.ByAutomationId("PasswordBox"))
                    ?? edits.ElementAtOrDefault(1);

        SetTextValue(username, DefaultAdminUsername, output);
        SetTextValue(password, DefaultAdminPassword, output);
        Thread.Sleep(150);

        ClickButtonByNameOrId(dialog, new[] { "SignInButton", "Sign In" }, output);
    }

    /// <summary>
    /// Completes all startup gating (privacy consent if present, then the auth
    /// gate, then the first-run "Create Categories" prompt if present) and
    /// returns the "Kitchen Inventory" main window.
    /// </summary>
    public static Window CompleteStartupGating(UIA3Automation automation, Process process, ITestOutputHelper? output = null, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(120));
        bool gateHandled = false;
        bool consentHandled = false;
        bool promptHandled = false;
        DateTime? mainSeenAt = null;

        while (DateTime.UtcNow < deadline)
        {
            if (process.HasExited)
            {
                throw new InvalidOperationException($"Desktop exited early with code {process.ExitCode} during startup gating.");
            }

            // 1) Crash Reporting privacy consent (first run only)
            if (!consentHandled)
            {
                var consent = FindProcessWindowByTitle(process, automation, "Crash Reporting");
                if (consent != null)
                {
                    output?.WriteLine("Startup gating: dismissing 'Crash Reporting' consent dialog.");
                    ClickButtonByNameOrId(consent, new[] { "NoThanksButton", "No thanks" }, output);
                    consentHandled = true;
                    Thread.Sleep(300);
                    continue;
                }
            }

            // 2) Authentication gate
            if (!gateHandled)
            {
                var setup = FindProcessWindowByTitle(process, automation, "Initial Admin Setup");
                if (setup != null)
                {
                    output?.WriteLine("Startup gating: completing 'Initial Admin Setup'.");
                    FillAndSubmitAdminSetup(setup, output);
                    gateHandled = true;
                    consentHandled = true;
                    Thread.Sleep(300);
                    continue;
                }

                var login = FindProcessWindowByTitle(process, automation, "Sign In");
                if (login != null)
                {
                    output?.WriteLine("Startup gating: completing 'Sign In'.");
                    FillAndSubmitLogin(login, output);
                    gateHandled = true;
                    consentHandled = true;
                    Thread.Sleep(300);
                    continue;
                }
            }

            // 3) Main window (+ first-run "Create Categories" prompt that appears after load)
            if (gateHandled)
            {
                var main = FindProcessWindowByTitle(process, automation, "Kitchen Inventory", excludeWin32ErrorDialogs: true);
                if (main != null)
                {
                    mainSeenAt ??= DateTime.UtcNow;

                    var prompt = FindProcessWindowByTitle(process, automation, "Create Categories");
                    if (prompt != null)
                    {
                        output?.WriteLine("Startup gating: dismissing 'Create Categories' prompt.");
                        ClickButtonByNameOrId(prompt, new[] { "No", "NoButton" }, output);
                        promptHandled = true;
                        Thread.Sleep(200);
                    }
                    else if (DateTime.UtcNow - mainSeenAt.Value > TimeSpan.FromSeconds(6))
                    {
                        // Give the post-load prompt a moment to appear before handing back control.
                        promptHandled = true;
                    }

                    if (promptHandled)
                    {
                        return main;
                    }
                }
            }

            Thread.Sleep(150);
        }

        throw new TimeoutException("Timed out waiting for the 'Kitchen Inventory' main window after startup gating.");
    }

    /// <summary>Kills the launched app process if it is still running (best effort).</summary>
    public static void KillIfRunning(Process process)
    {
        try
        {
            if (process != null && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch { }
    }

    private const uint WM_CLOSE = 0x0010;

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Closes the application's main window so the WPF app actually shuts down.
    /// FlaUI's <c>Window.Close()</c> (UI Automation WindowPattern.Close) does not reliably
    /// close this WPF window — the window stays open and the process never exits. Posting a
    /// real WM_CLOSE to the native window handle closes it and the app exits cleanly.
    /// </summary>
    public static void CloseAppWindow(Window window, Process process, ITestOutputHelper? output = null)
    {
        try
        {
            var hwnd = window.Properties.NativeWindowHandle.ValueOrDefault;
            if (hwnd != IntPtr.Zero && PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero))
            {
                return;
            }
            output?.WriteLine("PostMessage(WM_CLOSE) unavailable; falling back to FlaUI Close().");
        }
        catch (Exception ex)
        {
            output?.WriteLine($"WM_CLOSE post failed: {ex.Message}");
        }

        try { window.Close(); } catch { }
        try { process.CloseMainWindow(); } catch { }
    }
}