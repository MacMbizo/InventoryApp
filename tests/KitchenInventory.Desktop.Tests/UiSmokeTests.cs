using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using KitchenInventory.Data;
using KitchenInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
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

    /// <summary>
    /// Choosing a category or a location in the filters narrows the list. Nothing covered the
    /// filters, and it is the filters that were reported: their closed boxes draw the object they
    /// hold, which falls back to its ToString() — "KitchenInventory.Domain.Entities.Category".
    /// That fallback is what <c>Category</c>, <c>Location</c> and <c>ReportOption</c> now answer
    /// with their name instead; the drawn text itself is not published to automation, so the
    /// wiring around it is what is asserted here.
    /// </summary>
    [StaFact(DisplayName = "Filters_NarrowTheListToTheChosenCategoryAndLocation")]
    public void Filters_NarrowTheListToTheChosenCategoryAndLocation()
    {
        const string categoryName = "Dry goods";
        const string locationName = "Store cupboard";

        var connection = UiTestHelpers.CreateIsolatedSqliteConnectionString();
        SeedFilterData(connection, categoryName, locationName);

        using var process = UiTestHelpers.LaunchApp(connection);

        try
        {
            try { process.WaitForInputIdle(10000); } catch { }

            using var automation = new UIA3Automation();
            var window = UiTestHelpers.CompleteStartupGating(automation, process);
            Assert.NotNull(window);

            try { window.Focus(); } catch { }
            try { window.SetForeground(); } catch { }

            // Both items are listed before anything is filtered, so an item going away later
            // means the filter hid it rather than that it was never there.
            Assert.True(UiTestHelpers.WaitForGridRow(window, "ItemsDataGrid", "Rice", TimeSpan.FromSeconds(15)),
                "Rice was not listed before filtering.");
            Assert.True(UiTestHelpers.WaitForGridRow(window, "ItemsDataGrid", "Milk", TimeSpan.FromSeconds(15)),
                "Milk was not listed before filtering.");

            var categoryCombo = window.FindFirstDescendant(cf => cf.ByAutomationId("CategoryFilterCombo"));
            Assert.NotNull(categoryCombo);
            Assert.True(ChooseComboItem(window, categoryCombo!, categoryName),
                $"'{categoryName}' could not be chosen in the category filter.");

            Assert.True(UiTestHelpers.WaitForGridRow(window, "ItemsDataGrid", "Rice", TimeSpan.FromSeconds(10)),
                "Rice was filtered out by its own category.");
            Assert.True(WaitForGridRowToGoAway(window, "ItemsDataGrid", "Milk", TimeSpan.FromSeconds(10)),
                "Choosing a category did not narrow the list.");

            // Back to everything, then narrow by place instead.
            Assert.True(ChooseComboItem(window, categoryCombo!, "All"),
                "'All' could not be chosen in the category filter.");
            Assert.True(UiTestHelpers.WaitForGridRow(window, "ItemsDataGrid", "Milk", TimeSpan.FromSeconds(10)),
                "'All' did not bring the rest of the list back.");

            var locationCombo = window.FindFirstDescendant(cf => cf.ByAutomationId("LocationFilterCombo"));
            Assert.NotNull(locationCombo);
            Assert.True(ChooseComboItem(window, locationCombo!, locationName),
                $"'{locationName}' could not be chosen in the location filter.");

            Assert.True(UiTestHelpers.WaitForGridRow(window, "ItemsDataGrid", "Rice", TimeSpan.FromSeconds(10)),
                "Rice was filtered out by its own location.");
            Assert.True(WaitForGridRowToGoAway(window, "ItemsDataGrid", "Milk", TimeSpan.FromSeconds(10)),
                "Choosing a location did not narrow the list.");

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

    /// <summary>
    /// The import has always wanted its own particular spreadsheet shape and never said so
    /// anywhere, so the only way to learn it was to feed the import a file and read the
    /// complaints. This is the page that says it, reached the way a person reaches it, and the
    /// columns it names are checked against the ones the import reads.
    /// </summary>
    [StaFact(DisplayName = "Help_ExplainsWhatTheItemImportExpects")]
    public void Help_ExplainsWhatTheItemImportExpects()
    {
        var connection = UiTestHelpers.CreateIsolatedSqliteConnectionString();
        using var process = UiTestHelpers.LaunchApp(connection);

        try
        {
            try { process.WaitForInputIdle(10000); } catch { }

            using var automation = new UIA3Automation();
            var window = UiTestHelpers.CompleteStartupGating(automation, process);
            Assert.NotNull(window);

            try { window.Focus(); } catch { }
            try { window.SetForeground(); } catch { }

            var menuBar = window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Menu))?.AsMenu();
            Assert.NotNull(menuBar);

            var helpMenu = FindMenuItem(menuBar!, "Help");
            Assert.NotNull(helpMenu);
            helpMenu!.Click();
            Thread.Sleep(300);

            var importHelpItem = FindMenuItem(helpMenu, "Importing from a spreadsheet");
            Assert.NotNull(importHelpItem);
            importHelpItem!.Click();

            var page = UiTestHelpers.WaitForDialog(window, "Importing from a spreadsheet", TimeSpan.FromSeconds(10));
            Assert.NotNull(page);

            // The page has to name real columns, or following it still produces a file the
            // import refuses.
            Assert.True(WaitForText(page!, "CategoryName", TimeSpan.FromSeconds(10)),
                "The help page does not name the CategoryName column.");
            Assert.True(WaitForText(page!, "ExpiryDate", TimeSpan.FromSeconds(10)),
                "The help page does not name the ExpiryDate column.");
            Assert.True(WaitForText(page!, "Always", TimeSpan.FromSeconds(5)),
                "The help page does not say which column is the one that is needed.");

            var close = page!.FindFirstDescendant(cf =>
                cf.ByAutomationId("ImportHelpCloseButton").And(cf.ByControlType(ControlType.Button)));
            Assert.NotNull(close);
            UiTestHelpers.TryInvokeWithFallback(close!);

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

    /// <summary>Finds a menu entry by what it reads as, ignoring the access-key underscore.</summary>
    private static MenuItem? FindMenuItem(Menu menu, string header)
        => menu.Items.FirstOrDefault(i => string.Equals(
            (i.Properties.Name?.Value ?? string.Empty).Replace("_", string.Empty),
            header, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds a submenu entry by what it reads as, ignoring the access-key underscore.</summary>
    private static MenuItem? FindMenuItem(MenuItem parent, string header)
        => parent.Items.FirstOrDefault(i => string.Equals(
            (i.Properties.Name?.Value ?? string.Empty).Replace("_", string.Empty),
            header, StringComparison.OrdinalIgnoreCase));

    /// <summary>Waits for a piece of text to appear anywhere under an element.</summary>
    private static bool WaitForText(AutomationElement root, string text, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var found = root.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                    .Any(e => (e.Name ?? string.Empty).Contains(text, StringComparison.OrdinalIgnoreCase));
                if (found) return true;
            }
            catch { }

            Thread.Sleep(150);
        }

        return false;
    }

    /// <summary>
    /// Creates the isolated database up front with a category, a location, one item filed under
    /// both and one filed under neither, so the filters have something real to narrow. The app
    /// opens this same file at startup.
    /// </summary>
    private static void SeedFilterData(string connectionString, string categoryName, string locationName)
    {
        var options = new DbContextOptionsBuilder<KitchenInventoryDbContext>()
            .UseSqlite(connectionString)
            .Options;

        using var db = new KitchenInventoryDbContext(options);
        db.Database.Migrate();

        var category = new Category { Name = categoryName };
        var location = new Location { Name = locationName };
        db.Categories.Add(category);
        db.Locations.Add(location);
        db.Items.Add(new Item { Name = "Rice", Quantity = 10m, Unit = "kg", Category = category, Location = location });
        db.Items.Add(new Item { Name = "Milk", Quantity = 5m, Unit = "L" });
        db.SaveChanges();
    }

    /// <summary>Waits for a row to stop being listed, which is how a filter hiding it looks.</summary>
    private static bool WaitForGridRowToGoAway(Window window, string gridAutomationId, string contains, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (!UiTestHelpers.WaitForGridRow(window, gridAutomationId, contains, TimeSpan.FromMilliseconds(50)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Picks an entry out of a drop-down. Selection goes through the item's selection pattern
    /// rather than a click, for the same reason the rest of the UI tests avoid keystrokes.
    /// </summary>
    private static bool ChooseComboItem(Window window, AutomationElement combo, string itemName)
    {
        try
        {
            if (combo.Patterns.ExpandCollapse.IsSupported)
            {
                combo.Patterns.ExpandCollapse.Pattern.Expand();
            }
        }
        catch { }

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            // The entries hang off the box itself; the window is searched as well in case the
            // drop-down was published as its own surface.
            var item = FindListItem(combo, itemName) ?? FindListItem(window, itemName);
            if (item != null && item.Patterns.SelectionItem.IsSupported)
            {
                item.Patterns.SelectionItem.Pattern.Select();
                Thread.Sleep(250);
                try
                {
                    if (combo.Patterns.ExpandCollapse.IsSupported)
                    {
                        combo.Patterns.ExpandCollapse.Pattern.Collapse();
                    }
                }
                catch { }
                return true;
            }

            Thread.Sleep(150);
        }

        return false;
    }

    private static AutomationElement? FindListItem(AutomationElement root, string name)
    {
        try
        {
            return root.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
                .FirstOrDefault(i => string.Equals((i.Name ?? string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }
}
