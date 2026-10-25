using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using KitchenInventory.Data;
using KitchenInventory.Desktop.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public class DatabaseInfoServiceTests
{
    private static IDbContextFactory<KitchenInventoryDbContext> SqliteFactory(string dbPath)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<KitchenInventoryDbContext>(o => o.UseSqlite($"Data Source={dbPath}"));
        return services.BuildServiceProvider().GetRequiredService<IDbContextFactory<KitchenInventoryDbContext>>();
    }

    [Fact]
    public async Task GetInfoAsync_Sqlite_ReportsProviderTargetAndHealth()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dbinfo-{Guid.NewGuid():N}.db");
        try
        {
            var factory = SqliteFactory(dbPath);
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.MigrateAsync();
            }

            var svc = new DatabaseInfoService(factory);
            var info = await svc.GetInfoAsync();

            Assert.Equal(DatabaseProvider.Sqlite, info.Provider);
            Assert.Equal("OK", info.Health.Status);
            Assert.Equal("Connected", info.Health.Message);
            Assert.True(info.AppliedMigrations > 0);
            Assert.Equal(0, info.PendingMigrations);
            Assert.Equal(dbPath, info.DatabaseFilePath);
            Assert.Equal(dbPath, info.Target);
            Assert.Contains("InventoryApp", info.LogsDirectory);
            Assert.Contains("logs", info.LogsDirectory);
            Assert.NotEqual("unknown", info.ProviderVersion);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task Migrate_FromPreviousSchema_AddsSoftDeleteColumnsAndKeepsData()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"dbupgrade-{Guid.NewGuid():N}.db");
        try
        {
            var factory = SqliteFactory(dbPath);

            // Simulate a machine still holding the schema that shipped before soft delete.
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.GetService<IMigrator>().MigrateAsync("20261024145315_AddLocation");
            }

            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.ExecuteSqlRawAsync(
                    "INSERT INTO Items (Name, Quantity, Unit, CreatedAtUtc) VALUES ('Rice', '10', 'kg', '2026-01-01 00:00:00')");
            }

            // Upgrade in place, the way App.OnStartup does.
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.MigrateAsync();
            }

            await using (var db = await factory.CreateDbContextAsync())
            {
                var item = await db.Items.SingleAsync(i => i.Name == "Rice");
                Assert.Equal(10m, item.Quantity);
                Assert.False(item.IsDeleted);
                Assert.Equal(Guid.Empty, item.RowVersion);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task GetInfoAsync_Npgsql_Unreachable_ReportsErrorHealthAndTarget()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<KitchenInventoryDbContext>(o =>
            o.UseNpgsql("Host=127.0.0.1;Port=1;Database=inventory;Username=kitchen;Password=secret;Timeout=1;Pooling=false"));
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IDbContextFactory<KitchenInventoryDbContext>>();

        var svc = new DatabaseInfoService(factory);
        var info = await svc.GetInfoAsync();

        Assert.Equal(DatabaseProvider.Npgsql, info.Provider);
        Assert.Equal("127.0.0.1:1/inventory", info.Target);
        Assert.Null(info.DatabaseFilePath);
        Assert.Equal("Error", info.Health.Status);
        Assert.DoesNotContain("secret", info.RedactedConnection);
    }

    [Fact]
    public void RedactConnectionString_MasksSensitiveKeysAndKeepsOthers()
    {
        var svc = new DatabaseInfoService(null!);

        var redacted = svc.RedactConnectionString(
            "Host=h;Password=abc;Pwd=xyz;User ID=admin;UserId=u2;Database=db");

        Assert.DoesNotContain("abc", redacted);
        Assert.DoesNotContain("xyz", redacted);
        Assert.DoesNotContain("admin", redacted);
        Assert.DoesNotContain("u2", redacted);
        Assert.Contains("Database=db", redacted);
        Assert.Contains("Host=h", redacted);
    }

    [Fact]
    public void RedactConnectionString_BlankInput_IsReturnedUnchanged()
    {
        var svc = new DatabaseInfoService(null!);

        Assert.Equal(string.Empty, svc.RedactConnectionString(string.Empty));
        Assert.Equal("   ", svc.RedactConnectionString("   "));
        Assert.Null(svc.RedactConnectionString(null!));
    }
}
