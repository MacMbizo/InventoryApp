using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using KitchenInventory.Desktop.Services;
using Microsoft.Win32;

namespace KitchenInventory.Desktop;

public partial class DiagnosticsWindow : Window
{
    private readonly IDatabaseInfoService _infoService;
    private readonly IDiagnosticsExporter _exporter;
    private DatabaseInfo? _info;

    public DiagnosticsWindow(IDatabaseInfoService infoService, IDiagnosticsExporter exporter)
    {
        InitializeComponent();
        _infoService = infoService;
        _exporter = exporter;
        Loaded += async (_, __) => await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _info = await _infoService.GetInfoAsync();
            DataContext = _info;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to load diagnostics: {ex.Message}", "Diagnostics", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_info == null) return;
        var sb = new StringBuilder();
        sb.AppendLine($"Provider: {_info.Provider}");
        sb.AppendLine($"Target: {_info.Target}");
        sb.AppendLine($"ProviderVersion: {_info.ProviderVersion}");
        sb.AppendLine($"AppliedMigrations: {_info.AppliedMigrations}");
        sb.AppendLine($"PendingMigrations: {_info.PendingMigrations}");
        sb.AppendLine($"Health: {_info.Health.Status} - {_info.Health.Message}");
        sb.AppendLine($"RedactedConnection: {_info.RedactedConnection}");
        sb.AppendLine($"LogsDirectory: {_info.LogsDirectory}");
        if (!string.IsNullOrWhiteSpace(_info.DatabaseFilePath)) sb.AppendLine($"DatabaseFilePath: {_info.DatabaseFilePath}");
        Clipboard.SetText(sb.ToString());
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        if (_info == null) return;
        OpenFolder(_info.LogsDirectory);
    }

    private void OpenDbFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_info?.DatabaseFilePath is null) return;
        var dir = Path.GetDirectoryName(_info.DatabaseFilePath);
        if (!string.IsNullOrEmpty(dir)) OpenFolder(dir);
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var suggested = $"diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip";
            var dlg = new SaveFileDialog
            {
                Title = "Export Diagnostics Bundle",
                FileName = suggested,
                Filter = "Zip files (*.zip)|*.zip|All files (*.*)|*.*",
                DefaultExt = ".zip",
                AddExtension = true,
                OverwritePrompt = true
            };

            var result = dlg.ShowDialog(this);
            if (result != true) return;

            // Best-effort UX: disable window during export
            IsEnabled = false;
            try
            {
                await _exporter.ExportAsync(dlg.FileName);
                MessageBox.Show(this, $"Diagnostics bundle exported to:\n{dlg.FileName}", "Diagnostics", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Failed to export diagnostics: {ex.Message}", "Diagnostics", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void OpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
                Verb = "open"
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open folder: {ex.Message}", "Diagnostics", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}