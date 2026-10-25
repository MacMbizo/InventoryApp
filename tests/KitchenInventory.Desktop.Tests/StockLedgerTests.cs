using System;
using System.Threading.Tasks;
using FluentAssertions;
using KitchenInventory.Data;
using KitchenInventory.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

/// <summary>
/// Covers <see cref="StockLedger.ReconcileAsync"/>: the check that an item's stored quantity
/// matches the ledger implied by its movements under the signed Adjust convention
/// (<c>Quantity == ΣAdd − ΣConsume + ΣAdjust</c>).
/// </summary>
public sealed class StockLedgerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<KitchenInventoryDbContext> _options;

    public StockLedgerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<KitchenInventoryDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var db = new KitchenInventoryDbContext(_options);
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private KitchenInventoryDbContext CreateDb() => new(_options);

    private int SeedItem(string name, decimal quantity)
    {
        using var db = CreateDb();
        var item = new Item { Name = name, Quantity = quantity, Unit = "kg" };
        db.Items.Add(item);
        db.SaveChanges();
        return item.Id;
    }

    private void SeedMovements(params (int? ItemId, MovementType Type, decimal Quantity)[] movements)
    {
        using var db = CreateDb();
        foreach (var (itemId, type, quantity) in movements)
        {
            db.StockMovements.Add(new StockMovement
            {
                ItemId = itemId,
                Type = type,
                Quantity = quantity,
                TimestampUtc = DateTime.UtcNow
            });
        }
        db.SaveChanges();
    }

    [Fact]
    public async Task ReconcileAsync_ReturnsEmpty_WhenAddAndConsumeMatchQuantity()
    {
        var rice = SeedItem("Rice", 7m);
        SeedMovements((rice, MovementType.Add, 10m), (rice, MovementType.Consume, 3m));

        using var db = CreateDb();
        var discrepancies = await StockLedger.ReconcileAsync(db);

        discrepancies.Should().BeEmpty();
    }

    [Fact]
    public async Task ReconcileAsync_ReturnsEmpty_WhenAdjustIsASignedDecrease()
    {
        // Regression: a stock count that lowers the quantity must record the negative delta,
        // otherwise the ledger over-reports and the item can never be reconciled.
        var rice = SeedItem("Rice", 6m);
        SeedMovements((rice, MovementType.Add, 10m), (rice, MovementType.Adjust, -4m));

        using var db = CreateDb();
        var discrepancies = await StockLedger.ReconcileAsync(db);

        discrepancies.Should().BeEmpty();
    }

    [Fact]
    public async Task ReconcileAsync_ReturnsEmpty_WhenAdjustIsASignedIncrease()
    {
        var rice = SeedItem("Rice", 15m);
        SeedMovements((rice, MovementType.Add, 10m), (rice, MovementType.Adjust, 5m));

        using var db = CreateDb();
        var discrepancies = await StockLedger.ReconcileAsync(db);

        discrepancies.Should().BeEmpty();
    }

    [Fact]
    public async Task ReconcileAsync_FlagsItem_WhenAdjustStoredAMagnitudeInsteadOfASignedDelta()
    {
        // Legacy rows stored Math.Abs(delta) for Adjust; a downward count is indistinguishable
        // from an upward one and shows up as a discrepancy.
        var rice = SeedItem("Rice", 6m);
        SeedMovements((rice, MovementType.Add, 10m), (rice, MovementType.Adjust, 4m));

        using var db = CreateDb();
        var discrepancies = await StockLedger.ReconcileAsync(db);

        discrepancies.Should().ContainSingle();
        var discrepancy = discrepancies[0];
        discrepancy.ItemId.Should().Be(rice);
        discrepancy.ItemName.Should().Be("Rice");
        discrepancy.RecordedQuantity.Should().Be(6m);
        discrepancy.LedgerQuantity.Should().Be(14m);
        discrepancy.Difference.Should().Be(-8m);
    }

    [Fact]
    public async Task ReconcileAsync_FlagsItem_ThatHasNoMovements()
    {
        var rice = SeedItem("Rice", 5m);

        using var db = CreateDb();
        var discrepancies = await StockLedger.ReconcileAsync(db);

        discrepancies.Should().ContainSingle();
        discrepancies[0].ItemId.Should().Be(rice);
        discrepancies[0].LedgerQuantity.Should().Be(0m);
    }

    [Fact]
    public async Task ReconcileAsync_IgnoresToleranceSizedDifference()
    {
        var rice = SeedItem("Rice", 5.001m);
        SeedMovements((rice, MovementType.Add, 5m));

        using var db = CreateDb();
        var discrepancies = await StockLedger.ReconcileAsync(db);

        discrepancies.Should().BeEmpty();
    }

    [Fact]
    public async Task ReconcileAsync_IgnoresMovementsWhoseItemNoLongerExists()
    {
        var rice = SeedItem("Rice", 5m);
        SeedMovements(
            (rice, MovementType.Add, 5m),
            (null, MovementType.Adjust, -3m)); // orphaned tombstone: no item to attribute it to

        using var db = CreateDb();
        var discrepancies = await StockLedger.ReconcileAsync(db);

        discrepancies.Should().BeEmpty();
    }
}
