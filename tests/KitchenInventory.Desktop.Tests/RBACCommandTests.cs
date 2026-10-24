using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KitchenInventory.Data;
using KitchenInventory.Domain.Entities;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Desktop.ViewModels;
using KitchenInventory.Domain.Constants;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public sealed class RBACCommandTests
{
    private sealed class TestDbContextFactory : IDbContextFactory<KitchenInventoryDbContext>
    {
        private readonly DbContextOptions<KitchenInventoryDbContext> _options;
        public TestDbContextFactory(DbContextOptions<KitchenInventoryDbContext> options) => _options = options;
        public KitchenInventoryDbContext CreateDbContext() => new KitchenInventoryDbContext(_options);
        public ValueTask<KitchenInventoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => new(new KitchenInventoryDbContext(_options));
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

    private static (ItemsViewModel vm, SessionService session, SqliteConnection conn) BuildVmWithSession()
    {
        var (factory, conn, _) = CreateInMemoryFactory();
        var logger = CreateLogger();
        var session = new SessionService();
        var vm = new ItemsViewModel(factory, logger, new FileSaveService(), new FileOpenService(), new CsvImportService(),
            configuration: null, preferences: null, dialogs: null, session: session);
        return (vm, session, conn);
    }

    [Fact]
    public void AddItemCommand_Enabled_For_Admin_And_Manager_Only()
    {
        var (vm, session, conn) = BuildVmWithSession();
        try
        {
            // Unauthenticated
            session.Clear();
            Assert.False(vm.AddItemCommand.CanExecute(null));

            // Staff
            session.SetUser("staff", Roles.Staff);
            Assert.False(vm.AddItemCommand.CanExecute(null));

            // Manager
            session.SetUser("manager", Roles.Manager);
            Assert.True(vm.AddItemCommand.CanExecute(null));

            // Admin
            session.SetUser("admin", Roles.Admin);
            Assert.True(vm.AddItemCommand.CanExecute(null));
        }
        finally { conn.Dispose(); }
    }

    [Fact]
    public void DeleteItemCommand_Requires_SelectedItem_And_Admin()
    {
        var (vm, session, conn) = BuildVmWithSession();
        try
        {
            vm.SelectedItem = new Item { Name = "X", Quantity = 1, Unit = "pcs" };

            // Staff cannot delete
            session.SetUser("staff", Roles.Staff);
            Assert.False(vm.DeleteItemCommand.CanExecute(null));

            // Manager cannot delete
            session.SetUser("manager", Roles.Manager);
            Assert.False(vm.DeleteItemCommand.CanExecute(null));

            // Admin can delete
            session.SetUser("admin", Roles.Admin);
            Assert.True(vm.DeleteItemCommand.CanExecute(null));
        }
        finally { conn.Dispose(); }
    }

    [Fact]
    public void SaveChangesCommand_Enabled_For_Admin_And_Manager_When_Items_Valid()
    {
        var (vm, session, conn) = BuildVmWithSession();
        try
        {
            // Add a valid item directly
            vm.Items.Add(new Item { Name = "Milk", Quantity = 2m, Unit = "pcs" });

            // No validation errors by default
            session.SetUser("staff", Roles.Staff);
            Assert.False(vm.SaveChangesCommand.CanExecute(null));

            session.SetUser("manager", Roles.Manager);
            Assert.True(vm.SaveChangesCommand.CanExecute(null));

            session.SetUser("admin", Roles.Admin);
            Assert.True(vm.SaveChangesCommand.CanExecute(null));
        }
        finally { conn.Dispose(); }
    }

    [Fact]
    public void ImportItemsCsvCommand_Enabled_For_Admin_And_Manager_Only()
    {
        var (vm, session, conn) = BuildVmWithSession();
        try
        {
            session.SetUser("staff", Roles.Staff);
            Assert.False(vm.ImportItemsCsvCommand.CanExecute(null));

            session.SetUser("manager", Roles.Manager);
            Assert.True(vm.ImportItemsCsvCommand.CanExecute(null));

            session.SetUser("admin", Roles.Admin);
            Assert.True(vm.ImportItemsCsvCommand.CanExecute(null));
        }
        finally { conn.Dispose(); }
    }

    [Fact]
    public void StockCommands_Respect_Roles()
    {
        var (vm, session, conn) = BuildVmWithSession();
        try
        {
            vm.SelectedItem = new Item { Name = "Rice", Quantity = 5m, Unit = "kg" };

            // Staff
            session.SetUser("staff", Roles.Staff);
            Assert.False(vm.AddStockCommand.CanExecute(null));
            Assert.True(vm.ConsumeStockCommand.CanExecute(null));
            Assert.False(vm.AdjustStockCommand.CanExecute(null));

            // Manager
            session.SetUser("manager", Roles.Manager);
            Assert.True(vm.AddStockCommand.CanExecute(null));
            Assert.True(vm.ConsumeStockCommand.CanExecute(null));
            Assert.True(vm.AdjustStockCommand.CanExecute(null));

            // Admin
            session.SetUser("admin", Roles.Admin);
            Assert.True(vm.AddStockCommand.CanExecute(null));
            Assert.True(vm.ConsumeStockCommand.CanExecute(null));
            Assert.True(vm.AdjustStockCommand.CanExecute(null));
        }
        finally { conn.Dispose(); }
    }

    [Fact]
    public void ManageCategoriesCommand_Enabled_For_Admin_And_Manager()
    {
        var (vm, session, conn) = BuildVmWithSession();
        try
        {
            session.SetUser("staff", Roles.Staff);
            Assert.False(vm.ManageCategoriesCommand.CanExecute(null));

            session.SetUser("manager", Roles.Manager);
            Assert.True(vm.ManageCategoriesCommand.CanExecute(null));

            session.SetUser("admin", Roles.Admin);
            Assert.True(vm.ManageCategoriesCommand.CanExecute(null));
        }
        finally { conn.Dispose(); }
    }

    [Fact]
    public void Commands_Reevaluate_On_SessionChanged()
    {
        var (vm, session, conn) = BuildVmWithSession();
        try
        {
            // Initially Staff
            session.SetUser("staff", Roles.Staff);
            Assert.False(vm.AddItemCommand.CanExecute(null));

            // Switch to Admin
            session.SetUser("admin", Roles.Admin);
            Assert.True(vm.AddItemCommand.CanExecute(null));

            // Back to Staff
            session.SetUser("staff", Roles.Staff);
            Assert.False(vm.AddItemCommand.CanExecute(null));
        }
        finally { conn.Dispose(); }
    }
}