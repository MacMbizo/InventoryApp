using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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

public sealed class ExportTests
{
    private sealed class TestDbContextFactory : IDbContextFactory<KitchenInventoryDbContext>
    {
        private readonly DbContextOptions<KitchenInventoryDbContext> _options;
        public TestDbContextFactory(DbContextOptions<KitchenInventoryDbContext> options) => _options = options;
        public KitchenInventoryDbContext CreateDbContext() => new KitchenInventoryDbContext(_options);
        public ValueTask<KitchenInventoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => new(new KitchenInventoryDbContext(_options));
    }

    private sealed class CapturingFileSaveService : IFileSaveService
    {
        public string? LastSuggestedName { get; private set; }
        public string? LastContent { get; private set; }
        public Task<bool> SaveTextAsAsync(string suggestedFileName, string content)
        {
            LastSuggestedName = suggestedFileName;
            LastContent = content;
            return Task.FromResult(false);
        }
    }

    private sealed class NullFileOpenService : IFileOpenService
    {
        public Task<string?> OpenTextFileAsync(string filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*")
            => Task.FromResult<string?>(null);
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
    public async Task ExportItemsCsv_GeneratesExpectedCsv_WithEscaping()
    {
        var (factory, conn, options) = CreateInMemoryFactory();
        try
        {
            // Seed deterministic items
            using (var seed = new KitchenInventoryDbContext(options))
            {
                seed.Items.AddRange(new[]
                {
                    new Item
                    {
                        Name = "Tomato, \"Roma\"",
                        Quantity = 3.5m,
                        Unit = "pcs",
                        ExpiryDate = new DateTime(2025, 01, 31),
                        CreatedAtUtc = new DateTime(2024, 01, 02, 03, 04, 05, DateTimeKind.Utc),
                        UpdatedAtUtc = new DateTime(2024, 02, 03, 04, 05, 06, DateTimeKind.Utc)
                    },
                    new Item
                    {
                        Name = "Milk",
                        Quantity = 2m,
                        Unit = "L",
                        CreatedAtUtc = new DateTime(2024, 03, 04, 05, 06, 07, DateTimeKind.Utc),
                        UpdatedAtUtc = null,
                        ExpiryDate = null
                    }
                });
                await seed.SaveChangesAsync();
            }

            var saver = new CapturingFileSaveService();
            var vm = new ItemsViewModel(factory, CreateLogger(), saver, new NullFileOpenService(), new FakeCsvImportServiceEmpty());

            await vm.LoadAsync();
            await InvokePrivateAsync(vm, "ExportItemsCsvAsync");

            saver.LastSuggestedName.Should().NotBeNull();
            saver.LastSuggestedName!.Should().StartWith("items-").And.EndWith(".csv");
            saver.LastContent.Should().NotBeNull();

            var expected = CsvExportService.ExportItems(vm.ItemsView!.Cast<Item>().ToList());
            saver.LastContent.Should().Be(expected);

            // Spot-check header is present and quoted value for name is escaped
            var lines = saver.LastContent!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            lines[0].Should().Be("Id,Name,Quantity,Unit,ExpiryDate,CreatedAtUtc,UpdatedAtUtc");
            lines.Should().Contain(l => l.Contains("\"Tomato, \"\"Roma\"\"\""))
                 .And.Contain(l => Regex.IsMatch(l, @",2(\.0)?,"));
        }
        finally { conn.Dispose(); }
    }

    [StaFact]
    public async Task ExportSelectedMovementsCsv_UsesSelectedItemNameMap()
    {
        var (factory, conn, options) = CreateInMemoryFactory();
        try
        {
            int id;
            using (var seed = new KitchenInventoryDbContext(options))
            {
                var it = new Item { Name = "Sugar", Quantity = 0m, Unit = "kg", CreatedAtUtc = new DateTime(2024,1,1,0,0,0,DateTimeKind.Utc) };
                seed.Items.Add(it);
                await seed.SaveChangesAsync();
                id = it.Id;
            }

            var saver = new CapturingFileSaveService();
            var vm = new ItemsViewModel(factory, CreateLogger(), saver, new NullFileOpenService(), new FakeCsvImportServiceEmpty());
            await vm.LoadAsync();

            // Select the item to enable Selected map
            vm.SelectedItem = vm.Items.Single(i => i.Id == id);

            // Add a movement for the selected item
            var ts = new DateTime(2024, 05, 06, 07, 08, 09, DateTimeKind.Utc);
            vm.SelectedMovements.Add(new StockMovement
            {
                Id = 1,
                ItemId = id,
                Type = MovementType.Add,
                Quantity = 2m,
                Reason = "Manual, test",
                User = "tester",
                TimestampUtc = ts
            });

            await InvokePrivateAsync(vm, "ExportSelectedMovementsCsvAsync");

            saver.LastSuggestedName.Should().StartWith($"movements-item-{id}-").And.EndWith(".csv");
            saver.LastContent.Should().NotBeNull();

            var moves = vm.SelectedMovements.ToList();
            var expected = CsvExportService.ExportMovements(moves, new Dictionary<int, string> { [id] = "Sugar" });
            saver.LastContent.Should().Be(expected);

            // Ensure the ItemName column used the selected item's name
            saver.LastContent!.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                 .Should().Contain(l => l.StartsWith("1,") && l.Contains("," + id + ",") && l.Contains(",Sugar,"));
        }
        finally { conn.Dispose(); }
    }

    [StaFact]
    public async Task ExportRecentMovementsCsv_QueriesDbForItemNames()
    {
        var (factory, conn, options) = CreateInMemoryFactory();
        try
        {
            int id1, id2;
            using (var seed = new KitchenInventoryDbContext(options))
            {
                var a = new Item { Name = "Beans", Quantity = 0m, Unit = "pcs", CreatedAtUtc = new DateTime(2024,1,1,0,0,0,DateTimeKind.Utc) };
                var b = new Item { Name = "Oil", Quantity = 0m, Unit = "L", CreatedAtUtc = new DateTime(2024,1,1,0,0,0,DateTimeKind.Utc) };
                seed.Items.AddRange(a, b);
                await seed.SaveChangesAsync();
                id1 = a.Id; id2 = b.Id;
            }

            var saver = new CapturingFileSaveService();
            var vm = new ItemsViewModel(factory, CreateLogger(), saver, new NullFileOpenService(), new FakeCsvImportServiceEmpty());
            await vm.LoadAsync();

            // Populate RecentMovements with entries for both items (no Item set so name must be fetched)
            vm.RecentMovements.Add(new StockMovement { Id = 10, ItemId = id1, Type = MovementType.Add, Quantity = 1m, Reason = "import", User = "op,er", TimestampUtc = new DateTime(2024,6,1,12,0,0,DateTimeKind.Utc) });
            vm.RecentMovements.Add(new StockMovement { Id = 11, ItemId = id2, Type = MovementType.Consume, Quantity = 0.5m, Reason = "cook", User = "chef", TimestampUtc = new DateTime(2024,6,2,13,0,0,DateTimeKind.Utc) });

            await InvokePrivateAsync(vm, "ExportRecentMovementsCsvAsync");

            saver.LastSuggestedName.Should().StartWith("movements-recent-").And.EndWith(".csv");
            saver.LastContent.Should().NotBeNull();

            // Build expected name map via DB query like the VM does
            using var verify = new KitchenInventoryDbContext(options);
            var ids = new[] { id1, id2 };
            var pairs = await verify.Items.AsNoTracking().Where(i => ids.Contains(i.Id)).Select(i => new { i.Id, i.Name }).ToListAsync();
            var map = pairs.Where(p => !string.IsNullOrWhiteSpace(p.Name)).ToDictionary(p => p.Id, p => p.Name!);
            var expected = CsvExportService.ExportMovements(vm.RecentMovements.ToList(), map);
            saver.LastContent.Should().Be(expected);

            // Ensure both names appear and the comma in User is properly quoted
            var lines = saver.LastContent!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            lines[0].Should().Be("Id,ItemId,ItemName,Type,Quantity,Reason,User,TimestampUtc");
            lines.Should().Contain(l => l.Contains("," + id1 + ",Beans,"));
            lines.Should().Contain(l => l.Contains("," + id2 + ",Oil,"));
            lines.Should().Contain(l => l.Contains(",\"op,er\""));
        }
        finally { conn.Dispose(); }
    }

    // Helpers
    private sealed class FakeCsvImportServiceEmpty : ICsvImportService
    {
        public Task<IReadOnlyList<Item>> ParseItemsAsync(string csvContent) => Task.FromResult((IReadOnlyList<Item>)Array.Empty<Item>());
    }
}