using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using KitchenInventory.Data;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Desktop.Utilities;

namespace KitchenInventory.Desktop;

/// <summary>
/// Shows where the data file is, lets the user copy it, check it is sound, and put an older
/// copy back. Crafted for someone who has never opened a folder of application data.
/// </summary>
public partial class DatabaseMaintenanceWindow : Window
{
    private readonly IDatabaseMaintenanceService _maintenance;
    private bool _busy;

    public DatabaseMaintenanceWindow(IDatabaseMaintenanceService maintenance)
    {
        _maintenance = maintenance ?? throw new ArgumentNullException(nameof(maintenance));
        InitializeComponent();

        BackupsList.SelectionChanged += (_, __) =>
            RestoreButton.IsEnabled = !_busy && BackupsList.SelectedItem is BackupFileInfo;

        Loaded += (_, __) =>
        {
            RefreshDatabaseDetails();
            RefreshBackups();
            StatusBox.Text = "You can back up your data at any time. Nothing here changes your inventory.";
        };
    }

    private void RefreshDatabaseDetails()
    {
        var path = _maintenance.DatabaseFilePath;
        DatabasePathText.Text = path ?? "(this build keeps its data outside a file)";
        DatabasePathText.ToolTip = path;

        try
        {
            if (path != null && File.Exists(path))
            {
                var info = new FileInfo(path);
                DatabaseSizeText.Text = DescribeSize(info.Length);
                DatabaseSavedText.Text = info.LastWriteTime.ToString("d MMM yyyy 'at' HH:mm");
                return;
            }
        }
        catch
        {
            // Informational only.
        }

        DatabaseSizeText.Text = "unknown";
        DatabaseSavedText.Text = "unknown";
    }

    private void RefreshBackups()
    {
        var selected = (BackupsList.SelectedItem as BackupFileInfo)?.Path;
        var backups = _maintenance.ListBackups();

        BackupsList.ItemsSource = backups;
        BackupsList.DisplayMemberPath = nameof(BackupFileInfo.Description);

        NoBackupsHint.Visibility = backups.Any() ? Visibility.Collapsed : Visibility.Visible;

        if (backups.Any() && selected != null)
        {
            BackupsList.SelectedItem = backups.FirstOrDefault(b => b.Path == selected);
        }

        RestoreButton.IsEnabled = false;
    }

    private async void BackUpNow_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("Back up now", async () =>
        {
            var path = await _maintenance.BackupNowAsync();
            RefreshBackups();
            return $"A copy of your data was saved.\n\n{path}";
        });
    }

    private async void BackUpToFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a folder for the backup"
        };
        var owner = WindowOwnerHelper.GetSafeOwner(this) ?? this;
        if (dialog.ShowDialog(owner) != true)
        {
            return;
        }

        await RunBusyAsync("Back up to another folder", async () =>
        {
            var path = await _maintenance.BackupToFolderAsync(dialog.FolderName);
            return $"A copy of your data was saved.\n\n{path}";
        });
    }

    private async void Check_Click(object sender, RoutedEventArgs e)
    {
        await RunBusyAsync("Check for problems", async () =>
        {
            var report = await _maintenance.CheckAsync();
            RefreshDatabaseDetails();
            return Describe(report);
        });
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (BackupsList.SelectedItem is not BackupFileInfo backup)
        {
            return;
        }

        var owner = WindowOwnerHelper.GetSafeOwner(this) ?? this;
        var confirmed = MessageBox.Show(owner,
            $"Put this copy back?\n\n{backup.Description}\n\n" +
            "Your current data will be copied aside first, so this can be undone. " +
            "The app will close and open again with the restored data.",
            "Restore this backup", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirmed != MessageBoxResult.Yes)
        {
            return;
        }

        if (_busy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _maintenance.RestoreAsync(backup.Path);

            MessageBox.Show(owner,
                "Your data has been put back.\n\n" +
                $"What was there before is kept here, just in case:\n{result.SafetyCopyPath}\n\n" +
                "The app will now close. Open it again to carry on.",
                "Restore complete", MessageBoxButton.OK, MessageBoxImage.Information);

            RestartApplication();
        }
        catch (Exception ex)
        {
            StatusBox.Text = $"The backup could not be put back.\n\n{ex.Message}\n\n" +
                             "Nothing was changed. Please try another backup.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Runs an operation with the buttons disabled, and reports the outcome in the box.</summary>
    private async Task RunBusyAsync(string what, Func<Task<string>> operation)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        StatusBox.Text = $"{what}…";
        try
        {
            StatusBox.Text = await operation();
        }
        catch (Exception ex)
        {
            StatusBox.Text = $"{what} did not work.\n\n{ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BackUpNowButton.IsEnabled = !busy;
        BackUpToFolderButton.IsEnabled = !busy;
        CheckButton.IsEnabled = !busy;
        RestoreButton.IsEnabled = !busy && BackupsList.SelectedItem is BackupFileInfo;
        Mouse.OverrideCursor = busy ? Cursors.Wait : null;
    }

    private static string Describe(DatabaseIntegrityReport report)
    {
        var lines = report.Findings
            .Select(f => $"{(f.IsProblem ? "PROBLEM" : "OK")}  ·  {f.Area}\n    {f.Message}");
        return report.Summary + "\n\n" + string.Join("\n\n", lines);
    }

    private static string DescribeSize(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / (1024.0 * 1024.0):0.#} MB"
            : $"{Math.Max(1, bytes / 1024)} KB";

    /// <summary>
    /// Starts a fresh copy of the app and closes this one. The database file underneath the
    /// running app has just been replaced, so nothing in this process can be trusted afterwards.
    /// </summary>
    private void RestartApplication()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(exe))
            {
                Process.Start(new ProcessStartInfo(exe)
                {
                    UseShellExecute = true,
                    WorkingDirectory = AppContext.BaseDirectory
                });
            }
        }
        catch
        {
            // The message above already told the user to open the app again by hand.
        }

        Application.Current.Shutdown();
    }
}
