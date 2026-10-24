using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Linq;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

[Collection("UI")]
[Trait("Category", "UI")]
public class PrivacyConsentUiTests
{
    private static string GetDesktopExePath() => UiTestHelpers.GetDesktopExePath();
    private static Window? TryGetMainWindow(Application app, UIA3Automation automation) => UiTestHelpers.TryGetMainWindow(app, automation);

    private static string GetPreferencesPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDir = Path.Combine(appData, "InventoryApp");
        Directory.CreateDirectory(baseDir);
        return Path.Combine(baseDir, "preferences.json");
    }

    [StaFact]
    public void FirstRun_Privacy_NoThanks_WritesExpectedPreferences()
    {
        // Arrange: ensure a clean first-run by backing up any existing preferences and deleting it
        var preferencesPath = GetPreferencesPath();
        var hadExisting = File.Exists(preferencesPath);
        var backupPath = hadExisting ? preferencesPath + ".bak." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "." + Guid.NewGuid().ToString("N") : null;
        if (hadExisting)
        {
            File.Copy(preferencesPath, backupPath!, overwrite: false);
            File.Delete(preferencesPath);
        }

        var exe = GetDesktopExePath();
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            Arguments = string.Empty,
        };

        // Ensure UI is shown and consent dialog is not suppressed
        psi.Environment["CI"] = "false";                     // force non-headless even in CI
        psi.Environment["INVENTORY_HEADLESS"] = "0";          // explicitly disable headless
        psi.Environment.Remove("INVENTORY_SUPPRESS_FIRST_RUN"); // allow first-run consent dialog
        psi.Environment.Remove("INVENTORY_EXPORT_DIAGNOSTICS");
        psi.Environment.Remove("INVENTORY_CRASH_TEST");

        using var process = Process.Start(psi)!;
        try { process.WaitForInputIdle(10000); } catch { /* ignore */ }

        using var automation = new UIA3Automation();
        using var app = Application.Attach(process.Id);

        // Act: Directly wait for the Crash Reporting consent dialog (shown before MainWindow)
        AutomationElement? consentEl = null;
        var consentDeadline = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < consentDeadline)
        {
            try
            {
                var desktop = automation.GetDesktop();
                var windows = desktop.FindAllDescendants(cf => cf.ByProcessId(process.Id).And(cf.ByControlType(ControlType.Window)));
                consentEl = windows.FirstOrDefault(w =>
                {
                    var name = w.Name ?? string.Empty;
                    if (!string.IsNullOrEmpty(name) && name.Contains("Crash Reporting", StringComparison.OrdinalIgnoreCase)) return true;
                    try
                    {
                        var win = w.AsWindow();
                        var title = win?.Title ?? string.Empty;
                        return !string.IsNullOrEmpty(title) && title.Contains("Crash Reporting", StringComparison.OrdinalIgnoreCase);
                    }
                    catch { return false; }
                });
                if (consentEl != null) break;
            }
            catch { }
            Thread.Sleep(150);
        }
        Assert.NotNull(consentEl);
        var consent = consentEl!.AsWindow();

        // Locate the "No thanks" button by name and click
        var noThanksBtn = consent.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("No thanks")));
        Assert.NotNull(noThanksBtn);
        UiTestHelpers.TryInvokeWithFallback(noThanksBtn!);

        // Assert: preferences.json should be created and contain expected keys/values
        var fileDeadline = DateTime.UtcNow.AddSeconds(5);
        while (!File.Exists(preferencesPath) && DateTime.UtcNow < fileDeadline)
        {
            Thread.Sleep(100);
        }
        Assert.True(File.Exists(preferencesPath), $"Expected preferences file at '{preferencesPath}' to be created after consent.");

        var jsonText = File.ReadAllText(preferencesPath);
        Assert.False(string.IsNullOrWhiteSpace(jsonText));

        using var doc = JsonDocument.Parse(jsonText);
        var root = doc.RootElement;
        Assert.Equal(JsonValueKind.Object, root.ValueKind);

        // privacy.errorReportingPrompted must be true
        Assert.True(root.TryGetProperty("privacy.errorReportingPrompted", out var promptedProp));
        Assert.Equal(JsonValueKind.True, promptedProp.ValueKind);

        // privacy.errorReportingEnabled must be false (since we clicked "No thanks")
        Assert.True(root.TryGetProperty("privacy.errorReportingEnabled", out var enabledProp));
        Assert.Equal(JsonValueKind.False, enabledProp.ValueKind);

        // After consent, the MainWindow should appear; wait for it so we can close cleanly
        Window? mainWindow = null;
        var mainDeadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < mainDeadline)
        {
            mainWindow = TryGetMainWindow(app, automation);
            if (mainWindow != null && !string.IsNullOrEmpty(mainWindow.Title) && !mainWindow.Title.Equals("Crash Reporting", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }
            Thread.Sleep(200);
        }
        Assert.NotNull(mainWindow);

        // Cleanup: close the app
        try { mainWindow!.Close(); } catch { }
        var exited = process.WaitForExit(TimeSpan.FromSeconds(20));
        Assert.True(exited, "Desktop process did not exit in allotted time after closing main window.");
        Assert.Equal(0, process.ExitCode);

        // Done. Restore original preferences if any
        if (hadExisting && backupPath != null)
        {
            try
            {
                // Replace test-generated preferences with the original
                if (File.Exists(preferencesPath)) File.Delete(preferencesPath);
                File.Move(backupPath, preferencesPath);
            }
            catch { /* best-effort */ }
        }
        else
        {
            // No original prefs existed; remove the test-created file for isolation
            try { if (File.Exists(preferencesPath)) File.Delete(preferencesPath); } catch { }
        }
    }
}