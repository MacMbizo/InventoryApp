using System;
using System.Collections.Generic;
using System.Linq;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Domain.Entities;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public class ReportingServiceTests
{
    private static Item Item(int id, string name, decimal qty, string unit = "kg",
        string? category = null, string? location = null, DateTime? expiry = null)
        => new()
        {
            Id = id,
            Name = name,
            Quantity = qty,
            Unit = unit,
            ExpiryDate = expiry,
            Category = category == null ? null : new Category { Id = id, Name = category },
            Location = location == null ? null : new Location { Id = id, Name = location }
        };

    private static string Cell(ReportTable table, int row, string header)
    {
        var index = table.Headers.ToList().IndexOf(header);
        Assert.True(index >= 0, $"Header '{header}' not found.");
        return table.Rows[row][index];
    }

    [Theory]
    [InlineData(ReportKind.StockOnHand, "Stock on Hand")]
    [InlineData(ReportKind.LowStock, "Low Stock")]
    [InlineData(ReportKind.ExpiringSoon, "Expiring Soon")]
    [InlineData(ReportKind.MovementHistory, "Movement History")]
    public void Title_ReturnsFriendlyName(ReportKind kind, string expected)
        => Assert.Equal(expected, ReportingService.Title(kind));

    [Fact]
    public void Title_UnknownKind_FallsBackToEnumName()
        => Assert.Equal("99", ReportingService.Title((ReportKind)99));

    [Fact]
    public void ReportTable_NullArguments_YieldEmptyCollections()
    {
        var table = new ReportTable(null!, null!, null!);
        Assert.Equal(string.Empty, table.Title);
        Assert.Empty(table.Headers);
        Assert.Empty(table.Rows);
        Assert.Equal(0, table.RowCount);
    }

    [Fact]
    public void StockOnHand_NullItems_ReturnsEmptyRows()
    {
        var table = ReportingService.StockOnHand(null);
        Assert.Equal(0, table.RowCount);
        Assert.Equal(new[] { "Id", "Name", "Category", "Location", "Quantity", "Unit", "Expiry Date" }, table.Headers);
    }

    [Fact]
    public void StockOnHand_FormatsValues_AndSortsByName()
    {
        var items = new[]
        {
            Item(2, "Sugar", 2.5m, "kg", "Baking", "Pantry", new DateTime(2026, 3, 4)),
            Item(1, "Rice", 10m, "kg", "Grains", "Store"),
            null!
        };

        var table = ReportingService.StockOnHand(items);

        Assert.Equal(2, table.RowCount);
        Assert.Equal("Rice", Cell(table, 0, "Name"));
        Assert.Equal("Sugar", Cell(table, 1, "Name"));
        Assert.Equal("1", Cell(table, 0, "Id"));
        Assert.Equal("Grains", Cell(table, 0, "Category"));
        Assert.Equal("Store", Cell(table, 0, "Location"));
        Assert.Equal("10", Cell(table, 0, "Quantity"));
        Assert.Equal(string.Empty, Cell(table, 0, "Expiry Date"));
        Assert.Equal("2.5", Cell(table, 1, "Quantity"));
        Assert.Equal("2026-03-04", Cell(table, 1, "Expiry Date"));
    }

    [Fact]
    public void StockOnHand_MissingCategoryAndLocation_ProducesBlankCells()
    {
        var table = ReportingService.StockOnHand(new[] { Item(1, "Salt", 1m) });

        Assert.Equal(string.Empty, Cell(table, 0, "Category"));
        Assert.Equal(string.Empty, Cell(table, 0, "Location"));
    }

    [Fact]
    public void LowStock_FiltersBelowThreshold_AndOrdersByQuantityDescendingPriority()
    {
        var items = new[]
        {
            Item(1, "Rice", 10m),
            Item(2, "Sugar", 3m),
            Item(3, "Salt", 0m)
        };

        var table = ReportingService.LowStock(items, 5m);

        Assert.Equal("Low Stock (below 5)", table.Title);
        Assert.Equal(2, table.RowCount);
        Assert.Equal("Salt", Cell(table, 0, "Name"));
        Assert.Equal("Sugar", Cell(table, 1, "Name"));
    }

    [Fact]
    public void LowStock_NullItems_ReturnsEmptyRows()
        => Assert.Equal(0, ReportingService.LowStock(null, 5m).RowCount);

    [Fact]
    public void ExpiringSoon_IncludesWindowBoundaries_AndExcludesExpired()
    {
        var today = new DateTime(2026, 1, 10);
        var items = new[]
        {
            Item(1, "Today", 1m, expiry: today),
            Item(2, "Soon", 1m, expiry: today.AddDays(3)),
            Item(3, "Boundary", 1m, expiry: today.AddDays(7)),
            Item(4, "TooLate", 1m, expiry: today.AddDays(8)),
            Item(5, "Expired", 1m, expiry: today.AddDays(-1)),
            Item(6, "NoExpiry", 1m)
        };

        var table = ReportingService.ExpiringSoon(items, 7, today);

        Assert.Equal("Expiring Soon (next 7 day(s))", table.Title);
        Assert.Equal(3, table.RowCount);
        Assert.Equal("Today", Cell(table, 0, "Name"));
        Assert.Equal("0", Cell(table, 0, "Days To Expiry"));
        Assert.Equal("Soon", Cell(table, 1, "Name"));
        Assert.Equal("3", Cell(table, 1, "Days To Expiry"));
        Assert.Equal("Boundary", Cell(table, 2, "Name"));
        Assert.Equal("7", Cell(table, 2, "Days To Expiry"));
    }

    [Fact]
    public void ExpiringSoon_NegativeWindow_TreatedAsTodayOnly()
    {
        var today = new DateTime(2026, 1, 10);
        var table = ReportingService.ExpiringSoon(new[]
        {
            Item(1, "Today", 1m, expiry: today),
            Item(2, "Tomorrow", 1m, expiry: today.AddDays(1))
        }, -5, today);

        Assert.Equal("Expiring Soon (next 0 day(s))", table.Title);
        Assert.Equal(1, table.RowCount);
        Assert.Equal("Today", Cell(table, 0, "Name"));
    }

    [Fact]
    public void ExpiringSoon_NullItems_ReturnsEmptyRows()
    {
        Assert.Equal(0, ReportingService.ExpiringSoon(null, 7, DateTime.Today).RowCount);
        Assert.Equal(0, ReportingService.ExpiringSoon(new[] { (Item)null! }, 7, DateTime.Today).RowCount);
    }

    [Fact]
    public void MovementHistory_FiltersByRange_OrdersNewestFirst_AndResolvesNames()
    {
        var baseTime = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
        var movements = new[]
        {
            new StockMovement { Id = 1, ItemId = 7, Type = MovementType.Add, Quantity = 5m, Reason = "Initial add", User = "seed", TimestampUtc = baseTime },
            new StockMovement { Id = 2, ItemId = 8, Type = MovementType.Consume, Quantity = 2.5m, Reason = "Manual edit", User = "cook", TimestampUtc = baseTime.AddDays(3) },
            new StockMovement { Id = 3, ItemId = 9, Type = MovementType.Adjust, Quantity = 1m, Reason = "Stock take", User = "chef", TimestampUtc = baseTime.AddDays(10) }
        };
        var names = new Dictionary<int, string> { [7] = "Rice", [8] = "Milk", [9] = "Sugar" };

        var table = ReportingService.MovementHistory(movements, baseTime.AddDays(-1), baseTime.AddDays(5), names);

        Assert.Equal("Movement History", table.Title);
        Assert.Equal(2, table.RowCount);
        Assert.Equal("2", Cell(table, 0, "Id"));
        Assert.Equal("Milk", Cell(table, 0, "Item"));
        Assert.Equal("Consume", Cell(table, 0, "Type"));
        Assert.Equal("2.5", Cell(table, 0, "Quantity"));
        Assert.Equal("Manual edit", Cell(table, 0, "Reason"));
        Assert.Equal("cook", Cell(table, 0, "User"));
        Assert.Equal("2026-02-04 12:00", Cell(table, 0, "Timestamp (UTC)"));
        Assert.Equal("Rice", Cell(table, 1, "Item"));
    }

    [Fact]
    public void MovementHistory_PrefersNavigatedItemName_OverLookup()
    {
        var movement = new StockMovement
        {
            Id = 1,
            ItemId = 3,
            Item = new Item { Id = 3, Name = "Navigated" },
            Type = MovementType.Add,
            Quantity = 1m,
            TimestampUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var table = ReportingService.MovementHistory(
            new[] { movement }, null, null, new Dictionary<int, string> { [3] = "Lookup" });

        Assert.Equal("Navigated", Cell(table, 0, "Item"));
    }

    [Fact]
    public void MovementHistory_MissingEverything_ProducesBlankItemAndCells()
    {
        var movement = new StockMovement { Id = 1, Type = MovementType.Add, Quantity = 1m, TimestampUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };

        var table = ReportingService.MovementHistory(new[] { movement }, null, null);

        Assert.Equal(string.Empty, Cell(table, 0, "Item"));
        Assert.Equal(string.Empty, Cell(table, 0, "Reason"));
        Assert.Equal(string.Empty, Cell(table, 0, "User"));
    }

    [Fact]
    public void MovementHistory_NullMovements_ReturnsEmptyRows()
    {
        Assert.Equal(0, ReportingService.MovementHistory(null, null, null).RowCount);
        Assert.Equal(0, ReportingService.MovementHistory(new[] { (StockMovement)null! }, null, null).RowCount);
    }

    [Fact]
    public void MovementHistory_WithoutBounds_ReturnsEverything()
    {
        var movements = new[]
        {
            new StockMovement { Id = 1, Type = MovementType.Add, Quantity = 1m, TimestampUtc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc) },
            new StockMovement { Id = 2, Type = MovementType.Add, Quantity = 1m, TimestampUtc = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc) }
        };

        Assert.Equal(2, ReportingService.MovementHistory(movements, null, null).RowCount);
    }
}
