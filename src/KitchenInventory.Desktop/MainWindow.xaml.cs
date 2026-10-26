using System;
using System.Windows;
using System.Windows.Controls;
using KitchenInventory.Desktop.ViewModels;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Domain.Constants;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using KitchenInventory.Desktop.Utilities;

namespace KitchenInventory.Desktop
{
    public partial class MainWindow : Window
    {
        /// <summary>Ctrl+F. Puts the caret in the search box, which is otherwise only discoverable by eye.</summary>
        public static readonly RoutedUICommand FocusSearchCommand =
            new("Focus the search box", nameof(FocusSearchCommand), typeof(MainWindow));

        /// <summary>F2. Opens the selected row for editing, the keyboard's equivalent of double-clicking a cell.</summary>
        public static readonly RoutedUICommand EditSelectedItemCommand =
            new("Edit the selected item", nameof(EditSelectedItemCommand), typeof(MainWindow));

        private readonly ItemsViewModel _viewModel;
        private int _validationErrorCount = 0;
        private readonly DispatcherTimer _alertsTimer;

        public MainWindow(ItemsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            _viewModel = viewModel;
            // Replace inline lambda to allow post-load logic for first-run categories
            Loaded += MainWindow_OnLoaded;

            // Periodically re-evaluate alerts so day-based alerts (expiring/expired) stay current.
            _alertsTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
            _alertsTimer.Tick += (_, __) =>
            {
                try { _viewModel.RefreshAlerts(); }
                catch { /* alerts refresh is best-effort */ }
            };
            Closed += (_, __) => _alertsTimer.Stop();
        }

        private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            await _viewModel.LoadAsync();

            // Gate the user management entry point to Administrators only
            try
            {
                var session = ((App)Application.Current).Services.GetRequiredService<ISessionService>();
                ManageUsersMenuItem.Visibility = string.Equals(session.CurrentRole, Roles.Admin, StringComparison.Ordinal)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            catch { }

            // Respect automation/test runs: allow suppressing first-run prompt via env var
            bool suppressFirstRunPrompt = false;
            try
            {
                var suppress = Environment.GetEnvironmentVariable("INVENTORY_SUPPRESS_FIRST_RUN");
                if (!string.IsNullOrEmpty(suppress))
                {
                    suppressFirstRunPrompt = suppress.Equals("1", StringComparison.OrdinalIgnoreCase)
                                           || suppress.Equals("true", StringComparison.OrdinalIgnoreCase)
                                           || suppress.Equals("yes", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }

            // If only the sentinel "All" category exists (Id == 0), prompt the user to create categories
            if (!suppressFirstRunPrompt && _viewModel.Categories != null && _viewModel.Categories.Count <= 1)
            {
                var result = MessageBox.Show(WindowOwnerHelper.GetSafeOwner(this) ?? this,
                    "No categories found. Would you like to create categories now?",
                    "Create Categories",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes && _viewModel.ManageCategoriesCommand != null && _viewModel.ManageCategoriesCommand.CanExecute(null))
                {
                    _viewModel.ManageCategoriesCommand.Execute(null);
                }
            }

            _alertsTimer.Start();
        }

        /// <summary>
        /// Shows and hides the occasional toolbar actions. They are ordinary buttons in the
        /// window rather than a pop-up menu, so they keep their names — reachable with the
        /// keyboard, readable to a screen reader, and clickable by the UI tests that address
        /// them by name.
        /// </summary>
        private void MoreActions_Click(object sender, RoutedEventArgs e)
        {
            MorePanel.Visibility = MorePanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void Open_UnitConverter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new UnitConverterDialog { Owner = WindowOwnerHelper.GetSafeOwner(this) ?? this };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to open unit converter: {ex.Message}", "Unit Converter", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnValidationError(object sender, ValidationErrorEventArgs e)
        {
            if (e.Action == ValidationErrorEventAction.Added)
            {
                _validationErrorCount++;
            }
            else if (e.Action == ValidationErrorEventAction.Removed)
            {
                _validationErrorCount = Math.Max(0, _validationErrorCount - 1);
            }

            if (DataContext is ItemsViewModel vm)
            {
                bool hasErrors = _validationErrorCount > 0;
                if (vm.HasValidationErrors != hasErrors)
                {
                    vm.HasValidationErrors = hasErrors;
                }
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CommandManager.InvalidateRequerySuggested();
        }

        private void FocusSearch_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }

        private void EditSelectedItem_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            if (ItemsGrid.SelectedItem is null || ItemsGrid.Columns.Count == 0)
            {
                return;
            }

            // Put the caret in the first cell of the selected row, which is what the user
            // would have double-clicked to get here.
            ItemsGrid.CurrentCell = new DataGridCellInfo(ItemsGrid.SelectedItem, ItemsGrid.Columns[0]);
            ItemsGrid.Focus();
            ItemsGrid.BeginEdit();
        }

        private void DataGrid_CurrentCellChanged(object? sender, EventArgs e)
        {
            CommandManager.InvalidateRequerySuggested();
        }

        private void Quantity_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var tb = (TextBox)sender;
            var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, e.Text);
            e.Handled = !QuantityText.IsValidTyping(proposed, CultureInfo.CurrentCulture);
        }

        private void Quantity_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(DataFormats.Text))
            {
                e.CancelCommand();
                return;
            }

            // Take over the paste so that an amount copied from a spreadsheet or another locale
            // arrives in this machine's convention instead of being dropped without a word.
            e.CancelCommand();
            var pasted = QuantityText.NormalizePasted((string?)e.DataObject.GetData(DataFormats.Text), CultureInfo.CurrentCulture);
            var tb = (TextBox)sender;
            var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, pasted);
            if (QuantityText.IsValidTyping(proposed, CultureInfo.CurrentCulture))
            {
                tb.Text = proposed;
                tb.CaretIndex = proposed.Length;
            }
        }

        private void Open_Reports_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sp = ((App)Application.Current).Services;
                var window = sp.GetRequiredService<ReportsWindow>();
                WindowOwnerHelper.SetSafeOwner(window, this);
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to open reports: {ex.Message}", "Reports", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Help_About_Click(object sender, RoutedEventArgs e)
        {
            var owner = WindowOwnerHelper.GetSafeOwner(this) ?? this;
            MessageBox.Show(owner,
                $"Kitchen Inventory {VersionInfo.AppVersion}\n\n" +
                "Keep track of what is in the kitchen store, what is running low,\n" +
                "and what is about to go out of date.\n\n" +
                $"Build {VersionInfo.ShortCommit}",
                "About Kitchen Inventory", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Help_Diagnostics_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sp = ((App)Application.Current).Services;
                var diag = sp.GetRequiredService<DiagnosticsWindow>();
                WindowOwnerHelper.SetSafeOwner(diag, this);
                diag.ShowDialog();
            }
            catch (Exception ex)
            {
                var owner = WindowOwnerHelper.GetSafeOwner(this) ?? this;
                MessageBox.Show(owner, $"Failed to open diagnostics: {ex.Message}", "Diagnostics", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Open_DatabaseMaintenance_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sp = ((App)Application.Current).Services;
                var maintenance = sp.GetRequiredService<IDatabaseMaintenanceService>();
                var window = new DatabaseMaintenanceWindow(maintenance);
                WindowOwnerHelper.SetSafeOwner(window, this);
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                var owner = WindowOwnerHelper.GetSafeOwner(this) ?? this;
                MessageBox.Show(owner, $"Failed to open your data: {ex.Message}", "Your data", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Manage_Users_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sp = ((App)Application.Current).Services;
                var session = sp.GetRequiredService<ISessionService>();
                if (!string.Equals(session.CurrentRole, Roles.Admin, StringComparison.Ordinal))
                {
                    MessageBox.Show(this, "Only administrators can manage users.", "Manage Users", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var auth = sp.GetRequiredService<IAuthenticationService>();
                using var loggerFactory = LoggerFactory.Create(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });
                var dialogLogger = loggerFactory.CreateLogger<UserManagementDialog>();

                var dialog = new UserManagementDialog(auth, session, dialogLogger) { Owner = this };
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to open Manage Users: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Open_Settings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sp = ((App)Application.Current).Services;
                var sw = sp.GetRequiredService<SettingsWindow>();
                WindowOwnerHelper.SetSafeOwner(sw, this);
                // Share the same view model instance so changes reflect immediately
                sw.DataContext = _viewModel;
                sw.ShowDialog();
            }
            catch (Exception ex)
            {
                var owner = WindowOwnerHelper.GetSafeOwner(this) ?? this;
                MessageBox.Show(owner, $"Failed to open Settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}