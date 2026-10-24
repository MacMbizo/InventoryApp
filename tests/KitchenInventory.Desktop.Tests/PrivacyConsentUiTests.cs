using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Linq;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

[Collection("UI")]
[Trait("Category", "UI")]
public class PrivacyConsentUiTests
{
    [StaFact]
    public void FirstRun_Privacy_NoThanks_WritesExpectedPreferences()
    {
        // Every UI test gets its own fresh, isolated database and app-data directory.
        var connection = UiTestHelpers.CreateIsolatedSqliteConnectionString();
        var psi = UiTestHelpers.CreateStartInfo(connection);

        // The app writes preferences beside the isolated DB (see INVENTORY_DATA_DIR),
        // never in the developer's %LOCALAPPDATA%\InventoryApp.
        var preferencesPath = Path.Combine(UiTestHelpers.ResolveDataDirectory(connection), "preferences.json");

        using var process = Process.Start(psi)!;
        try
        {
            try { process.WaitForInputIdle(10000); } catch { /* ignore */ }

            using var automation = new UIA3Automation();

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

            // After consent the app hits the authentication gate (fresh DB -> Initial Admin
            // Setup) and then shows the MainWindow. Drive those real dialogs.
            var mainWindow = UiTestHelpers.CompleteStartupGating(automation, process);
            Assert.NotNull(mainWindow);
            Assert.Contains("Kitchen Inventory", mainWindow.Title, StringComparison.OrdinalIgnoreCase);

            // Cleanup: close the app
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
}
