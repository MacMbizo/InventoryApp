using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Linq;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
    using FlaUI.Core.Input;
 using FlaUI.Core.WindowsAPI;
 using Xunit;
using Xunit.Abstractions;
using System.Collections.Generic;
using FlaUI.Core.Patterns;

namespace KitchenInventory.Desktop.Tests;

[Collection("UI")]
[Trait("Category", "UI")]
public class StockOperationUiTests : IDisposable
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

    private static Window? WaitForDialog(Window parentWindow, string titleContains, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                // Check modal windows owned by parent first
                var modal = parentWindow.ModalWindows?.FirstOrDefault(w => 
                    !string.IsNullOrEmpty(w.Title) && 
                    w.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase));
                if (modal != null)
                {
                    return modal;
                }
            }
            catch { /* ignore transient */ }
            Thread.Sleep(100);
        }
        return null;
    }

    private static void CloseDialog(Window dialog, string buttonText)
    {
        var closeBtn = dialog.FindFirstDescendant(cf =>
            cf.ByControlType(ControlType.Button).And(cf.ByName(buttonText)))?.AsButton();
        Assert.NotNull(closeBtn);
        closeBtn!.Invoke();
        Thread.Sleep(200); // Brief pause for dialog to close
    }

    private readonly ITestOutputHelper _output;
    
    public StockOperationUiTests(ITestOutputHelper output)
    {
        _output = output;
    }
    
    public void Dispose()
    {
        // Clean up any resources if needed
    }

    // Helper: Try to open any toolbar overflow / more options popup that might hide actions
    private bool TryOpenToolbarOverflow(Window mainWindow)
    {
        try
        {
            // 0) Direct known AutomationId if available on root
            var directOverflow = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId("OverflowButton"))?.AsButton();
            if (directOverflow != null && directOverflow.IsEnabled)
            {
                _output.WriteLine($"Attempting to open toolbar overflow via element: Name='{directOverflow.Name}', Id='{directOverflow.AutomationId}', Type='{directOverflow.ControlType}'");
                TryInvokeWithFallback(directOverflow);
                Thread.Sleep(400); // allow popup to render
                try { DebugAllButtons(mainWindow); } catch { }
                return true;
            }
    
            // 1) Scan ToolBars for likely overflow/more affordances
            var toolbars = mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.ToolBar));
            foreach (var tb in toolbars)
            {
                // Try known AutomationId within toolbar as well
                var tbOverflow = tb.FindFirstDescendant(cf => cf.ByAutomationId("OverflowButton"))?.AsButton();
                if (tbOverflow != null && tbOverflow.IsEnabled)
                {
                    _output.WriteLine($"Attempting to open toolbar overflow via toolbar element: Name='{tbOverflow.Name}', Id='{tbOverflow.AutomationId}', Type='{tbOverflow.ControlType}'");
                    TryInvokeWithFallback(tbOverflow);
                    Thread.Sleep(400);
                    try { DebugAllButtons(mainWindow); } catch { }
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
                        _output.WriteLine($"Attempting to open toolbar overflow via element: Name='{name}', Id='{id}', Type='{el.ControlType}'");
                        TryInvokeWithFallback(el);
                        Thread.Sleep(400);
                        try { DebugAllButtons(mainWindow); } catch { }
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"TryOpenToolbarOverflow encountered exception: {ex.Message}");
        }
        return false;
    }

    // Helper: Debug trace all buttons visible in window and desktop for troubleshooting
    private void DebugAllButtons(Window mainWindow)
    {
        try
        {
            _output.WriteLine("== DEBUG: All buttons in main window ==");
            var buttons = mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
            foreach (var btn in buttons)
            {
                _output.WriteLine($"Button: '{btn.Name}' | Id: '{btn.AutomationId}' | Enabled: {btn.IsEnabled} | Visible: {btn.IsOffscreen == false}");
            }

            _output.WriteLine("== DEBUG: All menuitems in main window ==");
            var menuItems = mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem));
            foreach (var mi in menuItems)
            {
                _output.WriteLine($"MenuItem: '{mi.Name}' | Id: '{mi.AutomationId}' | Enabled: {mi.IsEnabled} | Visible: {mi.IsOffscreen == false}");
            }

            _output.WriteLine("== DEBUG: All buttons on desktop ==");
            var desktop = mainWindow.Automation?.GetDesktop();
            if (desktop != null)
            {
                var desktopButtons = desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.Button));
                foreach (var btn in desktopButtons.Take(20)) // Limit to first 20 to avoid spam
                {
                    _output.WriteLine($"Desktop Button: '{btn.Name}' | Id: '{btn.AutomationId}' | Enabled: {btn.IsEnabled}");
                }

                var desktopMenuItems = desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem));
                foreach (var mi in desktopMenuItems.Take(20)) // Limit to first 20
                {
                    _output.WriteLine($"Desktop MenuItem: '{mi.Name}' | Id: '{mi.AutomationId}' | Enabled: {mi.IsEnabled}");
                }
            }
        }
        catch (Exception ex)
        {
            _output.WriteLine($"DebugAllButtons failed: {ex.Message}");
        }
    }

    // Helper: Find an actionable element (Button/MenuItem/SplitButton) by visible name across window and popups
    private AutomationElement? TryFindActionElement(Window mainWindow, string actionName)
    {
        // Normalize candidate names (include synonyms)
        var candidates = new List<string> { actionName };
        if (actionName.Equals("Save", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add("Save Changes");
        }

        // 1) Direct by Name within main window (Buttons/MenuItems)
        foreach (var name in candidates)
        {
            var byName = mainWindow.FindFirstDescendant(cf => cf.ByName(name))
                       ?? mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(name)))
                       ?? mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(name)));
            if (byName != null) return byName;
        }

        // 2) Known AutomationId mapping for common actions (search within the window first)
        var idMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Save", "SaveChangesButton" },
            { "Save Changes", "SaveChangesButton" },
            { "Add", "AddItemButton" },
            { "Add Stock", "AddStockButton" },
            { "Delete", "DeleteItemButton" },
            { "Consume Stock", "ConsumeStockButton" },
            { "Adjust Stock", "AdjustStockButton" }
        };

        foreach (var name in candidates)
        {
            if (idMap.TryGetValue(name, out var targetId))
            {
                var byId = mainWindow.FindFirstDescendant(cf => cf.ByAutomationId(targetId));
                if (byId != null) return byId;
            }
        }

        // 3) Search within ToolBars inside main window (by Name or by mapped AutomationId)
        var toolbars = mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.ToolBar));
        foreach (var tb in toolbars)
        {
            foreach (var name in candidates)
            {
                var inTb = tb.FindFirstDescendant(cf => cf.ByName(name))
                         ?? tb.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName(name)))
                         ?? tb.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName(name)));
                if (inTb != null) return inTb;

                if (idMap.TryGetValue(name, out var tbId))
                {
                    var inTbById = tb.FindFirstDescendant(cf => cf.ByAutomationId(tbId));
                    if (inTbById != null) return inTbById;
                }
            }
        }

        // 4) Try popups within the main window (overflow panels/menus)
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
            }
        }

        // 5) After opening overflow, search at Desktop-level for elements belonging to the same process
        try
        {
            var automation = mainWindow.Automation;
            var desktop = automation.GetDesktop();
            var pid = mainWindow.Properties.ProcessId.ValueOrDefault;

            // Look for a popup window/menu owned by the same process
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

                    // Fallback: enumerate and match by partial name (case-insensitive)
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
        }
        catch (Exception ex)
        {
            _output?.WriteLine($"Desktop-level search failed: {ex.Message}");
        }

        // 6) If not found, try to open overflow and retry once
        _output?.WriteLine($"Action '{actionName}' not found yet. Trying to open overflow and retry.");
        if (TryOpenToolbarOverflow(mainWindow))
        {
            Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(250));
            Thread.Sleep(300);
            // Retry inside window popups after opening overflow
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
                }
            }

            // Retry desktop-level once more
            try
            {
                var automation = mainWindow.Automation;
                var desktop = automation.GetDesktop();
                var pid = mainWindow.Properties.ProcessId.ValueOrDefault;
                var hostPopups = desktop.FindAllDescendants(cf => cf.ByProcessId(pid)
                    .And(cf.ByControlType(ControlType.Window)
                        .Or(cf.ByControlType(ControlType.Menu))
                        .Or(cf.ByClassName("PopupRoot"))
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
                _output?.WriteLine($"Desktop-level retry failed: {ex.Message}");
            }
        }

        return null;
    }

    // Helper: Safely invoke an element using multiple fallbacks
    private void TryInvokeWithFallback(AutomationElement el)
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

    // Helper: Click action by visible name, with overflow handling and retries
    private void ClickActionByName(Window mainWindow, string actionName)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        AutomationElement? el = null;
        while (DateTime.UtcNow < deadline)
        {
            el = TryFindActionElement(mainWindow, actionName);
            if (el != null)
            {
                _output.WriteLine($"Found action '{actionName}' via element: Name='{el.Name}', Id='{el.AutomationId}', Type='{el.ControlType}'");
                TryInvokeWithFallback(el);
                Thread.Sleep(300);
                return;
            }

            _output.WriteLine($"Action '{actionName}' not found yet. Trying to open overflow and retry.");
            var opened = TryOpenToolbarOverflow(mainWindow);
            if (!opened)
            {
                // As a fallback, try to bring window to front and wait
                try { mainWindow.Focus(); } catch { }
                try { mainWindow.SetForeground(); } catch { }
                Thread.Sleep(300);
            }

            Thread.Sleep(500);
        }

        // One last attempt before failing
        el = TryFindActionElement(mainWindow, actionName);
        Assert.True(el != null, $"Could not find action '{actionName}' in main window or overflow menus.");
        TryInvokeWithFallback(el!);
        Thread.Sleep(300);
    }
    // ... existing code ...
    
}
