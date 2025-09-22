using System;
using System.Linq;
using System.Windows;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Desktop.Utilities;

namespace KitchenInventory.Desktop
{
    public partial class CsvImportPreviewDialog : Window
    {
        public CsvImportPreviewResult Result { get; private set; }
        private readonly IFileSaveService _fileSaveService;

        public CsvImportPreviewDialog(CsvImportPreviewResult result, IFileSaveService fileSaveService)
        {
            InitializeComponent();
            Result = result;
            _fileSaveService = fileSaveService;
            DataContext = result;
        }

        private void OnImportClick(object sender, RoutedEventArgs e)
        {
            if (Result.Error > 0)
            {
                var confirm = MessageBoxEx.Confirm($"There are {Result.Error} row(s) with errors. Invalid rows will be skipped. Continue importing valid rows?", owner: this);
                if (confirm != true)
                    return;
            }
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