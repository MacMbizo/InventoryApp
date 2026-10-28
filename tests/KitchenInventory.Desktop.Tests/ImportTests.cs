using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using FluentAssertions;
using KitchenInventory.Data;
using KitchenInventory.Domain.Entities;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Desktop.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public sealed class ImportTests
{
    private sealed class TestDbContextFactory : IDbContextFactory<KitchenInventoryDbContext>
    {
        private readonly DbContextOptions<KitchenInventoryDbContext> _options;
        public TestDbContextFactory(DbContextOptions<KitchenInventoryDbContext> options) => _options = options;
        public KitchenInventoryDbContext CreateDbContext() => new KitchenInventoryDbContext(_options);
        public ValueTask<KitchenInventoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => new(new KitchenInventoryDbContext(_options));
    }

    private sealed class FakeFileOpenService : IFileOpenService
    {
        private readonly string? _text;
        public FakeFileOpenService(string? text) { _text = text; }
        public Task<string?> OpenTextFileAsync(string filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*")
            => Task.FromResult(_text);
    }

    private sealed class FakeCsvImportService : ICsvImportService
    {
        private readonly IReadOnlyList<Item> _items;
        public FakeCsvImportService(IReadOnlyList<Item> items) { _items = items; }
        public Task<IReadOnlyList<Item>> ParseItemsAsync(string csvContent, List<Category> categories, List<Location>? locations = null)
            => Task.FromResult(_items);
    }

    private sealed class FakeFileSaveService : IFileSaveService
    {
        public string? LastSuggestedName { get; private set; }
        public string? LastContent { get; private set; }
        public Task<bool> SaveTextAsAsync(string suggestedFileName, string content)
        {
            LastSuggestedName = suggestedFileName;
            LastContent = content;
            return Task.FromResult(false);
        }

        public Task<bool> SaveBytesAsAsync(string suggestedFileName, byte[] content, string filter, string defaultExt)
            => Task.FromResult(false);
    }

    private static (IDbContextFactory<KitchenInventoryDbContext> factory, SqliteConnection conn, DbContextOptions<KitchenInventoryDbContext> options)
        CreateInMemoryFactory()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        var options = new DbContextOptionsBuilder<KitchenInventoryDbContext>()
            .UseSqlite(conn)
            .Options;
        using (var ctx = new KitchenInventoryDbContext(options))
        {
            ctx.Database.EnsureCreated();
        }
        var factory = new TestDbContextFactory(options);
        return (factory, conn, options);
    }

    private static ILogger<ItemsViewModel> CreateLogger() => NullLogger<ItemsViewModel>.Instance;

    private static async Task InvokePrivateAsync(object instance, string methodName)
    {
        var mi = instance.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (mi == null) throw new MissingMethodException(instance.GetType().FullName, methodName);
        var task = mi.Invoke(instance, Array.Empty<object?>()) as Task;
        if (task != null) await task;
    }

    [StaFact]
    public async Task Import_NewItem_CreatesAddMovement_And_Status()
    {
        var (factory, conn, options) = CreateInMemoryFactory();
        try
        {
            var logger = CreateLogger();
            var importItems = new List<Item> { new Item { Name = "Sugar", Quantity = 5m, Unit = "kg" } };
            var vm = new ItemsViewModel(factory, logger, new FakeFileSaveService(), new FakeFileOpenService("Name,Quantity,Unit\nSugar,5,kg"), new FakeCsvImportService(importItems));

            await InvokePrivateAsync(vm, "ImportItemsCsvAsync");

            using var verify = new KitchenInventoryDbContext(options);
            var item = await verify.Items.AsNoTracking().SingleAsync();
            item.Name.Should().Be("Sugar");
            item.Quantity.Should().Be(5m);
            item.Unit.Should().Be("kg");

            var movement = await verify.StockMovements.AsNoTracking().SingleAsync();
            movement.Type.Should().Be(MovementType.Add);
            movement.Quantity.Should().Be(5m);
            movement.Reason.Should().Be("Import add");

            vm.StatusText.Should().Contain("1 new");
            vm.StatusText.Should().Contain("0 updated");
            vm.Items.Should().ContainSingle(i => i.Name == "Sugar" && i.Quantity == 5m);
        }
        finally { conn.Dispose(); }
    }

    [StaFact]
    public async Task Import_Existing_Increase_CreatesAddDelta()
    {
        var (factory, conn, options) = CreateInMemoryFactory();
        try
        {
            int id;
            using (var seed = new KitchenInventoryDbContext(options))
            {
                var it = new Item { Name = "Milk", Quantity = 2m, Unit = "pcs" };
                seed.Items.Add(it);
                await seed.SaveChangesAsync();
                id = it.Id;
            }

            var logger = CreateLogger();
            var importItems = new List<Item> { new Item { Name = "milk", Quantity = 5m, Unit = "pcs" } }; // case-insensitive name match
            var vm = new ItemsViewModel(factory, logger, new FakeFileSaveService(), new FakeFileOpenService("Name,Quantity,Unit\nSugar,5,kg"), new FakeCsvImportService(importItems));
            await vm.LoadAsync();

            await InvokePrivateAsync(vm, "ImportItemsCsvAsync");

            using var verify = new KitchenInventoryDbContext(options);
            var updated = await verify.Items.AsNoTracking().SingleAsync(i => i.Id == id);
            updated.Quantity.Should().Be(5m);

            var mv = await verify.StockMovements.AsNoTracking().SingleAsync();
            mv.Type.Should().Be(MovementType.Add);
            mv.Quantity.Should().Be(3m);
            mv.Reason.Should().Be("Import update");

            vm.StatusText.Should().Contain("0 new");
            vm.StatusText.Should().Contain("1 updated");
        }
        finally { conn.Dispose(); }
    }

    [StaFact]
    public async Task Import_Existing_Decrease_CreatesConsumeDelta()
    {
        var (factory, conn, options) = CreateInMemoryFactory();
        try
        {
            int id;
            using (var seed = new KitchenInventoryDbContext(options))
            {
                var it = new Item { Name = "Rice", Quantity = 5m, Unit = "kg" };
                seed.Items.Add(it);
                await seed.SaveChangesAsync();
                id = it.Id;
            }

            var logger = CreateLogger();
            var importItems = new List<Item> { new Item { Name = "Rice", Quantity = 2m, Unit = "kg" } };
            var vm = new ItemsViewModel(factory, logger, new FakeFileSaveService(), new FakeFileOpenService("Name,Quantity,Unit\nSugar,5,kg"), new FakeCsvImportService(importItems));
            await vm.LoadAsync();

            await InvokePrivateAsync(vm, "ImportItemsCsvAsync");

            using var verify = new KitchenInventoryDbContext(options);
            var updated = await verify.Items.AsNoTracking().SingleAsync(i => i.Id == id);
            updated.Quantity.Should().Be(2m);

            var mv = await verify.StockMovements.AsNoTracking().SingleAsync();
            mv.Type.Should().Be(MovementType.Consume);
            mv.Quantity.Should().Be(3m);
            mv.Reason.Should().Be("Import update");
        }
        finally { conn.Dispose(); }
    }

    [StaFact]
    public async Task Import_NoItemsInCsv_SetsStatus()
    {
        var (factory, conn, options) = CreateInMemoryFactory();
        try
        {
            var logger = CreateLogger();
            var importItems = Array.Empty<Item>();
            // A readable header with nothing underneath it, which is the file this test is about.
            var vm = new ItemsViewModel(factory, logger, new FakeFileSaveService(), new FakeFileOpenService("Name,Quantity,Unit"), new FakeCsvImportService(importItems));

            await InvokePrivateAsync(vm, "ImportItemsCsvAsync");
            vm.StatusText.Should().Be("That file had no items in it.");
        }
        finally { conn.Dispose(); }
    }

    /// <summary>
    /// A first line that names no column this app reads is a dead end, and saying so once is the
    /// difference between "the header is wrong" and every row being reported as missing a name.
    /// Nothing at all should be written when that happens.
    /// </summary>
    [StaFact]
    public async Task Import_FirstLineThatIsNotAHeader_SaysWhy_AndWritesNothing()
    {
        var (factory, conn, options) = CreateInMemoryFactory();
        try
        {
            var logger = CreateLogger();
            // The importer is handed items on purpose: a file it cannot read must be refused
            // before it gets as far as the rows.
            var importItems = new List<Item> { new Item { Name = "Sugar", Quantity = 5m, Unit = "kg" } };
            var vm = new ItemsViewModel(factory, logger, new FakeFileSaveService(),
                new FakeFileOpenService("Item,Qty\nSugar,5"), new FakeCsvImportService(importItems));

            await InvokePrivateAsync(vm, "ImportItemsCsvAsync");

            vm.StatusText.Should().Contain("first line");
            vm.StatusText.Should().Contain("Importing from a spreadsheet");

            using var verify = new KitchenInventoryDbContext(options);
            (await verify.Items.AsNoTracking().CountAsync()).Should().Be(0);
            (await verify.StockMovements.AsNoTracking().CountAsync()).Should().Be(0);
        }
        finally { conn.Dispose(); }
    }
}