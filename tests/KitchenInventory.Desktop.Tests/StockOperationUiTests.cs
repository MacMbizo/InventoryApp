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
        return UiTestHelpers.GetDesktopExePath();
    }

    private static string GetRepoRoot()
    {
        return UiTestHelpers.GetRepoRoot();
    }

    private static Window? TryGetMainWindow(Application app, UIA3Automation automation)
    {
        return UiTestHelpers.TryGetMainWindow(app, automation);
    }

    private static Window? WaitForDialog(Window parentWindow, string titleContains, TimeSpan timeout)
    {
        return UiTestHelpers.WaitForDialog(parentWindow, titleContains, timeout);
    }

    private static void CloseDialog(Window dialog, string buttonText)
    {
        UiTestHelpers.CloseDialog(dialog, buttonText);
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

    // Note: Use UiTestHelpers directly for toolbar overflow, debug, action lookup, and clicks.
    // Example:
    // UiTestHelpers.TryOpenToolbarOverflow(mainWindow, _output);
    // UiTestHelpers.DebugAllButtons(mainWindow, _output);
    // var el = UiTestHelpers.TryFindActionElement(mainWindow, "Add Stock", _output);
    // UiTestHelpers.ClickActionByName(mainWindow, "Add Stock", _output);
    // UiTestHelpers.WaitForAndClickButtonByIdOrName(mainWindow, "SaveChangesButton", TimeSpan.FromSeconds(10), _output);
    
}