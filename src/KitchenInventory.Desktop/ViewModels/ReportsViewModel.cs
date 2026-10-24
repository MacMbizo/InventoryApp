using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using KitchenInventory.Data;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KitchenInventory.Desktop.ViewModels
{
    public sealed record ReportOption(ReportKind Kind, string Name);

    /// <summary>
    /// Drives the Reports dialog: pick a report, generate it from the database and
    /// export the result to CSV or PDF.
    /// </summary>
    public class ReportsViewModel : INotifyPropertyChanged
    {
        private const string CsvFilter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*";
        private const string PdfFilter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*";

        private readonly IDbContextFactory<KitchenInventoryDbContext> _dbFactory;
        private readonly IFileSaveService _fileSave;
        private readonly IPreferencesService? _preferences;
        private readonly ILogger<ReportsViewModel>? _logger;

        public IReadOnlyList<ReportOption> ReportOptions { get; } = new[]
        {
            new ReportOption(ReportKind.StockOnHand, "Stock on Hand"),
            new ReportOption(ReportKind.LowStock, "Low Stock"),
            new ReportOption(ReportKind.ExpiringSoon, "Expiring Soon"),
            new ReportOption(ReportKind.MovementHistory, "Movement History")
        };

        private ReportOption _selectedOption;
        public ReportOption SelectedOption
        {
            get => _selectedOption;
            set
            {
                if (value == null || ReferenceEquals(_selectedOption, value)) return;
                _selectedOption = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ShowThreshold));
                OnPropertyChanged(nameof(ShowExpiringDays));
                OnPropertyChanged(nameof(ShowDateRange));
                _preferences?.Set("reports.kind", (int)value.Kind);
                _preferences?.Save();
            }
        }

        public bool ShowThreshold => _selectedOption.Kind == ReportKind.LowStock;
        public bool ShowExpiringDays => _selectedOption.Kind == ReportKind.ExpiringSoon;
        public bool ShowDateRange => _selectedOption.Kind == ReportKind.MovementHistory;

        private decimal _lowStockThreshold;
        public decimal LowStockThreshold
        {
            get => _lowStockThreshold;
            set
            {
                if (_lowStockThreshold == value) return;
                _lowStockThreshold = value;
                OnPropertyChanged();
            }
        }

        private int _expiringSoonDays;
        public int ExpiringSoonDays
        {
            get => _expiringSoonDays;
            set
            {
                if (_expiringSoonDays == value) return;
                _expiringSoonDays = value;
                OnPropertyChanged();
            }
        }

        private DateTime _fromDate;
        public DateTime FromDate
        {
            get => _fromDate;
            set { if (_fromDate != value) { _fromDate = value; OnPropertyChanged(); } }
        }

        private DateTime _toDate;
        public DateTime ToDate
        {
            get => _toDate;
            set { if (_toDate != value) { _toDate = value; OnPropertyChanged(); } }
        }

        private ReportTable? _currentReport;
        public ReportTable? CurrentReport
        {
            get => _currentReport;
            private set
            {
                _currentReport = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasReport));
            }
        }

        public bool HasReport => _currentReport != null;

        private string _statusText = "Choose a report and select Generate.";
        public string StatusText
        {
            get => _statusText;
            private set { if (_statusText != value) { _statusText = value; OnPropertyChanged(); } }
        }

        public ICommand GenerateCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ExportPdfCommand { get; }

        public ReportsViewModel(
            IDbContextFactory<KitchenInventoryDbContext> dbFactory,
            IFileSaveService fileSave,
            IPreferencesService? preferences = null,
            ILogger<ReportsViewModel>? logger = null)
        {
            _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
            _fileSave = fileSave ?? throw new ArgumentNullException(nameof(fileSave));
            _preferences = preferences;
            _logger = logger;

            var savedKind = preferences?.Get("reports.kind", (int)ReportKind.StockOnHand) ?? (int)ReportKind.StockOnHand;
            _selectedOption = ReportOptions.FirstOrDefault(o => (int)o.Kind == savedKind) ?? ReportOptions[0];
            _lowStockThreshold = preferences?.Get("inventory.lowStockThreshold", 5m) ?? 5m;
            _expiringSoonDays = preferences?.Get("inventory.expiringSoonDays", 7) ?? 7;
            _fromDate = DateTime.Today.AddDays(-30);
            _toDate = DateTime.Today;

            GenerateCommand = new AsyncRelayCommand(GenerateAsync);
            ExportCsvCommand = new AsyncRelayCommand(ExportCsvAsync, () => _currentReport != null);
            ExportPdfCommand = new AsyncRelayCommand(ExportPdfAsync, () => _currentReport != null);
        }

        private DateTime FromUtc => FromDate.Date.ToUniversalTime();

        private DateTime ToUtc => ToDate.Date.AddDays(1).AddTicks(-1).ToUniversalTime();

        public async Task GenerateAsync()
        {
            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();
                ReportTable report;

                switch (_selectedOption.Kind)
                {
                    case ReportKind.LowStock:
                        report = ReportingService.LowStock(await LoadItemsAsync(db), _lowStockThreshold);
                        break;

                    case ReportKind.ExpiringSoon:
                        report = ReportingService.ExpiringSoon(await LoadItemsAsync(db), _expiringSoonDays, DateTime.Today);
                        break;

                    case ReportKind.MovementHistory:
                    {
                        if (ToDate.Date < FromDate.Date)
                        {
                            StatusText = "'To' date must be on or after the 'From' date.";
                            return;
                        }

                        var from = FromUtc;
                        var to = ToUtc;
                        var movements = await db.StockMovements.AsNoTracking()
                            .Where(m => m.TimestampUtc >= from && m.TimestampUtc <= to)
                            .ToListAsync();
                        var names = await db.Items.AsNoTracking()
                            .ToDictionaryAsync(i => i.Id, i => i.Name);
                        report = ReportingService.MovementHistory(movements, from, to, names);
                        break;
                    }

                    default:
                        report = ReportingService.StockOnHand(await LoadItemsAsync(db));
                        break;
                }

                CurrentReport = report;
                StatusText = $"{report.Title}: {report.RowCount} row(s) generated.";
                CommandManager.InvalidateRequerySuggested();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to generate report {Kind}", _selectedOption.Kind);
                StatusText = $"Failed to generate report: {ex.Message}";
            }
        }

        private static Task<List<Item>> LoadItemsAsync(KitchenInventoryDbContext db)
            => db.Items.AsNoTracking().Include(i => i.Category).Include(i => i.Location).ToListAsync();

        private async Task ExportCsvAsync()
        {
            // Guarded by ExportCsvCommand.CanExecute (a report must exist).
            var report = _currentReport!;

            try
            {
                var saved = await _fileSave.SaveTextAsAsync($"{Slug(report.Title)}.csv", ReportExporter.ToCsv(report));
                StatusText = saved
                    ? $"Exported {report.Title} to CSV ({report.RowCount} row(s))."
                    : "CSV export cancelled.";
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to export report to CSV");
                StatusText = $"Failed to export CSV: {ex.Message}";
            }
        }

        private async Task ExportPdfAsync()
        {
            // Guarded by ExportPdfCommand.CanExecute (a report must exist).
            var report = _currentReport!;

            try
            {
                var bytes = ReportExporter.ToPdf(report);
                var saved = await _fileSave.SaveBytesAsAsync($"{Slug(report.Title)}.pdf", bytes, PdfFilter, ".pdf");
                StatusText = saved
                    ? $"Exported {report.Title} to PDF ({report.RowCount} row(s))."
                    : "PDF export cancelled.";
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to export report to PDF");
                StatusText = $"Failed to export PDF: {ex.Message}";
            }
        }

        private static string Slug(string title)
        {
            var sb = new StringBuilder();
            foreach (var ch in (title ?? string.Empty).ToLower(CultureInfo.InvariantCulture))
            {
                if (char.IsLetterOrDigit(ch)) sb.Append(ch);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            return sb.ToString().Trim('-');
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private sealed class AsyncRelayCommand : ICommand
        {
            private readonly Func<Task> _executeAsync;
            private readonly Func<bool>? _canExecute;
            private bool _running;
            public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
            { _executeAsync = executeAsync; _canExecute = canExecute; }
            public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke() ?? true);
            public async void Execute(object? parameter)
            {
                if (!CanExecute(parameter)) return;
                try { _running = true; CommandManager.InvalidateRequerySuggested(); await _executeAsync(); }
                finally { _running = false; CommandManager.InvalidateRequerySuggested(); }
            }
            public event EventHandler? CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
        }
    }
}
