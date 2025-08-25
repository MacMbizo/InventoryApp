using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Domain.Entities;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public class CsvExportTests
{
    [Fact]
    public void ExportItems_IncludesHeader_AndEscapesFields()
    {
        var fixedCreated = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var fixedUpdated = new DateTime(2025, 1, 2, 3, 5, 6, DateTimeKind.Utc);

        var items = new List<Item>
        {
            new Item { Id = 1, Name = "Apple", Quantity = 5m, Unit = "pcs", CreatedAtUtc = fixedCreated, UpdatedAtUtc = fixedUpdated },
            new Item { Id = 2, Name = "Cheese, \"Gouda\"", Quantity = 2.5m, Unit = "kg", ExpiryDate = new DateTime(2025, 12, 31), CreatedAtUtc = fixedCreated, UpdatedAtUtc = fixedUpdated },
            new Item { Id = 3, Name = string.Empty, Quantity = 0m, Unit = string.Empty }
        };

        var csv = CsvExportService.ExportItems(items);
        var lines = csv.TrimEnd().Split('\n');

        Assert.True(lines.Length >= 4, $"Expected at least 4 lines, got {lines.Length}\n{csv}");
        Assert.Equal("Id,Name,Quantity,Unit,ExpiryDate,CreatedAtUtc,UpdatedAtUtc", lines[0]);

        // First item line
        Assert.Contains("1,Apple,5,pcs,," + fixedCreated.ToString("o", CultureInfo.InvariantCulture) + "," + fixedUpdated.ToString("o", CultureInfo.InvariantCulture), lines[1]);

        // Second item line with escaping and date
        var expectedName = "\"Cheese, \"\"Gouda\"\"\""; // quotes doubled and field quoted
        var expectedExpiry = "2025-12-31";
        Assert.Contains($"2,{expectedName},2.5,kg,{expectedExpiry},{fixedCreated:o},{fixedUpdated:o}", lines[2]);

        // Third item line with empty optional fields
        Assert.StartsWith("3,,0,,", lines[3]);
    }

    [Fact]
    public void ExportMovements_UsesItemName_WhenItemProvided()
    {
        var movement = new StockMovement
        {
            Id = 10,
            ItemId = 5,
            Item = new Item { Id = 5, Name = "Bananas" },
            Type = MovementType.Add,
            Quantity = 3,
            Reason = "Restock",
            User = "tester",
            TimestampUtc = new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc)
        };

        var csv = CsvExportService.ExportMovements(new[] { movement });
        var lines = csv.TrimEnd().Split('\n');

        Assert.Equal("Id,ItemId,ItemName,Type,Quantity,Reason,User,TimestampUtc", lines[0]);
        Assert.Contains(
            ",5,Bananas,Add,3,Restock,tester,2025-02-03T04:05:06.0000000Z",
            lines[1]
        );
    }

    [Fact]
    public void ExportMovements_UsesLookup_WhenItemNull()
    {
        var movement = new StockMovement
        {
            Id = 11,
            ItemId = 42,
            Item = null,
            Type = MovementType.Consume,
            Quantity = 1.5m,
            Reason = "Usage",
            User = "qa",
            TimestampUtc = new DateTime(2025, 3, 4, 5, 6, 7, DateTimeKind.Utc)
        };

        var map = new Dictionary<int, string> { { 42, "MappedName" } };
        var csv = CsvExportService.ExportMovements(new[] { movement }, map);
        var lines = csv.TrimEnd().Split('\n');

        Assert.Contains(
            ",42,MappedName,Consume,1.5,Usage,qa,2025-03-04T05:06:07.0000000Z",
            lines[1]
        );
    }

    [Fact]
    public void ExportMovements_EscapesReasonAndUser()
    {
        var movement = new StockMovement
        {
            Id = 12,
            ItemId = 1,
            Item = new Item { Id = 1, Name = "Test" },
            Type = MovementType.Adjust,
            Quantity = 2,
            Reason = "Fix, \"Manual\"",
            User = "Doe, John",
            TimestampUtc = new DateTime(2025, 4, 5, 6, 7, 8, DateTimeKind.Utc)
        };

        var csv = CsvExportService.ExportMovements(new[] { movement });
        var line = csv.TrimEnd().Split('\n')[1];

        Assert.Contains("\"Fix, \"\"Manual\"\"\"", line); // escaped reason
        Assert.Contains("\"Doe, John\"", line); // quoted user
    }
}