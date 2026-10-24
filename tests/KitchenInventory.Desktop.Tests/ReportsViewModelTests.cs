using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KitchenInventory.Data;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Desktop.ViewModels;
using KitchenInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public class ReportsViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly IDbContextFactory<KitchenInventoryDbContext> _factory;
    private readonly DateTime _movementUtc;

    public ReportsViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"reportsvm-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<KitchenInventoryDbContext>(o => o.UseSqlite($"Data Source={_dbPath}"));
        _factory = services.BuildServiceProvider().GetRequiredService<IDbContextFactory<KitchenInventoryDbContext>>();

        using var db = _factory.CreateDbContext();
        db.Database.Migrate();

        db.Categories.AddRange(new Category { Id = 1, Name = "Grains" }, new Category { Id = 2, Name = "Dairy" });
        db.Locations.Add(new Location { Id = 1, Name = "Pantry" });

        var rice = new Item { Name = "Rice", Quantity = 10m, Unit = "kg", CategoryId = 1, LocationId = 1, ExpiryDate = DateTime.Today.AddDays(60) };
        var milk = new Item { Name = "Milk", Quantity = 0m, Unit = "L", CategoryId = 2, ExpiryDate = DateTime.Today.AddDays(2) };
        db.Items.AddRange(rice, milk);
        db.SaveChanges();

        // Anchored inside the default reporting window ([today - 30, today]) without depending on the local clock.
        _movementUtc = DateTime.Today.ToUniversalTime().AddHours(1);
        db.StockMovements.Add(new StockMovement
        {
            ItemId = rice.Id,
            Type = MovementType.Add,
            Quantity = 10m,
            Reason = "Initial add",
            User = "seed",
            TimestampUtc = _movementUtc
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

    private ReportsViewModel Create(
        IPreferencesService? prefs = null,
        IFileSaveService? save = null,
        IDbContextFactory<KitchenInventoryDbContext>? factory = null)
        => new(
            factory ?? _factory,
            save ?? new CapturingFileSave(),
            prefs,
            NullLogger<ReportsViewModel>.Instance);

    private static void RunCommand(System.Windows.Input.ICommand command)
    {
        Assert.True(command.CanExecute(null));
        command.Execute(null);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 8000)
    {
        var sw = Stopwatch.StartNew();
        while (!condition() && sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(25);
        }
        Assert.True(condition(), "Expected condition was not met within the timeout.");
    }

    private static string Cell(ReportTable table, int row, string header)
        => table.Rows[row][table.Headers.ToList().IndexOf(header)];

    // ---- construction ------------------------------------------------------

    [Fact]
    public void Constructor_AppliesDefaults()
    {
        var vm = Create();

        Assert.Equal(5m, vm.LowStockThreshold);
        Assert.Equal(7, vm.ExpiringSoonDays);
        Assert.Equal(DateTime.Today.AddDays(-30), vm.FromDate);
        Assert.Equal(DateTime.Today, vm.ToDate);
        Assert.Equal(ReportKind.StockOnHand, vm.SelectedOption.Kind);
        Assert.Equal(4, vm.ReportOptions.Count);
        Assert.False(vm.HasReport);
        Assert.Null(vm.CurrentReport);
        Assert.Contains("Generate", vm.StatusText);
    }

    [Fact]
    public void Constructor_ReadsSavedSelectionsAndThresholdsFromPreferences()
    {
        var prefs = new FakePreferences();
        prefs.Values["reports.kind"] = (int)ReportKind.MovementHistory;
        prefs.Values["inventory.lowStockThreshold"] = 9m;
        prefs.Values["inventory.expiringSoonDays"] = 11;

        var vm = Create(prefs: prefs);

        Assert.Equal(ReportKind.MovementHistory, vm.SelectedOption.Kind);
        Assert.Equal(9m, vm.LowStockThreshold);
        Assert.Equal(11, vm.ExpiringSoonDays);
        Assert.True(vm.ShowDateRange);
        Assert.False(vm.ShowThreshold);
        Assert.False(vm.ShowExpiringDays);
    }

    [Fact]
    public void Constructor_UnknownSavedKind_FallsBackToFirstOption()
    {
        var prefs = new FakePreferences();
        prefs.Values["reports.kind"] = 99;

        var vm = Create(prefs: prefs);

        Assert.Equal(ReportKind.StockOnHand, vm.SelectedOption.Kind);
    }

    // ---- selection ---------------------------------------------------------

    [Fact]
    public void SelectedOption_TogglesFlagsAndPersists()
    {
        var prefs = new FakePreferences();
        var vm = Create(prefs: prefs);
        var raised = new List<string>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        vm.SelectedOption = vm.ReportOptions.Single(o => o.Kind == ReportKind.LowStock);

        Assert.True(vm.ShowThreshold);
        Assert.False(vm.ShowExpiringDays);
        Assert.False(vm.ShowDateRange);
        Assert.Equal((int)ReportKind.LowStock, prefs.Values["reports.kind"]);
        Assert.True(prefs.SaveCount > 0);
        Assert.Contains(nameof(ReportsViewModel.ShowThreshold), raised);
        Assert.Contains(nameof(ReportsViewModel.ShowExpiringDays), raised);
        Assert.Contains(nameof(ReportsViewModel.ShowDateRange), raised);

        vm.SelectedOption = vm.ReportOptions.Single(o => o.Kind == ReportKind.ExpiringSoon);
        Assert.True(vm.ShowExpiringDays);
        Assert.False(vm.ShowThreshold);
    }

    [Fact]
    public void SelectedOption_IgnoresNullAndSameInstance()
    {
        var prefs = new FakePreferences();
        var vm = Create(prefs: prefs);
        var original = vm.SelectedOption;

        vm.SelectedOption = null!;
        vm.SelectedOption = original;

        Assert.Same(original, vm.SelectedOption);
        Assert.Equal(0, prefs.SaveCount);
    }

    [Fact]
    public void SettableProperties_RaiseChangeNotifications()
    {
        var vm = Create();
        var raised = new List<string>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? string.Empty);

        vm.LowStockThreshold = 3m;
        vm.ExpiringSoonDays = 14;
        vm.FromDate = DateTime.Today.AddDays(-7);
        vm.ToDate = DateTime.Today.AddDays(-1);

        Assert.Contains(nameof(ReportsViewModel.LowStockThreshold), raised);
        Assert.Contains(nameof(ReportsViewModel.ExpiringSoonDays), raised);
        Assert.Contains(nameof(ReportsViewModel.FromDate), raised);
        Assert.Contains(nameof(ReportsViewModel.ToDate), raised);
    }

    [Fact]
    public void GenerateCommand_RequiresNoReportToRun()
    {
        var vm = Create();

        Assert.True(vm.GenerateCommand.CanExecute(null));
        Assert.False(vm.ExportCsvCommand.CanExecute(null));
        Assert.False(vm.ExportPdfCommand.CanExecute(null));
    }

    // ---- generation --------------------------------------------------------

    [Fact]
    public async Task GenerateAsync_StockOnHand_ListsEveryItemAndEnablesExports()
    {
        var vm = Create();

        await vm.GenerateAsync();

        var report = vm.CurrentReport!;
        Assert.True(vm.HasReport);
        Assert.Equal("Stock on Hand", report.Title);
        Assert.Equal(2, report.RowCount);
        Assert.Equal("Milk", Cell(report, 0, "Name")); // ordered by name
        Assert.Equal("Rice", Cell(report, 1, "Name"));
        Assert.Equal("Pantry", Cell(report, 1, "Location"));
        Assert.Equal("10", Cell(report, 1, "Quantity"));
        Assert.Equal("Stock on Hand: 2 row(s) generated.", vm.StatusText);
        Assert.True(vm.ExportCsvCommand.CanExecute(null));
        Assert.True(vm.ExportPdfCommand.CanExecute(null));
    }

    [Fact]
    public async Task GenerateAsync_LowStock_AppliesConfiguredThreshold()
    {
        var vm = Create();
        vm.SelectedOption = vm.ReportOptions.Single(o => o.Kind == ReportKind.LowStock);
        vm.LowStockThreshold = 5m;

        await vm.GenerateAsync();

        var report = vm.CurrentReport!;
        Assert.Equal("Low Stock (below 5)", report.Title);
        Assert.Equal(1, report.RowCount);
        Assert.Equal("Milk", Cell(report, 0, "Name"));
    }

    [Fact]
    public async Task GenerateAsync_ExpiringSoon_AppliesConfiguredWindow()
    {
        var vm = Create();
        vm.SelectedOption = vm.ReportOptions.Single(o => o.Kind == ReportKind.ExpiringSoon);
        vm.ExpiringSoonDays = 7;

        await vm.GenerateAsync();

        var report = vm.CurrentReport!;
        Assert.Equal("Expiring Soon (next 7 day(s))", report.Title);
        Assert.Equal(1, report.RowCount);
        Assert.Equal("Milk", Cell(report, 0, "Name"));
        Assert.Equal("2", Cell(report, 0, "Days To Expiry"));
    }

    [Fact]
    public async Task GenerateAsync_MovementHistory_ResolvesItemNamesWithinRange()
    {
        var vm = Create();
        vm.SelectedOption = vm.ReportOptions.Single(o => o.Kind == ReportKind.MovementHistory);
        vm.FromDate = DateTime.Today.AddDays(-1);
        vm.ToDate = DateTime.Today;

        await vm.GenerateAsync();

        var report = vm.CurrentReport!;
        Assert.Equal("Movement History", report.Title);
        Assert.Equal(1, report.RowCount);
        Assert.Equal("Rice", Cell(report, 0, "Item"));
        Assert.Equal("Add", Cell(report, 0, "Type"));
        Assert.Equal("seed", Cell(report, 0, "User"));
    }

    [Fact]
    public async Task GenerateAsync_MovementHistory_OutsideRange_YieldsNoRows()
    {
        var vm = Create();
        vm.SelectedOption = vm.ReportOptions.Single(o => o.Kind == ReportKind.MovementHistory);
        vm.FromDate = DateTime.Today.AddDays(-10);
        vm.ToDate = DateTime.Today.AddDays(-5);

        await vm.GenerateAsync();

        Assert.Equal(0, vm.CurrentReport!.RowCount);
        Assert.Contains("0 row(s)", vm.StatusText);
    }

    [Fact]
    public async Task GenerateAsync_MovementHistory_InvalidDateRange_KeepsReportUnset()
    {
        var vm = Create();
        vm.SelectedOption = vm.ReportOptions.Single(o => o.Kind == ReportKind.MovementHistory);
        vm.FromDate = DateTime.Today;
        vm.ToDate = DateTime.Today.AddDays(-3);

        await vm.GenerateAsync();

        Assert.Null(vm.CurrentReport);
        Assert.False(vm.HasReport);
        Assert.Equal("'To' date must be on or after the 'From' date.", vm.StatusText);
    }

    [Fact]
    public async Task GenerateAsync_DatabaseFailure_SetsFailureStatus()
    {
        var vm = Create(factory: new ThrowingDbContextFactory());

        await vm.GenerateAsync();

        Assert.Null(vm.CurrentReport);
        Assert.StartsWith("Failed to generate report:", vm.StatusText);
    }

    // ---- export ------------------------------------------------------------

    [Fact]
    public async Task ExportCsvCommand_WritesSluggedCsvOfCurrentReport()
    {
        var save = new CapturingFileSave();
        var vm = Create(save: save);
        await vm.GenerateAsync();

        RunCommand(vm.ExportCsvCommand);
        await WaitUntilAsync(() => save.LastText != null);

        Assert.Equal("stock-on-hand.csv", save.LastName);
        Assert.StartsWith("Id,Name,Category,Location,Quantity,Unit,Expiry Date", save.LastText);
        Assert.Contains("Rice", save.LastText);
        Assert.Equal("Exported Stock on Hand to CSV (2 row(s)).", vm.StatusText);
    }

    [Fact]
    public async Task ExportPdfCommand_WritesPdfBytesForCurrentReport()
    {
        var save = new CapturingFileSave();
        var vm = Create(save: save);
        await vm.GenerateAsync();

        RunCommand(vm.ExportPdfCommand);
        await WaitUntilAsync(() => save.LastBytes != null);

        Assert.Equal("stock-on-hand.pdf", save.LastName);
        Assert.Equal(".pdf", save.LastDefaultExt);
        Assert.StartsWith("%PDF-1.4", System.Text.Encoding.Latin1.GetString(save.LastBytes!));
        Assert.Equal("Exported Stock on Hand to PDF (2 row(s)).", vm.StatusText);
    }

    [Fact]
    public async Task Export_WhenCancelled_ReportsCancellation()
    {
        var save = new CapturingFileSave { Result = false };
        var vm = Create(save: save);
        await vm.GenerateAsync();

        RunCommand(vm.ExportCsvCommand);
        await WaitUntilAsync(() => vm.StatusText.Contains("cancelled"));

        Assert.Equal("CSV export cancelled.", vm.StatusText);
    }

    [Fact]
    public async Task Export_WhenSaveThrows_ReportsFailure()
    {
        var save = new CapturingFileSave { ThrowOnSave = true };
        var vm = Create(save: save);
        await vm.GenerateAsync();

        RunCommand(vm.ExportPdfCommand);
        await WaitUntilAsync(() => vm.StatusText.StartsWith("Failed to export PDF:"));

        Assert.StartsWith("Failed to export PDF:", vm.StatusText);
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

    private sealed class CapturingFileSave : IFileSaveService
    {
        public bool Result { get; set; } = true;
        public bool ThrowOnSave { get; set; }
        public string? LastName { get; private set; }
        public string? LastText { get; private set; }
        public byte[]? LastBytes { get; private set; }
        public string? LastFilter { get; private set; }
        public string? LastDefaultExt { get; private set; }

        public Task<bool> SaveTextAsAsync(string suggestedFileName, string content)
        {
            if (ThrowOnSave) throw new IOException("disk full");
            LastName = suggestedFileName;
            LastText = content;
            return Task.FromResult(Result);
        }

        public Task<bool> SaveBytesAsAsync(string suggestedFileName, byte[] content, string filter, string defaultExt)
        {
            if (ThrowOnSave) throw new IOException("disk full");
            LastName = suggestedFileName;
            LastBytes = content;
            LastFilter = filter;
            LastDefaultExt = defaultExt;
            return Task.FromResult(Result);
        }
    }

    private sealed class ThrowingDbContextFactory : IDbContextFactory<KitchenInventoryDbContext>
    {
        public KitchenInventoryDbContext CreateDbContext() => throw new InvalidOperationException("database unavailable");

        public Task<KitchenInventoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("database unavailable");
    }
}
