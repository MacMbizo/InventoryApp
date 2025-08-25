using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KitchenInventory.Data;
using KitchenInventory.Desktop.ViewModels;
using KitchenInventory.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using KitchenInventory.Desktop.Services;
using System.Collections.Generic;

namespace KitchenInventory.Desktop.Tests;

public sealed class MovementTests
{
    private sealed class TestDbContextFactory : IDbContextFactory<KitchenInventoryDbContext>
    {
        private readonly DbContextOptions<KitchenInventoryDbContext> _options;
        public TestDbContextFactory(DbContextOptions<KitchenInventoryDbContext> options) => _options = options;
        public KitchenInventoryDbContext CreateDbContext() => new KitchenInventoryDbContext(_options);
        public ValueTask<KitchenInventoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => new(new KitchenInventoryDbContext(_options));
    }

    private sealed class FakeFileSaveService : IFileSaveService
    {
        public string? LastSuggestedName { get; private set; }
        public string? LastContent { get; private set; }
        public Task<bool> SaveTextAsAsync(string suggestedFileName, string content)
        {
            LastSuggestedName = suggestedFileName;
            LastContent = content;
            // Simulate user cancelled save to avoid any filesystem I/O during tests
            return Task.FromResult(false);
        }
    }

    private sealed class FakeFileOpenService : IFileOpenService
    {
        public Task<string?> OpenTextFileAsync(string filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*")
            => Task.FromResult<string?>(null);
    }

    private sealed class FakeCsvImportService : ICsvImportService
    {
        public Task<IReadOnlyList<Item>> ParseItemsAsync(string csvContent)
            => Task.FromResult((IReadOnlyList<Item>)Array.Empty<Item>());
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
    public async Task Save_NewItem_WithQuantity_CreatesAddMovement()
    {
        var (factory, conn, options) = CreateInMemoryFactory();
        try
        {
            var logger = CreateLogger();
            var vm = new ItemsViewModel(factory, logger, new FakeFileSaveService(), new FakeFileOpenService(), new FakeCsvImportService());

            // Add the default new item (Name="New Item", Quantity=1, Unit="pcs")
            vm.AddItemCommand.Execute(null);
            await InvokePrivateAsync(vm, "SaveChangesAsync");

            using var verify = new KitchenInventoryDbContext(options);
            var movements = await verify.StockMovements.AsNoTracking().ToListAsync();
            movements.Should().HaveCount(1);
            var m = movements.Single();
            m.Type.Should().Be(MovementType.Add);
            m.Quantity.Should().Be(1);
            m.Reason.Should().Be("Initial add");
        }
        finally
        {
            conn.Dispose();
        }
    }

    [StaFact]
    public async Task Edit_ExistingItem_IncreaseQuantity_CreatesAddDeltaMovement()
    {
        var (factory, conn, options) = CreateInMemoryFactory();
        try
        {
            int itemId;
            using (var seed = new KitchenInventoryDbContext(options))
            {
                var it = new Item { Name = "Milk", Quantity = 2, Unit = "pcs" };
                seed.Items.Add(it);
                await seed.SaveChangesAsync();
                itemId = it.Id;
            }

            var logger = CreateLogger();
            var vm = new ItemsViewModel(factory, logger, new FakeFileSaveService(), new FakeFileOpenService(), new FakeCsvImportService());
            await vm.LoadAsync();

            var editable = vm.Items.Single(i => i.Id == itemId);
            editable.Quantity += 3; // delta +3
            await InvokePrivateAsync(vm, "SaveChangesAsync");

            using var verify = new KitchenInventoryDbContext(options);
            var movements = await verify.StockMovements.AsNoTracking().OrderBy(m => m.Id).ToListAsync();
            movements.Should().ContainSingle();
            var m = movements.Single();
            m.Type.Should().Be(MovementType.Add);
            m.Quantity.Should().Be(3);
            m.Reason.Should().Be("Manual edit");
        }
        finally
        {
            conn.Dispose();
        }
    }

    [StaFact]
    public async Task Edit_ExistingItem_DecreaseQuantity_CreatesConsumeDeltaMovement()
    {
        var (factory, conn, options) = CreateInMemoryFactory();
        try
        {
            int itemId;
            using (var seed = new KitchenInventoryDbContext(options))
            {
                var it = new Item { Name = "Rice", Quantity = 5, Unit = "kg" };
                seed.Items.Add(it);
                await seed.SaveChangesAsync();
                itemId = it.Id;
            }

            var logger = CreateLogger();
            var vm = new ItemsViewModel(factory, logger, new FakeFileSaveService(), new FakeFileOpenService(), new FakeCsvImportService());
            await vm.LoadAsync();

            var editable = vm.Items.Single(i => i.Id == itemId);
            editable.Quantity -= 3; // delta -3
            await InvokePrivateAsync(vm, "SaveChangesAsync");

            using var verify = new KitchenInventoryDbContext(options);
            var movements = await verify.StockMovements.AsNoTracking().OrderBy(m => m.Id).ToListAsync();
            movements.Should().ContainSingle();
            var m = movements.Single();
            m.Type.Should().Be(MovementType.Consume);
            m.Quantity.Should().Be(3);
            m.Reason.Should().Be("Manual edit");
        }
        finally
        {
            conn.Dispose();
        }
    }

    [StaFact]
    public async Task Edit_ExistingItem_NoQuantityChange_CreatesNoMovement()
    {
        var (factory, conn, options) = CreateInMemoryFactory();
        try
        {
            int itemId;
            using (var seed = new KitchenInventoryDbContext(options))
            {
                var it = new Item { Name = "Beans", Quantity = 10, Unit = "pcs" };
                seed.Items.Add(it);
                await seed.SaveChangesAsync();
                itemId = it.Id;
            }

            var logger = CreateLogger();
            var vm = new ItemsViewModel(factory, logger, new FakeFileSaveService(), new FakeFileOpenService(), new FakeCsvImportService());
            await vm.LoadAsync();

            var editable = vm.Items.Single(i => i.Id == itemId);
            // Do not change quantity
            await InvokePrivateAsync(vm, "SaveChangesAsync");

            using var verify = new KitchenInventoryDbContext(options);
            var movements = await verify.StockMovements.AsNoTracking().ToListAsync();
            movements.Should().BeEmpty();
        }
        finally
        {
            conn.Dispose();
        }
    }

    [StaFact]
    public async Task NewItem_WithZeroQuantity_CreatesNoMovement()
    {
        var (factory, conn, options) = CreateInMemoryFactory();
        try
        {
            var logger = CreateLogger();
            var vm = new ItemsViewModel(factory, logger, new FakeFileSaveService(), new FakeFileOpenService(), new FakeCsvImportService());

            vm.AddItemCommand.Execute(null);
            // Set the newly added item's quantity to 0 to avoid initial Add movement
            var added = vm.Items.Last();
            added.Quantity = 0;

            await InvokePrivateAsync(vm, "SaveChangesAsync");

            using var verify = new KitchenInventoryDbContext(options);
            var movements = await verify.StockMovements.AsNoTracking().ToListAsync();
            movements.Should().BeEmpty();
        }
        finally
        {
            conn.Dispose();
        }
    }

    [StaFact]
    public async Task Save_Sets_LastSavedAt_And_StatusText_Shows_Timestamp()
    {
        var (factory, conn, options) = CreateInMemoryFactory();
        try
        {
            var logger = CreateLogger();
            var vm = new ItemsViewModel(factory, logger, new FakeFileSaveService(), new FakeFileOpenService(), new FakeCsvImportService());

            // Ensure there's something to save
            vm.AddItemCommand.Execute(null);
            await InvokePrivateAsync(vm, "SaveChangesAsync");

            vm.LastSavedAtLocal.Should().NotBeNull();
            var expected = $"Last saved: {vm.LastSavedAtLocal:yyyy-MM-dd HH:mm}";
            vm.StatusText.Should().Contain(expected);
        }
        finally
        {
            conn.Dispose();
        }
    }


    [StaFact]
    public void SaveChangesCommand_Disabled_When_HasValidationErrors()
    {
        var (factory, conn, options) = CreateInMemoryFactory();
        try
        {
            var logger = CreateLogger();
            var vm = new ItemsViewModel(factory, logger, new FakeFileSaveService(), new FakeFileOpenService(), new FakeCsvImportService());

            // Make the state valid and savable
            vm.AddItemCommand.Execute(null);

            // Initially enabled when valid and items exist
            vm.SaveChangesCommand.CanExecute(null).Should().BeTrue();

            // Simulate validation error surfaced from UI
            vm.HasValidationErrors = true;

            vm.SaveChangesCommand.CanExecute(null).Should().BeFalse();

            // Clear errors, should re-enable
            vm.HasValidationErrors = false;

            vm.SaveChangesCommand.CanExecute(null).Should().BeTrue();
        }
        finally
        {
            conn.Dispose();
        }
    }

    [StaFact]
    public void SaveChangesCommand_Disabled_When_Item_Invalid()
    {
        var (factory, conn, options) = CreateInMemoryFactory();
        try
        {
            var logger = CreateLogger();
            var vm = new ItemsViewModel(factory, logger, new FakeFileSaveService(), new FakeFileOpenService(), new FakeCsvImportService());

            // Add a new item
            vm.AddItemCommand.Execute(null);
            var added = vm.Items.Last();

            // Make it invalid: empty name
            added.Name = string.Empty;
            vm.SaveChangesCommand.CanExecute(null).Should().BeFalse();

            // Fix name to become valid
            added.Name = "Apples";
            vm.SaveChangesCommand.CanExecute(null).Should().BeTrue();

            // Now make it invalid with out-of-range quantity
            added.Quantity = -1m;
            vm.SaveChangesCommand.CanExecute(null).Should().BeFalse();

            // Fix quantity to valid range
            added.Quantity = 10m;
            vm.SaveChangesCommand.CanExecute(null).Should().BeTrue();
        }
        finally
        {
            conn.Dispose();
        }
    }

    [StaFact]
    public void SaveChangesCommand_Disabled_When_No_Items()
    {
        var (factory, conn, options) = CreateInMemoryFactory();
        try
        {
            var logger = CreateLogger();
            var vm = new ItemsViewModel(factory, logger, new FakeFileSaveService(), new FakeFileOpenService(), new FakeCsvImportService());

            // No items initially
            vm.Items.Count.Should().Be(0);
            vm.SaveChangesCommand.CanExecute(null).Should().BeFalse();

            // After adding an item, should be enabled (assuming valid item)
            vm.AddItemCommand.Execute(null);
            var added = vm.Items.Last();
            added.Name = "Rice";
            added.Unit = "pcs";
            added.Quantity = 1m;
            vm.SaveChangesCommand.CanExecute(null).Should().BeTrue();
        }
        finally
        {
            conn.Dispose();
        }
    }
}