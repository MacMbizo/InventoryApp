using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Desktop.Utilities;
using KitchenInventory.Domain.Entities;

namespace KitchenInventory.Desktop
{
    public partial class CsvImportPreviewDialog : Window
    {
        public CsvImportPreviewResult Result { get; private set; }
        private readonly IFileSaveService _fileSaveService;
        private readonly IReadOnlyList<Category> _categories;
        private readonly IReadOnlyList<Location> _locations;

        public CsvImportPreviewDialog(
            CsvImportPreviewResult result,
            IFileSaveService fileSaveService,
            IEnumerable<Category>? categories = null,
            IEnumerable<Location>? locations = null)
        {
            InitializeComponent();
            Result = result;
            _fileSaveService = fileSaveService;
            _categories = categories?.ToList() ?? new List<Category>();
            _locations = locations?.ToList() ?? new List<Location>();
            DataContext = result;

            // Nothing in a file whose first line is unreadable can be brought in, so the button
            // that would try is off rather than sitting there to be pressed.
            if (result.HasHeaderProblem)
            {
                ImportButton.IsEnabled = false;
            }
        }

        private async void OnSaveTemplateClick(object sender, RoutedEventArgs e)
            => await ImportTemplateSaver.SaveAsync(_fileSaveService, this, _categories, _locations);

        private void OnImportClick(object sender, RoutedEventArgs e)
        {
            if (Result.HasHeaderProblem)
            {
                return;
            }

            if (Result.Error > 0)
            {
                var confirm = MessageBoxEx.Confirm($"There are {Result.Error} row(s) with errors. Invalid rows will be skipped. Continue importing valid rows?", owner: this);
                if (confirm != true)
                    return;
            }
            // DialogService.ShowAsync returns the dialog's Tag, so a confirmation must be
            // signalled through Tag (mirrors AddItemDialog) for the caller to see it.
            Tag = true;
            DialogResult = true;
            Close();
        }

        private async void OnExportErrorsClick(object sender, RoutedEventArgs e)
        {
            if (Result.Rows == null || Result.Rows.Count == 0)
            {
                MessageBoxEx.Info("No rows to export.", owner: this);
                return;
            }

            var hasErrors = Result.Rows.Any(r => !r.IsValid);
            if (!hasErrors)
            {
                MessageBoxEx.Info("No errors to export.", owner: this);
                return;
            }

            try
            {
                var csvText = CsvImportPreviewAnalyzer.BuildErrorsCsv(Result);
                var suggestedName = $"import-errors-{DateTime.Now:yyyyMMdd-HHmm}.csv";
                var ok = await _fileSaveService.SaveTextAsAsync(suggestedName, csvText);
                if (ok)
                {
                    MessageBoxEx.Info("Error report saved.", owner: this);
                }
            }
            catch (Exception ex)
            {
                MessageBoxEx.Error($"Failed to export errors: {ex.Message}", owner: this);
            }
        }
    }
}