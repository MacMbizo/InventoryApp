using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
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
using KitchenInventory.Desktop.Constants;
using KitchenInventory.Domain.Constants;

namespace KitchenInventory.Desktop.ViewModels;

public class ItemsViewModel : INotifyPropertyChanged
{
    /// <summary>How long the "removed · Undo" strip stays on screen.</summary>
    private static readonly TimeSpan UndoWindow = TimeSpan.FromSeconds(10);

    /// <summary>Reason recorded on the movement that writes off an item's stock when it is removed.</summary>
    private const string RemoveWriteOffReason = "Removed item";

    private readonly IDbContextFactory<KitchenInventoryDbContext> _dbFactory;
    private readonly ILogger<ItemsViewModel> _logger;
    private readonly IFileSaveService _fileSave;
    private readonly IFileOpenService _fileOpen;
    private readonly ICsvImportService _csvImport;
    private readonly IPreferencesService? _preferences;
    private readonly IDialogService? _dialogs;
    private readonly ISessionService? _session;
    private decimal _lowStockThreshold;
    private int _expiringSoonDays;

    /// <summary>The item removed most recently, kept so it can be put straight back.</summary>
    private Item? _removedItem;
    private string _undoMessage = string.Empty;
    private DispatcherTimer? _undoTimer;

    public ObservableCollection<Item> Items { get; } = new();
    public ObservableCollection<StockMovement> SelectedMovements { get; } = new();
    public ObservableCollection<StockMovement> RecentMovements { get; } = new();
    public ObservableCollection<Category> Categories { get; } = new();
    public ObservableCollection<Location> Locations { get; } = new();
    public ObservableCollection<InventoryAlert> Alerts { get; } = new();

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

    private int _alertsCount;
    public int AlertsCount
    {
        get => _alertsCount;
        private set { if (_alertsCount != value) { _alertsCount = value; OnPropertyChanged(); } }
    }

    private int _criticalAlertsCount;
    public int CriticalAlertsCount
    {
        get => _criticalAlertsCount;
        private set { if (_criticalAlertsCount != value) { _criticalAlertsCount = value; OnPropertyChanged(); } }
    }

    private int _lowStockCount;
    public int LowStockCount
    {
        get => _lowStockCount;
        private set { if (_lowStockCount != value) { _lowStockCount = value; OnPropertyChanged(); } }
    }

    private int _expiringSoonCount;
    public int ExpiringSoonCount
    {
        get => _expiringSoonCount;
        private set { if (_expiringSoonCount != value) { _expiringSoonCount = value; OnPropertyChanged(); } }
    }

    private int _expiredCount;
    public int ExpiredCount
    {
        get => _expiredCount;
        private set { if (_expiredCount != value) { _expiredCount = value; OnPropertyChanged(); } }
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

    /// <summary>Banner text shown after a removal ("Rice was removed."); empty when there is nothing to undo.</summary>
    public string UndoMessage
    {
        get => _undoMessage;
        private set { if (_undoMessage != value) { _undoMessage = value; OnPropertyChanged(); } }
    }

    /// <summary>True while the most recent removal can still be put back.</summary>
    public bool CanUndoRemove => _removedItem != null;

    /// <summary>Item id to name, including items that are no longer in the visible list.</summary>
    public IReadOnlyDictionary<int, string> MovementItemNames { get; private set; }
        = new Dictionary<int, string>();

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

    private int? _selectedLocationId;
    public int? SelectedLocationId
    {
        get => _selectedLocationId;
        set
        {
            if (_selectedLocationId != value)
            {
                _selectedLocationId = value;
                OnPropertyChanged();
                _preferences?.Set("ui.selectedLocationId", value ?? 0);
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
    public ICommand UndoRemoveCommand { get; }
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

    // Location Management
    public ICommand ManageLocationsCommand { get; }

    public ItemsViewModel(IDbContextFactory<KitchenInventoryDbContext> dbFactory, ILogger<ItemsViewModel> logger, IFileSaveService fileSave, IFileOpenService fileOpen, ICsvImportService csvImport, IConfiguration? configuration = null, IPreferencesService? preferences = null, IDialogService? dialogs = null, ISessionService? session = null)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _fileSave = fileSave;
        _fileOpen = fileOpen;
        _csvImport = csvImport;
        _preferences = preferences;
        _dialogs = dialogs;
        _session = session;
        _logger.LogInformation("ItemsViewModel constructed. DialogService injected: {HasDialog}", _dialogs != null);
        if (_session != null)
        {
            _session.SessionChanged += (_, __) => { CommandManager.InvalidateRequerySuggested(); };
        }

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
        var prefLoc = _preferences?.Get<int>("ui.selectedLocationId", 0) ?? 0;
        _selectedLocationId = (prefLoc > 0) ? prefLoc : (int?)null;
        _crashReportingEnabled = _preferences?.Get<bool>("privacy.errorReportingEnabled", false) ?? false;

        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        AddItemCommand = _dialogs != null
            ? new AsyncRelayCommand(AddItemAsync, () => HasRole(Roles.Admin, Roles.Manager))
            : new RelayCommand(AddItem, () => HasRole(Roles.Admin, Roles.Manager));
        DeleteItemCommand = new AsyncRelayCommand(DeleteSelectedAsync, () => SelectedItem != null && HasRole(Roles.Admin));
        UndoRemoveCommand = new AsyncRelayCommand(UndoRemoveAsync, () => _removedItem != null);
        SaveChangesCommand = new AsyncRelayCommand(SaveChangesAsync, () => Items.Count > 0 && AreItemsValid() && !HasValidationErrors && HasRole(Roles.Admin, Roles.Manager));
        ImportItemsCsvCommand = new AsyncRelayCommand(ImportItemsCsvAsync, () => HasRole(Roles.Admin, Roles.Manager));
        ExportItemsCsvCommand = new AsyncRelayCommand(ExportItemsCsvAsync, () => ItemsView?.Cast<object>().Any() ?? false);
        ExportSelectedMovementsCsvCommand = new AsyncRelayCommand(ExportSelectedMovementsCsvAsync, () => SelectedItem != null && SelectedMovements.Count > 0);
        ExportRecentMovementsCsvCommand = new AsyncRelayCommand(ExportRecentMovementsCsvAsync, () => RecentMovements.Count > 0);

        // Initialize stock operation commands
        AddStockCommand = new AsyncRelayCommand(() => ShowStockOperationDialogAsync(StockOperations.TakeIn), () => SelectedItem != null && HasRole(Roles.Admin, Roles.Manager));
        ConsumeStockCommand = new AsyncRelayCommand(() => ShowStockOperationDialogAsync(StockOperations.TakeOut), () => SelectedItem != null && HasRole(Roles.Admin, Roles.Manager, Roles.Staff));
        AdjustStockCommand = new AsyncRelayCommand(() => ShowStockOperationDialogAsync(StockOperations.Recount), () => SelectedItem != null && HasRole(Roles.Admin, Roles.Manager));

        // Initialize category management command
        ManageCategoriesCommand = new AsyncRelayCommand(ShowManageCategoriesDialogAsync, () => HasRole(Roles.Admin, Roles.Manager));
        // Initialize location management command
        ManageLocationsCommand = new AsyncRelayCommand(ShowManageLocationsDialogAsync, () => HasRole(Roles.Admin, Roles.Manager));
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

        // Location filter
        if (SelectedLocationId.HasValue && SelectedLocationId.Value > 0)
        {
            if (it.LocationId != SelectedLocationId.Value) return false;
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
        RefreshAlerts();
    }

    /// <summary>
    /// Rebuilds the alerts collection from the currently loaded items.
    /// Exposed publicly so a background timer can refresh day-based alerts (e.g. across midnight).
    /// </summary>
    public void RefreshAlerts()
    {
        var alerts = InventoryAlertService.Evaluate(Items, _lowStockThreshold, _expiringSoonDays, DateTime.Today);

        Alerts.Clear();
        foreach (var a in alerts) Alerts.Add(a);

        AlertsCount = alerts.Count;
        CriticalAlertsCount = alerts.Count(a => a.Severity == AlertSeverity.Critical);
        LowStockCount = alerts.Count(a => a.Kind == AlertKind.LowStock || a.Kind == AlertKind.OutOfStock);
        ExpiringSoonCount = alerts.Count(a => a.Kind == AlertKind.ExpiringSoon);
        ExpiredCount = alerts.Count(a => a.Kind == AlertKind.Expired);
    }

    private void UpdateStatus()
    {
        var baseText = string.IsNullOrWhiteSpace(FilterText)
            ? PluralText.Count(TotalCount, "item", "items")
            : $"{FilteredCount}/{TotalCount} items, showing '{FilterText.Trim()}'";
        var needsText = NeedsAttentionCount switch
        {
            0 => string.Empty,
            1 => " · 1 needs attention",
            _ => $" · {NeedsAttentionCount} need attention"
        };
        var savedText = LastSavedAtLocal.HasValue ? $" · Saved {LastSavedAtLocal:HH:mm}" : string.Empty;
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

            // Load locations
            var locs = await db.Locations.AsNoTracking().OrderBy(l => l.Name).ToListAsync();
            Locations.Clear();
            Locations.Add(new Location { Id = 0, Name = "All" });
            foreach (var l in locs) Locations.Add(l);
            OnPropertyChanged(nameof(Locations));

            // Load items with Category and Location included for display
            var items = await db.Items.AsNoTracking().Include(i => i.Category).Include(i => i.Location).OrderBy(i => i.Name).ToListAsync();
            Items.Clear();
            foreach (var it in items)
                Items.Add(it);
            OnPropertyChanged(nameof(Items));
            ItemsView?.Refresh();
            UpdateCounts();
            _logger.LogInformation("Loaded {Count} {Noun}", Items.Count, PluralText.Noun(Items.Count, "item", "items"));
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
            var locList = Locations.Where(l => l.Id > 0).OrderBy(l => l.Name).ToList();
            var dialog = new AddItemDialog(catList, locList);
            var result = await _dialogs.ShowAsync(dialog);
            if (result is Item newItem)
            {
                if (!newItem.CategoryId.HasValue && SelectedCategoryId.HasValue && SelectedCategoryId.Value > 0)
                    newItem.CategoryId = SelectedCategoryId.Value;
                if (!newItem.LocationId.HasValue && SelectedLocationId.HasValue && SelectedLocationId.Value > 0)
                    newItem.LocationId = SelectedLocationId.Value;

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
            StatusText = $"Could not add that item: {ex.Message}";
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

        // Ask first. Removing writes off whatever stock is left and hides the item, so the
        // user sees what they picked and what it will cost before anything happens.
        if (_dialogs != null)
        {
            var confirmed = await _dialogs.ShowAsync(new RemoveItemDialog(toDelete));
            if (confirmed is not true)
            {
                StatusText = $"Kept {DisplayName(toDelete)}.";
                return;
            }
        }

        // Keep enough of the row to put it back during the undo window.
        var removedSnapshot = new Item
        {
            Id = toDelete.Id,
            Name = toDelete.Name,
            Unit = toDelete.Unit,
            Quantity = toDelete.Quantity
        };

        Items.Remove(toDelete);
        SelectedItem = null;
        ItemsView?.Refresh();
        UpdateCounts();

        if (toDelete.Id > 0)
        {
            try
            {
                using var db = await _dbFactory.CreateDbContextAsync();
                using var tx = await db.Database.BeginTransactionAsync();

                var existing = await db.Items.FirstOrDefaultAsync(i => i.Id == toDelete.Id);
                if (existing != null)
                {
                    removedSnapshot.Quantity = existing.Quantity;
                    if (existing.Quantity != 0)
                    {
                        await db.AddAsync(new StockMovement
                        {
                            ItemId = existing.Id,
                            Type = MovementType.Adjust,
                            // Signed delta: deleting an item writes off its whole stock.
                            Quantity = -existing.Quantity,
                            Reason = RemoveWriteOffReason,
                            User = Environment.UserName,
                            TimestampUtc = DateTime.UtcNow
                        });
                    }

                    // Soft delete: keep the row and its movement history for audit, but hide it
                    // from every read path (global query filter) and write the stock off so the
                    // ledger invariant still holds for the hidden row.
                    existing.IsDeleted = true;
                    existing.Quantity = 0m;
                    await db.SaveChangesAsync();
                }

                await tx.CommitAsync();
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "Delete aborted: item {ItemId} changed since it was loaded", toDelete.Id);
                // Reload first: UpdateStatus() rewrites StatusText, so report after it.
                await LoadAsync();
                ApplyFilter();
                StatusText = $"Could not remove {DisplayName(toDelete)}: it was changed elsewhere. The list has been refreshed.";
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete item {ItemId}", toDelete.Id);
                await LoadAsync();
                ApplyFilter();
                StatusText = $"Could not remove {DisplayName(toDelete)}: {ex.Message}";
                return;
            }
        }
        CommandManager.InvalidateRequerySuggested();

        // Update movement panels post-delete
        await LoadRecentMovementsAsync();
        await LoadSelectedMovementsAsync();

        OfferUndo(removedSnapshot);
    }

    /// <summary>Shows the "removed · Undo" strip for <see cref="UndoWindow"/>.</summary>
    private void OfferUndo(Item removed)
    {
        _undoTimer?.Stop();
        _removedItem = removed;
        UndoMessage = $"{DisplayName(removed)} was removed.";
        OnPropertyChanged(nameof(CanUndoRemove));
        CommandManager.InvalidateRequerySuggested();

        _undoTimer = new DispatcherTimer { Interval = UndoWindow };
        _undoTimer.Tick += (_, __) =>
        {
            _undoTimer?.Stop();
            ClearUndo();
        };
        _undoTimer.Start();
    }

    private void ClearUndo()
    {
        _undoTimer?.Stop();
        _undoTimer = null;
        _removedItem = null;
        UndoMessage = string.Empty;
        OnPropertyChanged(nameof(CanUndoRemove));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>Puts back the most recently removed item, exactly reversing the write-off.</summary>
    private async Task UndoRemoveAsync()
    {
        var removed = _removedItem;
        if (removed == null) return;

        ClearUndo();

        try
        {
            using var db = await _dbFactory.CreateDbContextAsync();
            using var tx = await db.Database.BeginTransactionAsync();

            // The row is hidden by the global query filter, so it has to be asked for explicitly.
            var item = await db.Items.IgnoreQueryFilters().FirstOrDefaultAsync(i => i.Id == removed.Id);
            if (item != null)
            {
                var writeOff = await db.StockMovements
                    .Where(m => m.ItemId == removed.Id && m.Reason == RemoveWriteOffReason)
                    .OrderByDescending(m => m.Id)
                    .FirstOrDefaultAsync();
                if (writeOff != null) db.StockMovements.Remove(writeOff);

                item.IsDeleted = false;
                item.Quantity = removed.Quantity;
                item.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }

            await tx.CommitAsync();

            // Reload before reporting: UpdateStatus() rewrites StatusText.
            await LoadAsync();
            ApplyFilter();
            StatusText = $"{DisplayName(removed)} is back in your list.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to undo the removal of item {ItemId}", removed.Id);
            await LoadAsync();
            ApplyFilter();
            StatusText = $"Could not put {DisplayName(removed)} back. Please add it again.";
        }
    }

    private static string DisplayName(Item item)
        => string.IsNullOrWhiteSpace(item.Name) ? "Item" : item.Name.Trim();

    private async Task SaveChangesAsync()
    {
        if (!AreItemsValid())
        {
            _logger.LogWarning("Save aborted due to validation errors");
            return;
        }

        _logger.LogInformation("Saving {Count} {Noun}", Items.Count, PluralText.Noun(Items.Count, "item", "items"));
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
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Save aborted: an item changed since it was loaded");
            // Reload first: UpdateStatus() rewrites StatusText, so report after it.
            await LoadAsync();
            ApplyFilter();
            StatusText = "Could not save: an item was changed elsewhere. The list has been refreshed.";
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save changes");
            StatusText = $"Could not save your changes: {ex.Message}";
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

            // Names are looked up separately because the movement rows only store an id, and the
            // item may since have been removed (hidden by the global query filter).
            var names = await db.Items.IgnoreQueryFilters().AsNoTracking()
                .Select(i => new { i.Id, i.Name })
                .ToListAsync();
            MovementItemNames = names
                .GroupBy(n => n.Id)
                .ToDictionary(
                    g => g.Key,
                    g => string.IsNullOrWhiteSpace(g.First().Name) ? "(unnamed item)" : g.First().Name);
            OnPropertyChanged(nameof(MovementItemNames));

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
            // Locations available for parsing (exclude sentinel All with Id=0)
            var availableLocations = Locations.Where(l => l.Id > 0).ToList();

            // First, parse via ICsvImportService to keep legacy/test behavior
            var parsed = (await _csvImport.ParseItemsAsync(text, availableCategories, availableLocations)).ToList();
            if (parsed.Count == 0)
            {
                StatusText = "That file had no items in it.";
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
                var preview = await previewAnalyzer.AnalyzeAsync(text, availableCategories, existingItems, availableLocations);

                var previewDialog = new CsvImportPreviewDialog(preview, _fileSave);
                WindowOwnerHelper.SetSafeOwner(previewDialog, System.Windows.Application.Current.MainWindow);
                var dialogResult = await _dialogs.ShowAsync(previewDialog);
                if (!(dialogResult is bool b && b))
                {
                    StatusText = "Import canceled — nothing was changed.";
                    return;
                }

                // Use only valid items for the actual import
                parsed = preview.ValidItems;
                if (parsed.Count == 0)
                {
                    StatusText = "None of the rows in that file could be imported.";
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
                        LocationId = imp.LocationId, // may be null
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
                    target.LocationId = imp.LocationId; // allow updating location if provided
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

            StatusText = $"Imported {PluralText.Count(parsed.Count, "item", "items")}: {addedCount} new, {updatedCount} updated.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import items from CSV");
            StatusText = $"Could not import that file: {ex.Message}";
        }
    }

    // Stock Operation Dialog Handler
    private async Task ShowStockOperationDialogAsync(string operationType)
    {
        if (SelectedItem == null) return;
        // Capture before the reload below; LoadAsync() clears Items and nulls SelectedItem.
        var itemName = SelectedItem.Name;

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
                StatusText = $"{operationType} recorded for {itemName}.";
                
                _logger.LogInformation("Stock operation '{Operation}' completed for item '{ItemName}'", 
                    operationType, itemName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open stock operation dialog");
            StatusText = $"Could not open the '{operationType}' window: {ex.Message}";
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
                StatusText = "Categories saved.";
                _logger.LogInformation("Categories were updated via management dialog");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open category management dialog");
            StatusText = $"Could not open Manage categories: {ex.Message}";
        }
    }

    // Location Management
    private async Task ShowManageLocationsDialogAsync()
    {
        try
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });
            var dialogLogger = loggerFactory.CreateLogger<LocationManagementDialog>();

            var dialog = new LocationManagementDialog(_dbFactory, dialogLogger);
            WindowOwnerHelper.SetSafeOwner(dialog, System.Windows.Application.Current.MainWindow);

            var result = dialog.ShowDialog();
            if (result == true)
            {
                // Reload items and locations after changes
                await LoadAsync();
                ApplyFilter();
                CommandManager.InvalidateRequerySuggested();
                StatusText = "Locations saved.";
                _logger.LogInformation("Locations were updated via management dialog");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open location management dialog");
            StatusText = $"Could not open Manage locations: {ex.Message}";
        }
    }

    private bool HasRole(params string[] roles)
    {
        // If no session service is available (e.g., unit tests), allow operations to preserve legacy behavior
        if (_session == null) return true;
        var role = _session.CurrentRole;
        return _session.IsAuthenticated && !string.IsNullOrWhiteSpace(role) && roles.Contains(role);
    }
}