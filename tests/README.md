# KitchenInventory.Desktop.Tests – UI Automation Robustness Guide

Audience: Contributors writing or maintaining UI tests (FlaUI/UIA3) for the desktop app.

Goals
- Make UI tests stable across DPI, theming, and layout changes.
- Prefer AutomationId-first selectors; fall back to accessible Name when needed.
- Provide consistent helpers and diagnostics to accelerate triage.

Key Practices
1) Prefer AutomationId over Name
   - Assign stable AutomationIds in the app (Buttons, MenuItems, TextBoxes).
   - Test selectors should try AutomationId first, then Name. Use:
     - UiTestHelpers.WaitForAndClickButtonByIdOrName(mainWindow, "SaveChangesButton")
     - UiTestHelpers.ClickActionByName(mainWindow, "Manage Categories", _output)

2) Always maximize the main window
   - UiTestHelpers.TryGetMainWindow(...) already maximizes and sets focus.
   - This improves layout determinism and avoids hidden toolbar items.

3) Timeouts and retries
   - Default waits use ~5–10s and retry with small sleeps.
   - For slow CI or first-run cases, pass a longer timeout:
     - var dialog = UiTestHelpers.WaitForDialog(mainWindow, "Enter new category name", TimeSpan.FromSeconds(15));

4) Toolbar overflow/menus
   - Many actions live in a toolbar with an overflow chevron.
   - Helpers attempt to reveal overflow automatically:
     - UiTestHelpers.TryOpenToolbarOverflow(mainWindow, _output)
     - UiTestHelpers.ClickActionByName(mainWindow, "Add Stock", _output)

5) Rich diagnostics
   - Use ITestOutputHelper to print element trees and search attempts.
   - Helpers such as DebugAllButtons and ClickActionByName output context on failure.

6) Dialogs and prompts
   - Use WaitForDialog with a distinctive substring of the title.
   - Prefer data entry via AutomationId where possible; otherwise use visible Name.

Core Helpers (excerpt)
- TryGetMainWindow(app, automation) → Window
- WaitForDialog(parentWindow, titleContains, timeout)
- TryOpenToolbarOverflow(mainWindow, output)
- TryFindActionElement(mainWindow, actionName, output)
- ClickActionByName(mainWindow, actionName, output)
- WaitForAndClickButtonByIdOrName(mainWindow, automationIdOrName, timeout?, output?)

Conventions
- Keep all generic UI helpers in UiTestHelpers.cs. Do not rewrap in individual test classes.
- Pass _output from tests when available for better logs.
- Use Assert.Matches/DoesNotMatch for regex assertions (xUnit2008 compliance).

Troubleshooting Checklist
- Window maximized? Use TryGetMainWindow and ensure it returned non-null.
- Action not found? Call DebugAllButtons(mainWindow, _output) and inspect logs.
- Hidden in overflow? Call TryOpenToolbarOverflow before searching.
- Dialog not found? Increase timeout and verify title substring.
- Flaky focus-related clicks? Use TryInvokeWithFallback and/or pause with Wait.UntilInputIsProcessed.

CI Notes
- Prefer Release runs for UI tests to match production timings.
- Avoid running tests in parallel within the same UI collection.