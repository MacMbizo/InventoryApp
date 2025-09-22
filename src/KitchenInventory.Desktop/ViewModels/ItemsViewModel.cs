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
using KitchenInventory.Desktop.Services;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using KitchenInventory.Desktop;
using KitchenInventory.Desktop.ViewModels;
using KitchenInventory.Desktop.Utilities;

namespace KitchenInventory.Desktop.ViewModels;

public class ItemsViewModel : INotifyPropertyChanged
{
    private readonly IDbContextFactory<KitchenInventoryDbContext> _dbFactory;
    private readonly ILogger<ItemsViewModel> _logger;
    private readonly IFileSaveService _fileSave;
    private readonly IFileOpenService _fileOpen;
    private readonly ICsvImportService _csvImport;
    private readonly IPreferencesService? _preferences;
    private readonly IDialogService? _dialogs;
    private decimal _lowStockThreshold;
    private int _expiringSoonDays;

    public ObservableCollection<Item> Items { get; } = new();
    public ObservableCollection<StockMovement> SelectedMovements { get; } = new();
    public ObservableCollection<StockMovement> RecentMovements { get; } = new();
    public ObservableCollection<Category> Categories { get; } = new();

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

    private bool _showNeedsAttentionOnly;
    public bool ShowNeedsAttentionOnly
    {
        get => _showNeedsAttentionOnly;
        set {
            if (_showNeedsAttentionOnly != value)
            {
                _showNeedsAttentionOnly = value;
                OnPropertyChanged();
                ApplyFilter();
                // persist preference
                _preferences?.Set("ui.showNeedsAttentionOnly", value);
                _preferences?.Save();
            }
        }
    }

    private bool _crashReportingEnabled;
    public bool CrashReportingEnabled
    {
        get => _crashReportingEnabled;
        set
        {
            if (_crashReportingEnabled != value)
            {
                _crashReportingEnabled = value;
                OnPropertyChanged();
                _preferences?.Set("privacy.errorReportingEnabled", value);
                _preferences?.Save();
            }
        }
    }

    public decimal LowStockThreshold
    {
        get => _lowStockThreshold;
        set
        {
            if (_lowStockThreshold != value)
            {
                _lowStockThreshold = value;
                OnPropertyChanged();
                _preferences?.Set("inventory.lowStockThreshold", value);
                _preferences?.Save();
                ApplyFilter();
            }
        }
    }

    public int ExpiringSoonDays
    {
        get => _expiringSoonDays;
        set
        {
            if (_expiringSoonDays != value)
            {
                _expiringSoonDays = value;
                OnPropertyChanged();
                _preferences?.Set("inventory.expiringSoonDays", value);
                _preferences?.Save();
                ApplyFilter();
            }
        }
    }

    private int _needsAttentionCount;
    public int NeedsAttentionCount
    {
        get => _needsAttentionCount;
        private set { _needsAttentionCount = value; OnPropertyChanged(); UpdateStatus(); }
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

    private bool _hasValidationErrors;
    public bool HasValidationErrors
    {
        get => _hasValidationErrors;
        set { if (_hasValidationErrors != value) { _hasValidationErrors = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); } }
    }

    private int? _selectedCategoryId;
    public int? SelectedCategoryId
    {
        get => _selectedCategoryId;
        set
        {
            if (_selectedCategoryId != value)
            {
                _selectedCategoryId = value;
                OnPropertyChanged();
                _preferences?.Set("ui.selectedCategoryId", value ?? 0);
                _preferences?.Save();
                ApplyFilter();
            }
        }
    }

    private Item? _selectedItem;
    public Item? SelectedItem
    {
        get => _selectedItem;
        set { _selectedItem = value; OnPropertyChanged(); CommandManager.InvalidateRequerySuggested(); _ = LoadSelectedMovementsAsync(); }
    }

    public ICommand RefreshCommand { get; }
    public ICommand AddItemCommand { get; }
    public ICommand DeleteItemCommand { get; }
    public ICommand SaveChangesCommand { get; }
    public ICommand ImportItemsCsvCommand { get; }
    public ICommand ExportItemsCsvCommand { get; }
    public ICommand ExportSelectedMovementsCsvCommand { get; }
    public ICommand ExportRecentMovementsCsvCommand { get; }
    
    // New Stock Operation Commands
    public ICommand AddStockCommand { get; }
    public ICommand ConsumeStockCommand { get; }
    public ICommand AdjustStockCommand { get; }

    // Category Management
    public ICommand ManageCategoriesCommand { get; }

    public ItemsViewModel(IDbContextFactory<KitchenInventoryDbContext> dbFactory, ILogger<ItemsViewModel> logger, IFileSaveService fileSave, IFileOpenService fileOpen, ICsvImportService csvImport, IConfiguration? configuration = null, IPreferencesService? preferences = null, IDialogService? dialogs = null)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _fileSave = fileSave;
        _fileOpen = fileOpen;
        _csvImport = csvImport;
        _preferences = preferences;
        _dialogs = dialogs;
        _logger.LogInformation("ItemsViewModel constructed. DialogService injected: {HasDialog}", _dialogs != null);

        // thresholds from preferences > config; defaults: LowStockThreshold=5, ExpiringSoonDays=7
        var prefLow = _preferences?.Get<decimal>("inventory.lowStockThreshold", default);
        var prefExp = _preferences?.Get<int>("inventory.expiringSoonDays", default);
        var lowStock = configuration?["Inventory:LowStockThreshold"];
        var expSoon = configuration?["Inventory:ExpiringSoonDays"];
        _lowStockThreshold = prefLow.HasValue && prefLow.Value > 0 ? prefLow.Value : (decimal.TryParse(lowStock, NumberStyles.Number, CultureInfo.InvariantCulture, out var ls) ? ls : 5m);
        _expiringSoonDays = prefExp.HasValue && prefExp.Value > 0 ? prefExp.Value : (int.TryParse(expSoon, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ed) ? ed : 7);

        // Load UI preference
        _showNeedsAttentionOnly = _preferences?.Get<bool>("ui.showNeedsAttentionOnly", false) ?? false;
        var prefCat = _preferences?.Get<int>("ui.selectedCategoryId", 0) ?? 0;
        _selectedCategoryId = (prefCat > 0) ? prefCat : (int?)null;
        _crashReportingEnabled = _preferences?.Get<bool>("privacy.errorReportingEnabled", false) ?? false;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        AddItemCommand = _dialogs != null ? new AsyncRelayCommand(AddItemAsync) : new RelayCommand(AddItem);
        DeleteItemCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => SelectedItem != null);
        SaveChangesCommand = new AsyncRelayCommand(SaveChangesAsync, () => Items.Count > 0 && AreItemsValid() && !HasValidationErrors);
        ImportItemsCsvCommand = new AsyncRelayCommand(ImportItemsCsvAsync);
        ExportItemsCsvCommand = new AsyncRelayCommand(ExportItemsCsvAsync, () => ItemsView?.Cast<object>().Any() ?? false);
        ExportSelectedMovementsCsvCommand = new AsyncRelayCommand(ExportSelectedMovementsCsvAsync, () => SelectedItem != null && SelectedMovements.Count > 0);
        ExportRecentMovementsCsvCommand = new AsyncRelayCommand(ExportRecentMovementsCsvAsync, () => RecentMovements.Count > 0);

        // Initialize stock operation commands
        AddStockCommand = new AsyncRelayCommand(() => ShowStockOperationDialogAsync("Add Stock"), () => SelectedItem != null);
        ConsumeStockCommand = new AsyncRelayCommand(() => ShowStockOperationDialogAsync("Consume Stock"), () => SelectedItem != null);
        AdjustStockCommand = new AsyncRelayCommand(() => ShowStockOperationDialogAsync("Adjust Stock"), () => SelectedItem != null);

        // Initialize category management command
        ManageCategoriesCommand = new AsyncRelayCommand(ShowManageCategoriesDialogAsync);
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
        SelectedMovements.CollectionChanged += (_, __) => { CommandManager.InvalidateRequerySuggested(); };
        RecentMovements.CollectionChanged += (_, __) => { CommandManager.InvalidateRequerySuggested(); };
        UpdateCounts();

        // Seed recent movements on startup
        _ = LoadRecentMovementsAsync();
    }

    private bool FilterPredicate(object obj)
    {
        if (obj is not Item it) return false;
        var matchesText = string.IsNullOrWhiteSpace(FilterText) ||
                          (it.Name?.IndexOf(FilterText.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0) ||
                          (it.Unit?.IndexOf(FilterText.Trim(), StringComparison.CurrentCultureIgnoreCase) >= 0);
        if (!matchesText) return false;

        // Category filter
        if (SelectedCategoryId.HasValue && SelectedCategoryId.Value > 0)
        {
            if (it.CategoryId != SelectedCategoryId.Value) return false;
        }

        if (ShowNeedsAttentionOnly)
        {
            return IsNeedsAttention(it);
        }
        return true;
    }

    private void ApplyFilter()
    {
        ItemsView?.Refresh();
        UpdateCounts();
        _logger.LogDebug("Applied filter '{FilterText}' (NeedsOnly={Needs}) -> {FilteredCount}/{TotalCount}", FilterText, ShowNeedsAttentionOnly, FilteredCount, TotalCount);
        CommandManager.InvalidateRequerySuggested();
    }

    private void UpdateCounts()
    {
        TotalCount = Items.Count;
        FilteredCount = ItemsView?.Cast<object>().Count() ?? Items.Count;
        NeedsAttentionCount = Items.Count(IsNeedsAttention);
    }

    private void UpdateStatus()
    {
        var baseText = string.IsNullOrWhiteSpace(FilterText)
            ? $"Items: {TotalCount}"
            : $"Items: {FilteredCount}/{TotalCount} (filter: '{FilterText}')";
        var needsText = $" | Needs attention: {NeedsAttentionCount}";
        var savedText = LastSavedAtLocal.HasValue ? $" | Last saved: {LastSavedAtLocal:yyyy-MM-dd HH:mm}" : string.Empty;
        StatusText = baseText + needsText + savedText;
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

            // Load categories
            var cats = await db.Categories.AsNoTracking().OrderBy(c => c.Name).ToListAsync();
            Categories.Clear();
            // Insert sentinel "All" option as Id=0
            Categories.Add(new Category { Id = 0, Name = "All" });
            foreach (var c in cats) Categories.Add(c);
            OnPropertyChanged(nameof(Categories));

            // Load items with Category included for display
            var items = await db.Items.AsNoTracking().Include(i => i.Category).OrderBy(i => i.Name).ToListAsync();
            Items.Clear();
            foreach (var it in items)
                Items.Add(it);
            OnPropertyChanged(nameof(Items));
            ItemsView?.Refresh();
            UpdateCounts();
            _logger.LogInformation("Loaded {Count} items", Items.Count);
            CommandManager.InvalidateRequerySuggested();

            // Refresh movement panels
            await LoadRecentMovementsAsync();
            await LoadSelectedMovementsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load items");
            throw;
        }
    }

    private void AddItem()
    {
        _logger.LogWarning("AddItem() direct path executed (no dialog)");
        var newItem = new Item { Name = "New Item", Quantity = 1, Unit = "pcs" };
        Items.Add(newItem);
        SelectedItem = newItem;
        ItemsView?.Refresh();
        UpdateCounts();
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task AddItemAsync()
    {
        if (_dialogs == null)
        {
            _logger.LogWarning("IDialogService is null in AddItemAsync; falling back to direct AddItem");
            AddItem();
            return;
        }

        try
        {
            _logger.LogInformation("AddItemAsync() using dialog flow");
            var catList = Categories.Where(c => c.Id > 0).OrderBy(c => c.Name).ToList();
            var dialog = new AddItemDialog(catList);
            var result = await _dialogs.ShowAsync(dialog);
            if (result is Item newItem)
            {
                if (!newItem.CategoryId.HasValue && SelectedCategoryId.HasValue && SelectedCategoryId.Value > 0)
                    newItem.CategoryId = SelectedCategoryId.Value;

                Items.Add(newItem);
                SelectedItem = newItem;
                ItemsView?.Refresh();
                UpdateCounts();
                CommandManager.InvalidateRequerySuggested();

                await SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add item via dialog");
            StatusText = $"Add failed: {ex.Message}";
        }
    }

    private bool IsNeedsAttention(Item it)
    {
        try
        {
            var lowStock = it.Quantity < _lowStockThreshold;
            var expSoon = it.ExpiryDate.HasValue && it.ExpiryDate.Value.Date <= DateTime.Today.AddDays(_expiringSoonDays);
            return lowStock || expSoon;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to compute NeedsAttention for item {ItemId}", it.Id);
            return false;
        }
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

        // Update movement panels post-delete
        await LoadRecentMovementsAsync();
        await LoadSelectedMovementsAsync();
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
            var existingIds = Items.Where(x => x.Id != 0).Select(x => x.Id).ToList();
            var existingMap = existingIds.Count == 0
                ? new Dictionary<int, decimal>()
                : await db.Items.AsNoTracking()
                    .Where(i => existingIds.Contains(i.Id))
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
            StatusText = $"Save failed: {ex.Message}";
            return;
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

    // Moved inside class: load movements helpers
    private async Task LoadSelectedMovementsAsync(int take = 50)
    {
        try
        {
            SelectedMovements.Clear();
            if (SelectedItem?.Id > 0)
            {
                using var db = await _dbFactory.CreateDbContextAsync();
                var list = await db.StockMovements.AsNoTracking()
                    .Where(m => m.ItemId == SelectedItem.Id)
                    .OrderByDescending(m => m.TimestampUtc)
                    .Take(take)
                    .ToListAsync();
                foreach (var m in list) SelectedMovements.Add(m);
            }
            OnPropertyChanged(nameof(SelectedMovements));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load selected item movements");
        }
    }

    public async Task LoadRecentMovementsAsync(int take = 50)
    {
        try
        {
            RecentMovements.Clear();
            using var db = await _dbFactory.CreateDbContextAsync();
            var list = await db.StockMovements.AsNoTracking()
                .OrderByDescending(m => m.TimestampUtc)
                .Take(take)
                .ToListAsync();
            foreach (var m in list) RecentMovements.Add(m);
            OnPropertyChanged(nameof(RecentMovements));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load recent movements");
        }
    }

    // Export CSV methods
    private async Task ExportItemsCsvAsync()
    {
        var list = ItemsView?.Cast<Item>().ToList() ?? new List<Item>();
        var csv = CsvExportService.ExportItems(list);
        var suggested = $"items-{DateTime.Now:yyyyMMdd-HHmm}.csv";
        await _fileSave.SaveTextAsAsync(suggested, csv);
    }

    private async Task ExportSelectedMovementsCsvAsync()
    {
        var moves = SelectedMovements.ToList();
        var map = new Dictionary<int, string>();
        if (SelectedItem?.Id > 0 && !string.IsNullOrWhiteSpace(SelectedItem.Name))
        {
            map[SelectedItem.Id] = SelectedItem.Name!;
        }
        var csv = CsvExportService.ExportMovements(moves, map);
        var suggested = $"movements-item-{SelectedItem?.Id}-{DateTime.Now:yyyyMMdd-HHmm}.csv";
        await _fileSave.SaveTextAsAsync(suggested, csv);
    }

    private async Task ExportRecentMovementsCsvAsync()
    {
        var moves = RecentMovements.ToList();
        var ids = moves.Where(m => m.ItemId.HasValue).Select(m => m.ItemId!.Value).Distinct().ToList();
        var nameMap = new Dictionary<int, string>();
        if (ids.Count > 0)
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            var pairs = await db.Items.AsNoTracking()
                .Where(i => ids.Contains(i.Id))
                .Select(i => new { i.Id, i.Name })
                .ToListAsync();
            foreach (var p in pairs)
            {
                if (!string.IsNullOrWhiteSpace(p.Name))
                    nameMap[p.Id] = p.Name!;
            }
        }
        var csv = CsvExportService.ExportMovements(moves, nameMap);
        var suggested = $"movements-recent-{DateTime.Now:yyyyMMdd-HHmm}.csv";
        await _fileSave.SaveTextAsAsync(suggested, csv);
    }

    // Import CSV methods
    private async Task ImportItemsCsvAsync()
    {
        try
        {
            var text = await _fileOpen.OpenTextFileAsync("CSV Files|*.csv|All Files|*.*");
            if (text == null) return;

            // Categories available for parsing (exclude sentinel All with Id=0)
            var availableCategories = Categories.Where(c => c.Id > 0).ToList();

            // First, parse via ICsvImportService to keep legacy/test behavior
            var parsed = (await _csvImport.ParseItemsAsync(text, availableCategories)).ToList();
            if (parsed.Count == 0)
            {
                StatusText = "No items found in CSV.";
                return;
            }

            // Load existing items for matching and optional preview
            List<Item> existingItems;

            // If a dialog service is available, run preview and let user confirm; only import valid rows
            if (_dialogs != null)
            {
                using (var dbLoad = await _dbFactory.CreateDbContextAsync())
                {
                    existingItems = await dbLoad.Items.AsNoTracking().ToListAsync();
                }

                var previewAnalyzer = new CsvImportPreviewAnalyzer();
                var preview = await previewAnalyzer.AnalyzeAsync(text, availableCategories, existingItems);

                var previewDialog = new CsvImportPreviewDialog(preview, _fileSave);
                WindowOwnerHelper.SetSafeOwner(previewDialog, System.Windows.Application.Current.MainWindow);
                var dialogResult = await _dialogs.ShowAsync(previewDialog);
                if (!(dialogResult is bool b && b))
                {
                    StatusText = "Import canceled.";
                    return;
                }

                // Use only valid items for the actual import
                parsed = preview.ValidItems;
                if (parsed.Count == 0)
                {
                    StatusText = "No valid items to import.";
                    return;
                }
            }
            else
            {
                using var dbLoad = await _dbFactory.CreateDbContextAsync();
                existingItems = await dbLoad.Items.AsNoTracking().ToListAsync();
            }

            using var db = await _dbFactory.CreateDbContextAsync();
            using var tx = await db.Database.BeginTransactionAsync();

            // Match existing items by Id or case-insensitive Name
            var byId = existingItems.ToDictionary(i => i.Id, i => i);
            var byName = existingItems
                .Where(i => !string.IsNullOrWhiteSpace(i.Name))
                .GroupBy(i => i.Name!.Trim().ToLowerInvariant())
                .ToDictionary(g => g.Key, g => g.First());

            int addedCount = 0;
            int updatedCount = 0;
            var movements = new List<StockMovement>();
            var nowUtc = DateTime.UtcNow;

            foreach (var imp in parsed)
            {
                var name = (imp.Name ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(name)) continue;

                Item? target = null;
                if (imp.Id > 0 && byId.TryGetValue(imp.Id, out var byIdMatch))
                {
                    target = byIdMatch;
                }
                else if (byName.TryGetValue(name.ToLowerInvariant(), out var byNameMatch))
                {
                    target = byNameMatch;
                }

                if (target == null)
                {
                    // New item
                    var entity = new Item
                    {
                        Name = name,
                        Quantity = imp.Quantity,
                        Unit = string.IsNullOrWhiteSpace(imp.Unit) ? "pcs" : imp.Unit!,
                        CategoryId = imp.CategoryId, // may be null
                        ExpiryDate = imp.ExpiryDate,
                        CreatedAtUtc = imp.CreatedAtUtc == default ? nowUtc : imp.CreatedAtUtc,
                        UpdatedAtUtc = nowUtc
                    };
                    await db.Items.AddAsync(entity);

                    if (entity.Quantity != 0)
                    {
                        movements.Add(new StockMovement
                        {
                            Item = entity,
                            Type = MovementType.Add,
                            Quantity = Math.Abs(entity.Quantity),
                            Reason = "Import add",
                            User = Environment.UserName,
                            TimestampUtc = nowUtc
                        });
                    }
                    addedCount++;
                }
                else
                {
                    // Existing item -> update fields and record quantity delta
                    var oldQty = target.Quantity;
                    var newQty = imp.Quantity;
                    var delta = newQty - oldQty;

                    target.Unit = string.IsNullOrWhiteSpace(imp.Unit) ? target.Unit : imp.Unit!;
                    target.ExpiryDate = imp.ExpiryDate;
                    target.CategoryId = imp.CategoryId; // allow updating category if provided
                    target.Quantity = newQty;
                    target.UpdatedAtUtc = nowUtc;

                    db.Attach(target);
                    db.Entry(target).State = EntityState.Modified;

                    if (delta != 0)
                    {
                        movements.Add(new StockMovement
                        {
                            ItemId = target.Id,
                            Type = delta > 0 ? MovementType.Add : MovementType.Consume,
                            Quantity = Math.Abs(delta),
                            Reason = "Import update",
                            User = Environment.UserName,
                            TimestampUtc = nowUtc
                        });
                    }

                    updatedCount++;
                }
            }

            if (movements.Count > 0)
                await db.StockMovements.AddRangeAsync(movements);

            await db.SaveChangesAsync();
            await tx.CommitAsync();

            // Refresh view-model collections to reflect DB state
            await LoadAsync();
            ApplyFilter();
            UpdateCounts();

            StatusText = $"Imported {parsed.Count} items. Added: {addedCount}, Updated: {updatedCount}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import items from CSV");
            StatusText = $"Import failed: {ex.Message}";
        }
    }

    // Stock Operation Dialog Handler
    private async Task ShowStockOperationDialogAsync(string operationType)
    {
        if (SelectedItem == null) return;

        try
        {
            // Create logger factory and logger for the specific dialog
            using var loggerFactory = LoggerFactory.Create(builder => 
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });
            var dialogLogger = loggerFactory.CreateLogger<StockOperationDialog>();

            var dialog = new StockOperationDialog(_dbFactory, dialogLogger, SelectedItem, operationType);
            WindowOwnerHelper.SetSafeOwner(dialog, System.Windows.Application.Current.MainWindow);

            var result = dialog.ShowDialog();
            
            if (result == true)
            {
                // Refresh data after successful operation
                await LoadAsync();
                ApplyFilter();
                CommandManager.InvalidateRequerySuggested();
                
                // Update status to show operation completed
                StatusText = $"{operationType} completed for {SelectedItem.Name}";
                
                _logger.LogInformation("Stock operation '{Operation}' completed for item '{ItemName}'", 
                    operationType, SelectedItem.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open stock operation dialog");
            StatusText = $"Failed to open {operationType.ToLower()} dialog: {ex.Message}";
        }
    }

    // Category Management
    private async Task ShowManageCategoriesDialogAsync()
    {
        try
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });
            var dialogLogger = loggerFactory.CreateLogger<CategoryManagementDialog>();

            var dialog = new CategoryManagementDialog(_dbFactory, dialogLogger);
            WindowOwnerHelper.SetSafeOwner(dialog, System.Windows.Application.Current.MainWindow);

            var result = dialog.ShowDialog();
            if (result == true)
            {
                // Reload items and categories after changes
                await LoadAsync();
                ApplyFilter();
                CommandManager.InvalidateRequerySuggested();
                StatusText = "Categories updated.";
                _logger.LogInformation("Categories were updated via management dialog");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open category management dialog");
            StatusText = $"Failed to open categories dialog: {ex.Message}";
        }
    }
}