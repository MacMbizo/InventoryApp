using System;
using System.Collections.Generic;
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

    public static bool TryOpenToolbarOverflow(Window mainWindow, ITestOutputHelper? output = null)
    {
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
            { "Add Stock", "AddStockButton" },
            { "Delete", "DeleteItemButton" },
            { "Consume Stock", "ConsumeStockButton" },
            { "Adjust Stock", "AdjustStockButton" },
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
}