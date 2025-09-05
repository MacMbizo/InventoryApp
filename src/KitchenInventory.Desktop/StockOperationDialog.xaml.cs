using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using KitchenInventory.Data;
using KitchenInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KitchenInventory.Desktop
{
    public partial class StockOperationDialog : Window, INotifyPropertyChanged
    {
        private readonly IDbContextFactory<KitchenInventoryDbContext> _dbFactory;
        private readonly ILogger<StockOperationDialog> _logger;
        private static readonly string DecimalSeparator = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        private static readonly Regex QuantityRegex = new($"^\\d*(?:{Regex.Escape(DecimalSeparator)}\\d{{0,3}})?$");

        public ObservableCollection<Item> AvailableItems { get; } = new();
        public ObservableCollection<string> OperationTypes { get; } = new() { "Add Stock", "Consume Stock", "Adjust Stock" };

        private Item? _selectedItem;
        public Item? SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (_selectedItem != value)
                {
                    _selectedItem = value;
                    OnPropertyChanged();
                    UpdateCurrentStock();
                    UpdateCalculation();
                    ValidateInput();
                }
            }
        }

        private string _selectedOperationType = "Add Stock";
        public string SelectedOperationType
        {
            get => _selectedOperationType;
            set
            {
                if (_selectedOperationType != value)
                {
                    _selectedOperationType = value;
                    OnPropertyChanged();
                    UpdateCalculation();
                    ValidateInput();
                }
            }
        }

        private decimal _quantity = 1m;
        public decimal Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity != value)
                {
                    _quantity = value;
                    OnPropertyChanged();
                    UpdateCalculation();
                    ValidateInput();
                }
            }
        }

        private string _reason = string.Empty;
        public string Reason
        {
            get => _reason;
            set
            {
                if (_reason != value)
                {
                    _reason = value;
                    OnPropertyChanged();
                    ValidateInput();
                }
            }
        }

        private string _user = Environment.UserName;
        public string User
        {
            get => _user;
            set
            {
                if (_user != value)
                {
                    _user = value;
                    OnPropertyChanged();
                    ValidateInput();
                }
            }
        }

        private decimal _currentStock;
        public decimal CurrentStock
        {
            get => _currentStock;
            set
            {
                if (_currentStock != value)
                {
                    _currentStock = value;
                    OnPropertyChanged();
                    UpdateCurrentStockDisplay();
                    UpdateCalculation();
                }
            }
        }

        private string _currentStockDisplay = "0 pcs";
        public string CurrentStockDisplay
        {
            get => _currentStockDisplay;
            set
            {
                if (_currentStockDisplay != value)
                {
                    _currentStockDisplay = value;
                    OnPropertyChanged();
                }
            }
        }

        private Brush _currentStockBrush = Brushes.Black;
        public Brush CurrentStockBrush
        {
            get => _currentStockBrush;
            set
            {
                if (_currentStockBrush != value)
                {
                    _currentStockBrush = value;
                    OnPropertyChanged();
                }
            }
        }

        private decimal _newStock;
        public decimal NewStock
        {
            get => _newStock;
            set
            {
                if (_newStock != value)
                {
                    _newStock = value;
                    OnPropertyChanged();
                    UpdateNewStockDisplay();
                }
            }
        }

        private string _newStockDisplay = "0 pcs";
        public string NewStockDisplay
        {
            get => _newStockDisplay;
            set
            {
                if (_newStockDisplay != value)
                {
                    _newStockDisplay = value;
                    OnPropertyChanged();
                }
            }
        }

        private Brush _newStockBrush = Brushes.Black;
        public Brush NewStockBrush
        {
            get => _newStockBrush;
            set
            {
                if (_newStockBrush != value)
                {
                    _newStockBrush = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _hasErrors;
        public bool HasErrors
        {
            get => _hasErrors;
            set
            {
                if (_hasErrors != value)
                {
                    _hasErrors = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _canExecute;
        public bool CanExecute
        {
            get => _canExecute;
            set
            {
                if (_canExecute != value)
                {
                    _canExecute = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _title = "Stock Operation";
        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsItemSelectionEnabled { get; private set; } = true;
        public bool IsOperationSelectable { get; private set; } = true;

        public StockOperationDialog(IDbContextFactory<KitchenInventoryDbContext> dbFactory, ILogger<StockOperationDialog> logger)
        {
            InitializeComponent();
            _dbFactory = dbFactory;
            _logger = logger;
            DataContext = this;
            Loaded += OnLoaded;
        }

        // Constructor for pre-selected item and operation
        public StockOperationDialog(IDbContextFactory<KitchenInventoryDbContext> dbFactory, ILogger<StockOperationDialog> logger, 
            Item selectedItem, string operationType = "Add Stock")
            : this(dbFactory, logger)
        {
            _selectedItem = selectedItem;
            _selectedOperationType = operationType;
            IsItemSelectionEnabled = false;
            IsOperationSelectable = false;
            Title = $"{operationType} - {selectedItem.Name}";
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await LoadAvailableItemsAsync();
                if (SelectedItem != null)
                {
                    await UpdateCurrentStockFromDbAsync();
                }
                UpdateCalculation();
                ValidateInput();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load stock operation dialog");
                MessageBox.Show(this, $"Failed to load data: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadAvailableItemsAsync()
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var items = await db.Items.AsNoTracking()
                .OrderBy(i => i.Name)
                .ToListAsync();

            AvailableItems.Clear();
            foreach (var item in items)
            {
                AvailableItems.Add(item);
            }

            if (SelectedItem != null)
            {
                // Ensure the selected item is in the collection
                var match = AvailableItems.FirstOrDefault(i => i.Id == SelectedItem.Id);
                if (match != null)
                {
                    SelectedItem = match;
                }
            }
        }

        private async Task UpdateCurrentStockFromDbAsync()
        {
            if (SelectedItem?.Id > 0)
            {
                try
                {
                    using var db = await _dbFactory.CreateDbContextAsync();
                    var item = await db.Items.AsNoTracking()
                        .FirstOrDefaultAsync(i => i.Id == SelectedItem.Id);
                    if (item != null)
                    {
                        CurrentStock = item.Quantity;
                        // Update the selected item with fresh data
                        SelectedItem.Quantity = item.Quantity;
                        SelectedItem.Unit = item.Unit;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to refresh current stock for item {ItemId}", SelectedItem.Id);
                }
            }
        }

        private void UpdateCurrentStock()
        {
            if (SelectedItem != null)
            {
                CurrentStock = SelectedItem.Quantity;
            }
            else
            {
                CurrentStock = 0m;
            }
        }

        private void UpdateCurrentStockDisplay()
        {
            var unit = SelectedItem?.Unit ?? "pcs";
            CurrentStockDisplay = $"{CurrentStock:F3} {unit}".TrimEnd('0').TrimEnd('.');
            
            // Color coding: red for zero/negative, orange for low stock
            if (CurrentStock <= 0)
            {
                CurrentStockBrush = Brushes.Red;
            }
            else if (CurrentStock < 5) // Could make this configurable
            {
                CurrentStockBrush = Brushes.Orange;
            }
            else
            {
                CurrentStockBrush = Brushes.Green;
            }
        }

        private void UpdateCalculation()
        {
            if (SelectedItem == null)
            {
                NewStock = 0;
                return;
            }

            switch (SelectedOperationType)
            {
                case "Add Stock":
                    NewStock = CurrentStock + Quantity;
                    break;
                case "Consume Stock":
                    NewStock = CurrentStock - Quantity;
                    break;
                case "Adjust Stock":
                    NewStock = Quantity; // Set to absolute value
                    break;
                default:
                    NewStock = CurrentStock;
                    break;
            }
        }

        private void UpdateNewStockDisplay()
        {
            var unit = SelectedItem?.Unit ?? "pcs";
            NewStockDisplay = $"{NewStock:F3} {unit}".TrimEnd('0').TrimEnd('.');
            
            // Color coding for new stock level
            if (NewStock < 0)
            {
                NewStockBrush = Brushes.Red;
            }
            else if (NewStock < 5)
            {
                NewStockBrush = Brushes.Orange;
            }
            else
            {
                NewStockBrush = Brushes.Green;
            }
        }

        private void ValidateInput()
        {
            HasErrors = false;
            ErrorMessage = string.Empty;
            CanExecute = false;

            // Check if item is selected
            if (SelectedItem == null)
            {
                SetError("Please select an item.");
                return;
            }

            // Check quantity
            if (Quantity <= 0)
            {
                SetError("Quantity must be greater than zero.");
                return;
            }

            // Check for consume operation that would result in negative stock
            if (SelectedOperationType == "Consume Stock" && NewStock < 0)
            {
                SetError($"Cannot consume {Quantity:F3} - insufficient stock (current: {CurrentStock:F3}).");
                return;
            }

            // Check user field
            if (string.IsNullOrWhiteSpace(User))
            {
                SetError("User field is required.");
                return;
            }

            // Check reason for certain operations
            if ((SelectedOperationType == "Adjust Stock" || Quantity > 100) && string.IsNullOrWhiteSpace(Reason))
            {
                SetError("Reason is required for adjustments or large quantities.");
                return;
            }

            // All validations passed
            CanExecute = true;
        }

        private void SetError(string message)
        {
            HasErrors = true;
            ErrorMessage = message;
            CanExecute = false;
        }

        private async void Execute_Click(object sender, RoutedEventArgs e)
        {
            if (!CanExecute || SelectedItem == null) return;

            try
            {
                await ExecuteOperationAsync();
                DialogResult = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to execute stock operation");
                SetError($"Operation failed: {ex.Message}");
            }
        }

        private async Task ExecuteOperationAsync()
        {
            if (SelectedItem == null) return;

            using var db = await _dbFactory.CreateDbContextAsync();
            using var transaction = await db.Database.BeginTransactionAsync();

            try
            {
                // Get current item state from database
                var dbItem = await db.Items.FirstOrDefaultAsync(i => i.Id == SelectedItem.Id);
                if (dbItem == null)
                {
                    throw new InvalidOperationException($"Item with ID {SelectedItem.Id} not found.");
                }

                // Verify current stock matches what we expect (prevent race conditions)
                if (Math.Abs(dbItem.Quantity - CurrentStock) > 0.001m)
                {
                    throw new InvalidOperationException(
                        $"Stock has changed since dialog opened. Current: {dbItem.Quantity:F3}, Expected: {CurrentStock:F3}. Please refresh and try again.");
                }

                // Determine movement type and actual quantity change
                MovementType movementType;
                decimal actualQuantityChange;

                switch (SelectedOperationType)
                {
                    case "Add Stock":
                        movementType = MovementType.Add;
                        actualQuantityChange = Quantity;
                        dbItem.Quantity += Quantity;
                        break;
                    case "Consume Stock":
                        movementType = MovementType.Consume;
                        actualQuantityChange = Quantity;
                        dbItem.Quantity -= Quantity;
                        break;
                    case "Adjust Stock":
                        movementType = MovementType.Adjust;
                        actualQuantityChange = Math.Abs(NewStock - CurrentStock);
                        dbItem.Quantity = NewStock;
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown operation type: {SelectedOperationType}");
                }

                // Update item timestamp
                dbItem.UpdatedAtUtc = DateTime.UtcNow;
                
                // Create stock movement record
                var movement = new StockMovement
                {
                    ItemId = dbItem.Id,
                    Type = movementType,
                    Quantity = actualQuantityChange,
                    Reason = string.IsNullOrWhiteSpace(Reason) ? SelectedOperationType : Reason,
                    User = User,
                    TimestampUtc = DateTime.UtcNow
                };

                await db.StockMovements.AddAsync(movement);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();

                _logger.LogInformation("Stock operation completed: {Operation} {Quantity} for item {ItemId} by {User}",
                    SelectedOperationType, Quantity, SelectedItem.Id, User);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
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

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}