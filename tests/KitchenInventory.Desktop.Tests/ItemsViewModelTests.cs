using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KitchenInventory.Data;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Desktop.ViewModels;
using KitchenInventory.Domain.Constants;
using KitchenInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public class ItemsViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IDbContextFactory<KitchenInventoryDbContext> _factory;
    private int _riceId;

    public ItemsViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"itemsvm-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<KitchenInventoryDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        _factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<KitchenInventoryDbContext>>();

        using var db = _factory.CreateDbContext();
        db.Database.Migrate();

        var grains = new Category { Id = 1, Name = "Grains" };
        var dairy = new Category { Id = 2, Name = "Dairy" };
        var pantry = new Location { Id = 1, Name = "Pantry" };
        db.Categories.AddRange(grains, dairy);
        db.Locations.Add(pantry);

        var rice = new Item { Name = "Rice", Quantity = 10m, Unit = "kg", CategoryId = 1, LocationId = 1, ExpiryDate = DateTime.Today.AddDays(60) };
        var milk = new Item { Name = "Milk", Quantity = 0m, Unit = "L", CategoryId = 2, ExpiryDate = DateTime.Today.AddDays(2) };
        db.Items.AddRange(rice, milk);
        db.SaveChanges();
        _riceId = rice.Id;

        db.StockMovements.Add(new StockMovement
        {
            ItemId = _riceId,
            Type = MovementType.Add,
            Quantity = 10m,
            Reason = "Initial add",
            User = "seed",
            TimestampUtc = DateTime.UtcNow.AddMinutes(-5)
        });
        db.SaveChanges();
    }

    public void Dispose()
    {
        try
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
        catch { }
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private ItemsViewModel Create(
        IPreferencesService? prefs = null,
        IFileSaveService? save = null,
        IFileOpenService? open = null,
        IDialogService? dialogs = null,
        ISessionService? session = null,
        IConfiguration? config = null)
        => new(
            _factory,
            NullLogger<ItemsViewModel>.Instance,
            save ?? new FakeFileSave(),
            open ?? new FakeFileOpen(),
            new CsvImportService(),
            config,
            prefs ?? new FakePreferences(),
            dialogs,
            session);

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 8000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(25);
        }
        Assert.True(condition(), "Expected condition was not met within the timeout.");
    }

    private static void RunCommand(System.Windows.Input.ICommand command)
    {
        Assert.True(command.CanExecute(null));
        command.Execute(null);
    }

    private bool DbHasItem(string name)
    {
        using var db = _factory.CreateDbContext();
        return db.Items.AsNoTracking().Any(i => i.Name == name);
    }

    // ---- fakes -------------------------------------------------------------

    private sealed class FakePreferences : IPreferencesService
    {
        public Dictionary<string, object?> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int SaveCount { get; private set; }
        public T? Get<T>(string key, T? @default = default)
            => Values.TryGetValue(key, out var v) && v is T typed ? typed : @default;
        public void Set<T>(string key, T value) => Values[key] = value;
        public void Save() => SaveCount++;
    }

    private sealed class FakeFileSave : IFileSaveService
    {
        public List<(string Name, string Content)> Saved { get; } = new();
        public Task<bool> SaveTextAsAsync(string suggestedFileName, string content)
        {
            Saved.Add((suggestedFileName, content));
            return Task.FromResult(true);
        }

        public Task<bool> SaveBytesAsAsync(string suggestedFileName, byte[] content, string filter, string defaultExt)
        {
            Saved.Add((suggestedFileName, string.Empty));
            return Task.FromResult(true);
        }
    }

    private sealed class FakeFileOpen : IFileOpenService
    {
        public string? Content { get; set; }
        public Task<string?> OpenTextFileAsync(string filter = "") => Task.FromResult(Content);
    }

    private sealed class FakeDialogService : IDialogService
    {
        public object? Result { get; set; }
        public Task<object?> ShowAsync(System.Windows.Window dialog) => Task.FromResult(Result);
    }

    private sealed class FakeSession : ISessionService
    {
        public string? CurrentUsername { get; private set; }
        public string? CurrentRole { get; private set; }
        public bool IsAuthenticated { get; private set; }
        public event EventHandler? SessionChanged;

        public void SetUser(string username, string role)
        {
            CurrentUsername = username;
            CurrentRole = role;
            IsAuthenticated = true;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Clear()
        {
            CurrentUsername = null;
            CurrentRole = null;
            IsAuthenticated = false;
            SessionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // ---- tests -------------------------------------------------------------

    [Fact]
    public async Task LoadAsync_PopulatesItemsCategoriesLocationsAndCounts()
    {
        var vm = Create();

        await vm.LoadAsync();

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal(3, vm.Categories.Count); // sentinel "All" + 2
        Assert.Equal(2, vm.Locations.Count);  // sentinel "All" + 1
        Assert.Equal(0, vm.Categories[0].Id);
        Assert.Equal(2, vm.TotalCount);
        Assert.Equal(2, vm.FilteredCount);
        Assert.Equal(1, vm.NeedsAttentionCount); // Milk only
        Assert.Contains("2 items", vm.StatusText);
        Assert.NotNull(vm.ItemsView);
        Assert.Contains(vm.ItemsView!.SortDescriptions, sd => sd.PropertyName == nameof(Item.Name));
    }

    [Fact]
    public async Task FilterText_FiltersByName_AndUpdatesStatus()
    {
        var vm = Create();
        await vm.LoadAsync();

        vm.FilterText = "ric";

        Assert.Equal(1, vm.FilteredCount);
        Assert.Contains("1/2", vm.StatusText);
        Assert.Contains("ric", vm.StatusText);
    }

    [Fact]
    public async Task ShowNeedsAttentionOnly_FiltersAndPersistsPreference()
    {
        var prefs = new FakePreferences();
        var vm = Create(prefs: prefs);
        await vm.LoadAsync();

        vm.ShowNeedsAttentionOnly = true;

        Assert.Equal(1, vm.FilteredCount);
        Assert.Equal(true, prefs.Values["ui.showNeedsAttentionOnly"]);
        Assert.True(prefs.SaveCount > 0);
    }

    [Fact]
    public void SettableProperties_PersistPreferences()
    {
        var prefs = new FakePreferences();
        var vm = Create(prefs: prefs);

        vm.LowStockThreshold = 3m;
        vm.ExpiringSoonDays = 14;
        vm.SelectedCategoryId = 1;
        vm.SelectedLocationId = 1;
        vm.CrashReportingEnabled = true;
        vm.HasValidationErrors = true;

        Assert.Equal(3m, prefs.Values["inventory.lowStockThreshold"]);
        Assert.Equal(14, prefs.Values["inventory.expiringSoonDays"]);
        Assert.Equal(1, prefs.Values["ui.selectedCategoryId"]);
        Assert.Equal(1, prefs.Values["ui.selectedLocationId"]);
        Assert.Equal(true, prefs.Values["privacy.errorReportingEnabled"]);
        Assert.True(prefs.SaveCount >= 5);
    }

    [Fact]
    public async Task Constructor_ReadsThresholdsAndSelectionsFromPreferences()
    {
        var prefs = new FakePreferences();
        prefs.Values["inventory.lowStockThreshold"] = 9m;
        prefs.Values["inventory.expiringSoonDays"] = 11;
        prefs.Values["ui.selectedCategoryId"] = 2;
        prefs.Values["ui.selectedLocationId"] = 1;
        prefs.Values["privacy.errorReportingEnabled"] = true;
        prefs.Values["ui.showNeedsAttentionOnly"] = true;

        var vm = Create(prefs: prefs);
        await vm.LoadAsync();

        Assert.Equal(9m, vm.LowStockThreshold);
        Assert.Equal(11, vm.ExpiringSoonDays);
        Assert.Equal(2, vm.SelectedCategoryId);
        Assert.Equal(1, vm.SelectedLocationId);
        Assert.True(vm.CrashReportingEnabled);
        Assert.True(vm.ShowNeedsAttentionOnly);
    }

    [Fact]
    public void Constructor_FallsBackToConfigurationThenDefaults()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Inventory:LowStockThreshold"] = "8",
                ["Inventory:ExpiringSoonDays"] = "21"
            })
            .Build();

        var fromConfig = Create(config: config);
        Assert.Equal(8m, fromConfig.LowStockThreshold);
        Assert.Equal(21, fromConfig.ExpiringSoonDays);

        var defaults = Create();
        Assert.Equal(5m, defaults.LowStockThreshold);
        Assert.Equal(7, defaults.ExpiringSoonDays);
    }

    [Fact]
    public async Task RefreshAlerts_RebuildsAlertCollection()
    {
        var vm = Create();
        await vm.LoadAsync();

        vm.RefreshAlerts();

        Assert.True(vm.AlertsCount >= 1);
        Assert.Equal(vm.AlertsCount, vm.Alerts.Count);
        Assert.True(vm.CriticalAlertsCount <= vm.AlertsCount);
        Assert.True(vm.LowStockCount >= 1);
    }

    [Fact]
    public async Task SaveChangesCommand_PersistsNewItemAndRecordsMovement()
    {
        var vm = Create();
        await vm.LoadAsync();
        vm.Items.Add(new Item { Name = "Beans", Quantity = 4m, Unit = "kg" });

        RunCommand(vm.SaveChangesCommand);
        await WaitUntilAsync(() => DbHasItem("Beans"));

        using var db = _factory.CreateDbContext();
        Assert.True(db.StockMovements.Any(m => m.Reason == "Initial add" && m.Quantity == 4m));
        Assert.NotNull(vm.LastSavedAtLocal);
    }

    [Fact]
    public async Task SaveChangesCommand_RecordsDeltaForEditedItem()
    {
        var vm = Create();
        await vm.LoadAsync();
        var rice = vm.Items.First(i => i.Name == "Rice");
        rice.Quantity = 3m;

        RunCommand(vm.SaveChangesCommand);
        await WaitUntilAsync(() =>
        {
            using var db = _factory.CreateDbContext();
            return db.StockMovements.Any(m => m.Reason == "Manual edit" && m.Type == MovementType.Consume);
        });
    }

    [Fact]
    public async Task DeleteItemCommand_SoftDeletesItemAndRecordsAdjustment()
    {
        var vm = Create();
        await vm.LoadAsync();
        vm.SelectedItem = vm.Items.First(i => i.Name == "Rice");

        RunCommand(vm.DeleteItemCommand);
        await WaitUntilAsync(() =>
        {
            using var db = _factory.CreateDbContext();
            return db.Items.IgnoreQueryFilters().Any(i => i.Id == _riceId && i.IsDeleted);
        });

        // Hidden from every normal read path...
        Assert.False(DbHasItem("Rice"));

        using var db2 = _factory.CreateDbContext();
        // ...but the row survives, with the stock written off.
        var row = db2.Items.IgnoreQueryFilters().Single(i => i.Id == _riceId);
        Assert.True(row.IsDeleted);
        Assert.Equal(0m, row.Quantity);

        var movements = db2.StockMovements.Where(m => m.ItemId == _riceId).ToList();
        Assert.Contains(movements, m => m.Reason == "Removed item" && m.Type == MovementType.Adjust && m.Quantity == -10m);
        // The movement history must not be orphaned by the delete.
        Assert.Contains(movements, m => m.Reason == "Initial add");
    }

    [StaFact]
    public async Task UndoRemoveCommand_PutsRemovedItemBack()
    {
        // The dialog is a real window (constructed before the fake answers), so this must run on an STA thread.
        var dialogs = new FakeDialogService { Result = true };
        var vm = Create(dialogs: dialogs);
        await vm.LoadAsync();
        vm.SelectedItem = vm.Items.First(i => i.Name == "Rice");

        RunCommand(vm.DeleteItemCommand);
        await WaitUntilAsync(() => vm.CanUndoRemove);

        Assert.Contains("Rice", vm.UndoMessage);
        Assert.False(DbHasItem("Rice"));

        RunCommand(vm.UndoRemoveCommand);
        await WaitUntilAsync(() => vm.Items.Any(i => i.Name == "Rice"));

        // The item is visible again...
        Assert.True(DbHasItem("Rice"));
        // ...the undo offer is withdrawn...
        Assert.False(vm.CanUndoRemove);

        using var db = _factory.CreateDbContext();
        // ...the row is un-hidden with its stock restored...
        var row = db.Items.IgnoreQueryFilters().Single(i => i.Id == _riceId);
        Assert.False(row.IsDeleted);
        Assert.Equal(10m, row.Quantity);
        // ...and the write-off movement is gone, so the ledger invariant holds again.
        var movements = db.StockMovements.Where(m => m.ItemId == _riceId).ToList();
        Assert.DoesNotContain(movements, m => m.Reason == "Removed item");
    }

    [StaFact]
    public async Task DeleteItemCommand_KeepsItem_WhenConfirmationDeclined()
    {
        var dialogs = new FakeDialogService { Result = false };
        var vm = Create(dialogs: dialogs);
        await vm.LoadAsync();
        vm.SelectedItem = vm.Items.First(i => i.Name == "Rice");

        RunCommand(vm.DeleteItemCommand);
        await WaitUntilAsync(() => vm.StatusText.Contains("Kept"));

        Assert.True(DbHasItem("Rice"));
        Assert.False(vm.CanUndoRemove);
    }

    [Fact]
    public async Task SaveChangesCommand_AssignsAndRotatesRowVersion()
    {
        Guid first;
        using (var db = _factory.CreateDbContext())
        {
            first = db.Items.AsNoTracking().Single(i => i.Id == _riceId).RowVersion;
        }
        Assert.NotEqual(Guid.Empty, first);

        using (var db = _factory.CreateDbContext())
        {
            var rice = await db.Items.SingleAsync(i => i.Id == _riceId);
            rice.Quantity = 7m;
            await db.SaveChangesAsync();
        }

        using (var db = _factory.CreateDbContext())
        {
            Assert.NotEqual(first, db.Items.AsNoTracking().Single(i => i.Id == _riceId).RowVersion);
        }
    }

    [Fact]
    public async Task SaveChangesCommand_ReportsConflict_WhenItemChangedElsewhere()
    {
        var vm = Create();
        await vm.LoadAsync();
        var rice = vm.Items.First(i => i.Name == "Rice");

        // A second writer moves the row on, invalidating the row version the view model holds.
        using (var other = _factory.CreateDbContext())
        {
            var current = await other.Items.SingleAsync(i => i.Id == _riceId);
            current.Quantity = 42m;
            await other.SaveChangesAsync();
        }

        rice.Quantity = 3m;
        RunCommand(vm.SaveChangesCommand);
        await WaitUntilAsync(() => vm.StatusText.Contains("changed elsewhere"));

        Assert.Contains("changed elsewhere", vm.StatusText);

        // The stale write must not have landed.
        using var db = _factory.CreateDbContext();
        Assert.Equal(42m, db.Items.AsNoTracking().Single(i => i.Id == _riceId).Quantity);
    }

    [Fact]
    public async Task AddItemCommand_WithoutDialog_AddsInlineItem()
    {
        var vm = Create();
        await vm.LoadAsync();

        vm.AddItemCommand.Execute(null);

        Assert.Contains(vm.Items, i => i.Name == "New Item");
        Assert.Equal("New Item", vm.SelectedItem?.Name);
    }

    [StaFact]
    public async Task AddItemCommand_WithDialog_AddsReturnedItem()
    {
        var dialogs = new FakeDialogService { Result = new Item { Name = "From Dialog", Quantity = 2m, Unit = "pcs" } };
        var vm = Create(dialogs: dialogs);
        await vm.LoadAsync();

        vm.AddItemCommand.Execute(null);
        await WaitUntilAsync(() => vm.Items.Any(i => i.Name == "From Dialog"));

        Assert.True(DbHasItem("From Dialog"));
        Assert.DoesNotContain("Could not add", vm.StatusText);
        Assert.Contains("was added", vm.ActionMessage);
    }

    [StaFact]
    public async Task EditSelectedItemAsync_WritesTheEditedItem_AndSaysSo()
    {
        var dialogs = new FakeDialogService
        {
            Result = new Item { Name = "Rice", Quantity = 5m, Unit = "kg", ExpiryDate = DateTime.Today.AddDays(30) }
        };
        var vm = Create(dialogs: dialogs);
        await vm.LoadAsync();
        vm.SelectedItem = vm.Items.First(i => i.Name == "Rice");

        await vm.EditSelectedItemAsync();

        // The row is updated in place and the change is written straight away, which is the
        // whole point of routing editing through the dialog: nothing is left looking applied.
        Assert.Equal(5m, vm.SelectedItem!.Quantity);
        Assert.True(vm.HasActionMessage);
        Assert.Contains("was updated", vm.ActionMessage);

        using var db = _factory.CreateDbContext();
        Assert.Equal(5m, db.Items.AsNoTracking().Single(i => i.Id == _riceId).Quantity);
    }

    [StaFact]
    public async Task EditSelectedItemAsync_LeavesTheItemAlone_WhenTheDialogIsDismissed()
    {
        var dialogs = new FakeDialogService { Result = null };
        var vm = Create(dialogs: dialogs);
        await vm.LoadAsync();
        vm.SelectedItem = vm.Items.First(i => i.Name == "Rice");

        await vm.EditSelectedItemAsync();

        Assert.Equal(10m, vm.SelectedItem!.Quantity);
        Assert.False(vm.HasActionMessage);

        using var db = _factory.CreateDbContext();
        Assert.Equal(10m, db.Items.AsNoTracking().Single(i => i.Id == _riceId).Quantity);
    }

    [Fact]
    public async Task LoadAsync_RaisesIsBusy_AndClearsItWhenDone()
    {
        var vm = Create();
        var busyStates = new List<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ItemsViewModel.IsBusy)) busyStates.Add(vm.IsBusy);
        };

        Assert.False(vm.IsBusy);
        await vm.LoadAsync();

        // The toolbar says so while the work is in flight, and is usable again when it is not.
        Assert.Contains(true, busyStates);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ExportItemsCsvCommand_WritesFilteredItems()
    {
        var save = new FakeFileSave();
        var vm = Create(save: save);
        await vm.LoadAsync();

        RunCommand(vm.ExportItemsCsvCommand);
        await WaitUntilAsync(() => save.Saved.Count == 1);

        Assert.Contains("Rice", save.Saved[0].Content);
        Assert.EndsWith(".csv", save.Saved[0].Name);
    }

    [Fact]
    public async Task ExportRecentMovementsCsvCommand_WritesMovements()
    {
        var save = new FakeFileSave();
        var vm = Create(save: save);
        await vm.LoadAsync();
        await WaitUntilAsync(() => vm.RecentMovements.Count > 0);

        RunCommand(vm.ExportRecentMovementsCsvCommand);
        await WaitUntilAsync(() => save.Saved.Count == 1);

        Assert.Contains("movements-recent", save.Saved[0].Name);
    }

    [Fact]
    public async Task ExportSelectedMovementsCsvCommand_WritesSelectedItemMovements()
    {
        var save = new FakeFileSave();
        var vm = Create(save: save);
        await vm.LoadAsync();
        vm.SelectedItem = vm.Items.First(i => i.Name == "Rice");
        await WaitUntilAsync(() => vm.SelectedMovements.Count > 0);

        RunCommand(vm.ExportSelectedMovementsCsvCommand);
        await WaitUntilAsync(() => save.Saved.Count == 1);

        Assert.Contains("movements-item", save.Saved[0].Name);
        Assert.Contains("Rice", save.Saved[0].Content);
    }

    [Fact]
    public async Task ImportItemsCsvCommand_WithoutDialog_ImportsNewItems()
    {
        var open = new FakeFileOpen { Content = "Id,Name,Quantity,Unit\n0,Imported Sugar,4,kg" };
        var vm = Create(open: open);
        await vm.LoadAsync();

        RunCommand(vm.ImportItemsCsvCommand);
        await WaitUntilAsync(() => DbHasItem("Imported Sugar"));

        // One row imported, so the noun must be singular: "Imported 1 item: ...".
        Assert.Contains("Imported 1 item:", vm.StatusText);
        using var db = _factory.CreateDbContext();
        Assert.True(db.StockMovements.Any(m => m.Reason == "Import add"));
    }

    [Fact]
    public async Task ImportItemsCsvCommand_UpdatesExistingItemByName()
    {
        var open = new FakeFileOpen { Content = "Id,Name,Quantity,Unit\n0,Rice,15,kg" };
        var vm = Create(open: open);
        await vm.LoadAsync();

        RunCommand(vm.ImportItemsCsvCommand);
        await WaitUntilAsync(() =>
        {
            using var db = _factory.CreateDbContext();
            return db.StockMovements.Any(m => m.Reason == "Import update");
        });

        Assert.Contains("1 updated", vm.StatusText);
    }

    [Fact]
    public async Task ImportItemsCsvCommand_EmptyCsv_SetsStatus()
    {
        var open = new FakeFileOpen { Content = "Id,Name,Quantity,Unit\n" };
        var vm = Create(open: open);
        await vm.LoadAsync();

        RunCommand(vm.ImportItemsCsvCommand);
        await WaitUntilAsync(() => vm.StatusText.Contains("no items in it"));

        Assert.Contains("no items in it", vm.StatusText);
    }

    [Fact]
    public async Task ImportItemsCsvCommand_NullContent_DoesNothing()
    {
        var open = new FakeFileOpen { Content = null };
        var vm = Create(open: open);
        await vm.LoadAsync();
        var before = vm.StatusText;

        RunCommand(vm.ImportItemsCsvCommand);
        await Task.Delay(200);

        Assert.Equal(before, vm.StatusText);
    }

    [Fact]
    public async Task RoleGating_DisablesCommandsWhenNotAuthorized()
    {
        var session = new FakeSession();
        var vm = Create(session: session);
        await vm.LoadAsync();

        Assert.False(vm.AddItemCommand.CanExecute(null));

        session.SetUser("admin", Roles.Admin);
        Assert.True(vm.AddItemCommand.CanExecute(null));

        session.Clear();
        Assert.False(vm.AddItemCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelectedItem_LoadsMovementsForThatItem()
    {
        var vm = Create();
        await vm.LoadAsync();

        vm.SelectedItem = vm.Items.First(i => i.Name == "Rice");
        await WaitUntilAsync(() => vm.SelectedMovements.Count > 0);

        Assert.All(vm.SelectedMovements, m => Assert.Equal(_riceId, m.ItemId));
    }

    [Fact]
    public async Task SelectedCategoryAndLocation_ApplyFilter()
    {
        var vm = Create();
        await vm.LoadAsync();

        vm.SelectedCategoryId = 2; // Dairy -> Milk only
        Assert.Equal(1, vm.FilteredCount);
        Assert.Equal("Milk", vm.ItemsView!.Cast<Item>().Single().Name);

        vm.SelectedCategoryId = null;
        vm.SelectedLocationId = 1; // Pantry -> Rice only
        Assert.Equal(1, vm.FilteredCount);
        Assert.Equal("Rice", vm.ItemsView!.Cast<Item>().Single().Name);
    }

    [Fact]
    public async Task LoadAsync_ReloadsAfterSave()
    {
        var vm = Create();
        await vm.LoadAsync();
        var firstTotal = vm.TotalCount;

        await vm.LoadAsync();

        Assert.Equal(firstTotal, vm.TotalCount);
    }

    // ---- view states (empty / no results / all clear) ----------------------

    [Fact]
    public async Task ViewState_WithDataAndAnAlert_ShowsNoOverlay()
    {
        var vm = Create();
        await vm.LoadAsync();

        // Milk is out of stock, so there is both data and something to report.
        Assert.False(vm.IsEmpty);
        Assert.False(vm.HasNoMatches);
        Assert.False(vm.IsAllClear);
    }

    [Fact]
    public async Task IsEmpty_IsTrueWhenThereAreNoItemsAtAll()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.StockMovements.RemoveRange(db.StockMovements.ToList());
            db.Items.RemoveRange(db.Items.ToList());
            db.SaveChanges();
        }

        var vm = Create();
        await vm.LoadAsync();

        Assert.True(vm.IsEmpty);
        Assert.False(vm.HasNoMatches);
        Assert.Equal(0, vm.TotalCount);
    }

    [Fact]
    public async Task IsAllClear_IsTrueWhenNothingNeedsAttention()
    {
        using (var db = _factory.CreateDbContext())
        {
            db.Items.Remove(db.Items.Single(i => i.Name == "Milk"));
            db.SaveChanges();
        }

        var vm = Create();
        await vm.LoadAsync();

        Assert.Equal(0, vm.AlertsCount);
        Assert.True(vm.IsAllClear);
        Assert.False(vm.IsEmpty);
        Assert.False(vm.HasNoMatches);
    }

    [Fact]
    public async Task HasNoMatches_IsTrueWhenTheFilterHidesEveryItem()
    {
        var vm = Create();
        await vm.LoadAsync();

        vm.FilterText = "zzz";

        Assert.True(vm.HasNoMatches);
        Assert.False(vm.IsEmpty);
        Assert.Equal("No items match \u201czzz\u201d.", vm.NoMatchesMessage);
    }

    [Fact]
    public async Task NoMatchesMessage_QuotesTheTermOrFallsBackToTheFilters()
    {
        var vm = Create();
        await vm.LoadAsync();

        vm.FilterText = "zzz";
        Assert.Equal("No items match \u201czzz\u201d.", vm.NoMatchesMessage);

        // Whitespace is not a search term, so nothing is quoted back.
        vm.FilterText = "   ";
        Assert.Equal("Nothing matches the filters you have set.", vm.NoMatchesMessage);
    }

    [Fact]
    public async Task ViewState_ReannouncesWhenTheFilterChanges()
    {
        var vm = Create();
        await vm.LoadAsync();

        var seen = new List<string?>();
        vm.PropertyChanged += (_, e) => seen.Add(e.PropertyName);

        vm.FilterText = "zzz";

        Assert.Contains(nameof(ItemsViewModel.HasNoMatches), seen);
        Assert.Contains(nameof(ItemsViewModel.NoMatchesMessage), seen);
        Assert.Contains(nameof(ItemsViewModel.IsEmpty), seen);
    }

    [Fact]
    public async Task ClearFiltersCommand_ShowsEverythingAgain_AndClearsSavedChoices()
    {
        var prefs = new FakePreferences();
        var vm = Create(prefs: prefs);
        await vm.LoadAsync();

        vm.FilterText = "zzz";
        vm.SelectedCategoryId = 1;
        vm.SelectedLocationId = 1;
        vm.ShowNeedsAttentionOnly = true;
        Assert.True(vm.HasNoMatches);

        RunCommand(vm.ClearFiltersCommand);

        Assert.Equal(string.Empty, vm.FilterText);
        Assert.Null(vm.SelectedCategoryId);
        Assert.Null(vm.SelectedLocationId);
        Assert.False(vm.ShowNeedsAttentionOnly);
        Assert.False(vm.HasNoMatches);
        Assert.Equal(vm.TotalCount, vm.FilteredCount);
        // The saved choices are cleared too, or the next launch would reopen the empty list.
        Assert.Equal(0, prefs.Values["ui.selectedCategoryId"]);
        Assert.Equal(0, prefs.Values["ui.selectedLocationId"]);
        Assert.Equal(false, prefs.Values["ui.showNeedsAttentionOnly"]);
    }
}
