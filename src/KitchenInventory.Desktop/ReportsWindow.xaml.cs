using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using KitchenInventory.Desktop.ViewModels;

namespace KitchenInventory.Desktop
{
    public partial class ReportsWindow : Window
    {
        private readonly ReportsViewModel _viewModel;

        public ReportsWindow(ReportsViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            Closed += (_, __) => viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ReportsViewModel.CurrentReport))
            {
                RebuildColumns();
            }
        }

        private void RebuildColumns()
        {
            ReportGrid.Columns.Clear();

            var report = _viewModel.CurrentReport;
            if (report == null)
            {
                ReportGrid.ItemsSource = null;
                return;
            }

            // Report rows are ordered cell lists, so bind each column to the row index.
            for (var i = 0; i < report.Headers.Count; i++)
            {
                ReportGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = report.Headers[i],
                    Binding = new Binding($"[{i}]"),
                    Width = new DataGridLength(1, DataGridLengthUnitType.Auto)
                });
            }

            ReportGrid.ItemsSource = report.Rows;
        }
    }
}
