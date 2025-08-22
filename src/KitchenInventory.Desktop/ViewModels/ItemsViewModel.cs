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

    private Item? _selectedItem;
    public Item? SelectedItem
    {
        get => _selectedItem;
        set { _selectedItem = value; OnPropertyChanged(); }
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
        SaveChangesCommand = new AsyncRelayCommand(SaveChangesAsync, () => Items.Count > 0);

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
    }

    private void UpdateCounts()
    {
        TotalCount = Items.Count;
        FilteredCount = ItemsView?.Cast<object>().Count() ?? Items.Count;
    }

    private void UpdateStatus()
    {
        StatusText = string.IsNullOrWhiteSpace(FilterText)
            ? $"Items: {TotalCount}"
            : $"Items: {FilteredCount}/{TotalCount} (filter: '{FilterText}')";
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
            db.Attach(toDelete);
            db.Remove(toDelete);
            await db.SaveChangesAsync();
        }
    }

    private async Task SaveChangesAsync()
    {
        _logger.LogInformation("Saving {Count} items", Items.Count);
        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();

            foreach (var it in Items)
            {
                if (it.Id == 0)
                {
                    var now = DateTime.UtcNow;
                    it.CreatedAtUtc = now;
                    it.UpdatedAtUtc = now;
                    await db.AddAsync(it);
                }
                else
                {
                    it.UpdatedAtUtc = DateTime.UtcNow;
                    db.Attach(it);
                    db.Entry(it).State = EntityState.Modified;
                }
            }

            await db.SaveChangesAsync();
            _logger.LogInformation("Save successful");

            await LoadAsync();
            ApplyFilter();
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