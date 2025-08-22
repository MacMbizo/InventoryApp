using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using KitchenInventory.Data;
using KitchenInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace KitchenInventory.Desktop.ViewModels;

public class ItemsViewModel : INotifyPropertyChanged
{
    private readonly IDbContextFactory<KitchenInventoryDbContext> _dbFactory;
    private readonly ILogger<ItemsViewModel> _logger;

    public ObservableCollection<Item> Items { get; } = new();

    private ICollectionView? _itemsView;
    public ICollectionView? ItemsView
    {
        get => _itemsView;
        private set { _itemsView = value; OnPropertyChanged(); }
    }

    private string _filterText = string.Empty;
    public string FilterText
    {
        get => _filterText;
        set { if (_filterText != value) { _filterText = value; OnPropertyChanged(); ApplyFilter(); } }
    }

    private int _totalCount;
    public int TotalCount
    {
        get => _totalCount;
        private set { _totalCount = value; OnPropertyChanged(); UpdateStatus(); }
    }

    private int _filteredCount;
    public int FilteredCount
    {
        get => _filteredCount;
        private set { _filteredCount = value; OnPropertyChanged(); UpdateStatus(); }
    }

    private string _statusText = string.Empty;
    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    private DateTime? _lastSavedAtLocal;
    public DateTime? LastSavedAtLocal
    {
        get => _lastSavedAtLocal;
        private set { _lastSavedAtLocal = value; OnPropertyChanged(); UpdateStatus(); }
    }

    private Item? _selectedItem;
    public Item? SelectedItem
    {
        get => _selectedItem;
        set { _selectedItem = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); }
    }

    public ICommand RefreshCommand { get; }
    public ICommand AddItemCommand { get; }
    public ICommand DeleteItemCommand { get; }
    public ICommand SaveChangesCommand { get; }

    public ItemsViewModel(IDbContextFactory<KitchenInventoryDbContext> dbFactory, ILogger<ItemsViewModel> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        AddItemCommand = new RelayCommand(AddItem);
        DeleteItemCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => SelectedItem != null);
        SaveChangesCommand = new AsyncRelayCommand(SaveChangesAsync, () => Items.Count > 0 && AreItemsValid());

        // Initialize view
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        if (ItemsView != null)
        {
            ItemsView.Filter = FilterPredicate;
            using (ItemsView.DeferRefresh())
            {
                ItemsView.SortDescriptions.Clear();
                ItemsView.SortDescriptions.Add(new SortDescription(nameof(Item.Name), ListSortDirection.Ascending));
            }
        }

        Items.CollectionChanged += (_, __) => { UpdateCounts(); CommandManager.InvalidateRequerySuggested(); };
        UpdateCounts();
    }

    private bool FilterPredicate(object obj)
    {
        if (obj is not Item it) return false;
        if (string.IsNullOrWhiteSpace(FilterText)) return true;
        var term = FilterText.Trim();
        return (it.Name?.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0)
               || (it.Unit?.IndexOf(term, StringComparison.CurrentCultureIgnoreCase) >= 0);
    }

    private void ApplyFilter()
    {
        ItemsView?.Refresh();
        UpdateCounts();
        _logger.LogDebug("Applied filter '{FilterText}' -> {FilteredCount}/{TotalCount}", FilterText, FilteredCount, TotalCount);
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateCounts()
    {
        TotalCount = Items.Count;
        FilteredCount = ItemsView?.Cast<object>().Count() ?? Items.Count;
    }

    private void UpdateStatus()
    {
        var baseText = string.IsNullOrWhiteSpace(FilterText)
            ? $"Items: {TotalCount}"
            : $"Items: {FilteredCount}/{TotalCount} (filter: '{FilterText}')";
        var savedText = LastSavedAtLocal.HasValue ? $" | Last saved: {LastSavedAtLocal:yyyy-MM-dd HH:mm}" : string.Empty;
        StatusText = baseText + savedText;
    }

    private bool AreItemsValid()
    {
        foreach (var it in Items)
        {
            if (string.IsNullOrWhiteSpace(it.Name)) return false;
            if (it.Quantity < 0m || it.Quantity > 1000000m) return false;
        }
        return true;
    }

    public async Task LoadAsync()
    {
        _logger.LogInformation("Loading items from database...");
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var items = await db.Items.AsNoTracking().OrderBy(i => i.Name).ToListAsync();
            Items.Clear();
            foreach (var it in items)
                Items.Add(it);
            OnPropertyChanged(nameof(Items));
            ItemsView?.Refresh();
            UpdateCounts();
            _logger.LogInformation("Loaded {Count} items", Items.Count);
            CommandManager.InvalidateRequerySuggested();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load items");
            throw;
        }
    }

    private void AddItem()
    {
        var newItem = new Item { Name = "New Item", Quantity = 1, Unit = "pcs" };
        Items.Add(newItem);
        SelectedItem = newItem;
        ItemsView?.Refresh();
        UpdateCounts();
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task DeleteSelectedAsync()
    {
        if (SelectedItem == null) return;
        var toDelete = SelectedItem;
        Items.Remove(toDelete);
        SelectedItem = null;
        ItemsView?.Refresh();
        UpdateCounts();

        if (toDelete.Id > 0)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            // Get existing quantity before deletion
            var existing = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == toDelete.Id);
            using var tx = await db.Database.BeginTransactionAsync();
            if (existing != null && existing.Quantity != 0)
            {
                var adj = new StockMovement
                {
                    ItemId = existing.Id,
                    Type = MovementType.Adjust,
                    Quantity = Math.Abs(existing.Quantity),
                    Reason = "Delete item",
                    User = Environment.UserName,
                    TimestampUtc = DateTime.UtcNow
                };
                await db.AddAsync(adj);
            }
            db.Attach(toDelete);
            db.Remove(toDelete);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task SaveChangesAsync()
    {
        if (!AreItemsValid())
        {
            _logger.LogWarning("Save aborted due to validation errors");
            return;
        }

        _logger.LogInformation("Saving {Count} items", Items.Count);
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            using var tx = await db.Database.BeginTransactionAsync();

            // Map of existing quantities for delta computation
            var existingMap = await db.Items.AsNoTracking()
                .Where(i => Items.Where(x => x.Id != 0).Select(x => x.Id).Contains(i.Id))
                .ToDictionaryAsync(i => i.Id, i => i.Quantity);

            var movements = new List<StockMovement>();

            foreach (var it in Items)
            {
                if (it.Id == 0)
                {
                    var now = DateTime.UtcNow;
                    it.CreatedAtUtc = now;
                    it.UpdatedAtUtc = now;
                    await db.AddAsync(it);

                    if (it.Quantity != 0)
                    {
                        movements.Add(new StockMovement
                        {
                            Item = it,
                            Type = MovementType.Add,
                            Quantity = Math.Abs(it.Quantity),
                            Reason = "Initial add",
                            User = Environment.UserName,
                            TimestampUtc = DateTime.UtcNow
                        });
                    }
                }
                else
                {
                    var oldQty = existingMap.TryGetValue(it.Id, out var q) ? q : 0m;
                    var delta = it.Quantity - oldQty;
                    it.UpdatedAtUtc = DateTime.UtcNow;
                    db.Attach(it);
                    db.Entry(it).State = EntityState.Modified;

                    if (delta != 0)
                    {
                        movements.Add(new StockMovement
                        {
                            ItemId = it.Id,
                            Type = delta > 0 ? MovementType.Add : MovementType.Consume,
                            Quantity = Math.Abs(delta),
                            Reason = "Manual edit",
                            User = Environment.UserName,
                            TimestampUtc = DateTime.UtcNow
                        });
                    }
                }
            }

            if (movements.Count > 0)
                await db.AddRangeAsync(movements);

            await db.SaveChangesAsync();
            await tx.CommitAsync();
            _logger.LogInformation("Save successful");

            LastSavedAtLocal = DateTime.Now;

            await LoadAsync();
            ApplyFilter();
            CommandManager.InvalidateRequerySuggested();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save changes");
            throw;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // Basic RelayCommand implementations
    private sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool>? _canExecute;
        public RelayCommand(Action execute, Func<bool>? canExecute = null)
        { _execute = execute; _canExecute = canExecute; }
        public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
        public void Execute(object? parameter) => _execute();
        public event EventHandler? CanExecuteChanged { add { CommandManager.RequerySuggested += value; } remove { CommandManager.RequerySuggested -= value; } }
    }

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