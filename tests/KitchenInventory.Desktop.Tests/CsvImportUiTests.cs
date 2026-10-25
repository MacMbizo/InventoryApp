using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

[Collection("UI")]
[Trait("Category", "UI")]
public class CsvImportUiTests
{
    /// <summary>
    /// Regression guard: CsvImportPreviewDialog.OnImportClick set DialogResult but not Tag,
    /// while DialogService.ShowAsync returns dialog.Tag. Confirming an import therefore
    /// handed null back to ItemsViewModel, which took the cancel branch and reported
    /// "Import canceled." — the CSV's valid rows were silently discarded.
    /// </summary>
    [StaFact]
    public void Desktop_ImportCsv_WithErrorRows_ImportsValidRows_AfterConfirmation()
    {
        var connection = UiTestHelpers.CreateIsolatedSqliteConnectionString();
        // Keep the fixture beside the isolated app data so the app can always open it.
        var csvPath = Path.Combine(UiTestHelpers.ResolveDataDirectory(connection), "import-regression.csv");
        File.WriteAllText(csvPath, string.Join("\n", new[]
        {
            "Id,Name,Quantity,Unit,ExpiryDate",
            ",Fresh Tomatoes,2,kg,2027-06-01",
            ",Cooking Oil,20,l,2027-06-01",
            ",Rejected Negative,-4,kg,2027-06-01"
        }));

        using var process = UiTestHelpers.LaunchApp(connection);

        try
        {
            try { process.WaitForInputIdle(10000); } catch { }

            using var automation = new UIA3Automation();
            var mainWindow = UiTestHelpers.CompleteStartupGating(automation, process);

            try { mainWindow.Focus(); } catch { }
            try { mainWindow.SetForeground(); } catch { }

            UiTestHelpers.ClickActionByName(mainWindow, "Import Items (CSV)");

            var openDialog = UiTestHelpers.WaitForDialog(mainWindow, "Open", TimeSpan.FromSeconds(15));
            Assert.True(openDialog != null, "The 'Open' file dialog did not appear.");
            SubmitFilePath(openDialog!, csvPath);

            var preview = UiTestHelpers.WaitForDialog(mainWindow, "CSV Import Preview", TimeSpan.FromSeconds(20));
            Assert.True(preview != null, "The 'CSV Import Preview' dialog did not appear.");

            // The fixture deliberately contains an invalid row, so the preview raises a
            // confirmation MessageBox before applying the import.
            var importBtn = preview!.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                                    .FirstOrDefault(b => (b.Name ?? string.Empty).Contains("Import", StringComparison.OrdinalIgnoreCase));
            Assert.True(importBtn != null, "Could not locate the Import button on the preview dialog.");
            UiTestHelpers.TryInvokeWithFallback(importBtn!);

            ConfirmErrorRowMessageBox(preview);

            // The preview closes once the import has been applied.
            var closeDeadline = DateTime.UtcNow.AddSeconds(20);
            while (UiTestHelpers.WaitForDialog(mainWindow, "CSV Import Preview", TimeSpan.FromSeconds(1)) != null
                   && DateTime.UtcNow < closeDeadline)
            {
                Thread.Sleep(200);
            }

            Assert.True(UiTestHelpers.WaitForGridRow(mainWindow, "ItemsDataGrid", "Fresh Tomatoes", TimeSpan.FromSeconds(15)),
                "Valid CSV rows were not imported (expected 'Fresh Tomatoes' in the items grid).");

            var status = UiTestHelpers.GetStatusText(mainWindow);
            Assert.DoesNotContain("Import canceled", status, StringComparison.OrdinalIgnoreCase);

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

    /// <summary>Types a path into the classic Win32 "Open" dialog and confirms it.</summary>
    private static void SubmitFilePath(Window fileDialog, string path)
    {
        try { fileDialog.SetForeground(); } catch { }

        // The "File name" combo's edit box is id 1148 in the classic common dialog.
        var edit = fileDialog.FindFirstDescendant(cf => cf.ByAutomationId("1148"))?.AsTextBox()
                ?? fileDialog.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit))
                             .Select(e => e.AsTextBox())
                             .LastOrDefault(tb => tb != null);
        Assert.True(edit != null, "Could not locate the file name box in the Open dialog.");

        if (edit!.Patterns.Value.IsSupported)
        {
            edit.Patterns.Value.Pattern.SetValue(path);
        }
        else
        {
            try { edit.Click(); } catch { }
            FlaUI.Core.Input.Keyboard.Type(path);
        }
        Thread.Sleep(200);

        // "Open" is button id 1 in the classic common dialog.
        var openBtn = fileDialog.FindFirstDescendant(cf => cf.ByAutomationId("1"))?.AsButton();
        if (openBtn != null)
        {
            openBtn.Invoke();
        }
        else
        {
            try { fileDialog.SetForeground(); } catch { }
            FlaUI.Core.Input.Keyboard.Press(VirtualKeyShort.ENTER);
        }
        Thread.Sleep(300);
    }

    /// <summary>
    /// Dismisses the "There are N row(s) with errors…" confirmation. It is a Win32
    /// MessageBox (class #32770) and does not reliably expose its buttons through UIA,
    /// so Enter is used to activate the default (OK) button when no button is found.
    /// </summary>
    private static void ConfirmErrorRowMessageBox(Window preview)
    {
        Window? confirm = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (confirm == null && DateTime.UtcNow < deadline)
        {
            try
            {
                var desktop = preview.Automation.GetDesktop();
                var pid = preview.Properties.ProcessId.ValueOrDefault;
                confirm = desktop.FindAllDescendants(cf => cf.ByProcessId(pid).And(cf.ByControlType(ControlType.Window)))
                                 .Select(e => { try { return e.AsWindow(); } catch { return null; } })
                                 .FirstOrDefault(w => w != null && string.Equals(w!.ClassName, "#32770", StringComparison.Ordinal));
            }
            catch { }

            if (confirm == null) Thread.Sleep(150);
        }

        if (confirm == null)
        {
            // Nothing to confirm (e.g. a valid-only CSV) — nothing to dismiss.
            return;
        }

        try { confirm.SetForeground(); } catch { }
        var ok = confirm.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("OK")));
        if (ok != null)
        {
            UiTestHelpers.TryInvokeWithFallback(ok);
        }
        else
        {
            FlaUI.Core.Input.Keyboard.Press(VirtualKeyShort.ENTER);
        }
        Thread.Sleep(300);
    }
}
