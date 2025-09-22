using System;
using System.Windows;
using System.Windows.Controls;
using KitchenInventory.Desktop.ViewModels;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using KitchenInventory.Desktop.Utilities;

namespace KitchenInventory.Desktop
{
    public partial class MainWindow : Window
    {
        private readonly ItemsViewModel _viewModel;
        private static readonly string DecimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        private static readonly Regex QuantityRegex = new($"^\\d*(?:{Regex.Escape(DecimalSeparator)}\\d{{0,3}})?$");
        private int _validationErrorCount = 0;

        public MainWindow(ItemsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            _viewModel = viewModel;
            // Replace inline lambda to allow post-load logic for first-run categories
            Loaded += MainWindow_OnLoaded;
        }

        private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            await _viewModel.LoadAsync();

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

        private void DataGrid_CurrentCellChanged(object? sender, EventArgs e)
        {
            CommandManager.InvalidateRequerySuggested();
        }

        private void Quantity_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            var tb = (TextBox)sender;
            var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, e.Text);
            e.Handled = !QuantityRegex.IsMatch(proposed);
        }

        private void Quantity_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(DataFormats.Text))
            {
                var paste = (string)e.DataObject.GetData(DataFormats.Text)!;
                e.CancelCommand();
                var tb = (TextBox)sender;
                var proposed = tb.Text.Remove(tb.SelectionStart, tb.SelectionLength).Insert(tb.SelectionStart, paste);
                if (QuantityRegex.IsMatch(proposed))
                {
                    tb.Text = proposed;
                    tb.CaretIndex = proposed.Length;
                }
            }
            else
            {
                e.CancelCommand();
            }
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