using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Domain.Entities;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public class CsvServicesTests
{
    [Fact]
    public async Task ParseItemsAsync_EmptyOrNull_ReturnsEmpty()
    {
        var svc = new CsvImportService();
        Assert.Empty(await svc.ParseItemsAsync(string.Empty, new List<Category>()));
        Assert.Empty(await svc.ParseItemsAsync("   ", new List<Category>()));
    }

    [Fact]
    public async Task ParseItemsAsync_HeaderOnly_ReturnsEmpty()
    {
        var svc = new CsvImportService();
        var result = await svc.ParseItemsAsync("Id,Name,Quantity,Unit", new List<Category>());
        Assert.Empty(result);
    }

    [Fact]
    public async Task ParseItemsAsync_ParsesQuotedValuesAndAllColumns()
    {
        var svc = new CsvImportService();
        var categories = new List<Category> { new() { Id = 1, Name = "Grains" } };
        var locations = new List<Location> { new() { Id = 7, Name = "Pantry" } };

        var csv = "Id,Name,Quantity,Unit,CategoryId,CategoryName,LocationId,LocationName,ExpiryDate,CreatedAtUtc,UpdatedAtUtc\n" +
                  "1,\"Rice, white\",2.5,kg,1,,7,,2026-01-15,2026-01-01T00:00:00Z,2026-01-02T00:00:00Z";

        var result = await svc.ParseItemsAsync(csv, categories, locations);

        var item = Assert.Single(result);
        Assert.Equal(1, item.Id);
        Assert.Equal("Rice, white", item.Name);
        Assert.Equal(2.5m, item.Quantity);
        Assert.Equal("kg", item.Unit);
        Assert.Equal(1, item.CategoryId);
        Assert.Equal(7, item.LocationId);
        Assert.NotNull(item.ExpiryDate);
        Assert.Equal(new DateTime(2026, 1, 15), item.ExpiryDate!.Value.Date);
        Assert.NotNull(item.UpdatedAtUtc);
    }

    [Fact]
    public async Task ParseItemsAsync_ResolvesCategoryAndLocationByName()
    {
        var svc = new CsvImportService();
        var categories = new List<Category> { new() { Id = 3, Name = "Dairy" } };
        var locations = new List<Location> { new() { Id = 4, Name = "Fridge" } };

        var csv = "Id,Name,Quantity,Unit,CategoryName,LocationName\n" +
                  "0,Milk,1,L,Dairy,Fridge";

        var result = await svc.ParseItemsAsync(csv, categories, locations);

        var item = Assert.Single(result);
        Assert.Equal(3, item.CategoryId);
        Assert.Equal(4, item.LocationId);
    }

    [Fact]
    public async Task ParseItemsAsync_UnknownCategoryAndLocationBecomeNull()
    {
        var svc = new CsvImportService();
        var categories = new List<Category> { new() { Id = 1, Name = "Grains" } };
        var locations = new List<Location> { new() { Id = 1, Name = "Pantry" } };

        var csv = "Id,Name,Quantity,Unit,CategoryId,CategoryName,LocationId,LocationName\n" +
                  "0,Sugar,5,kg,99,Unknown,42,Nowhere";

        var result = await svc.ParseItemsAsync(csv, categories, locations);

        var item = Assert.Single(result);
        Assert.Null(item.CategoryId);
        Assert.Null(item.LocationId);
    }

    [Fact]
    public async Task ParseItemsAsync_SkipsBlankLinesAndToleratesMissingColumns()
    {
        var svc = new CsvImportService();
        var csv = "Id,Name\n" +
                  "\n" +
                  "0,Bread\n" +
                  "   \n";

        var result = await svc.ParseItemsAsync(csv, new List<Category>());

        var item = Assert.Single(result);
        Assert.Equal("Bread", item.Name);
        Assert.Equal(0m, item.Quantity);
    }

    [Fact]
    public async Task ParseItemsAsync_InvalidExpiryDate_LeavesExpiryNull()
    {
        var svc = new CsvImportService();
        var csv = "Id,Name,Quantity,Unit,ExpiryDate\n" +
                  "0,Milk,1,L,15/01/2026";

        var result = await svc.ParseItemsAsync(csv, new List<Category>());

        Assert.Null(Assert.Single(result).ExpiryDate);
    }

    [Fact]
    public async Task Analyzer_ValidRow_BecomesAdd_AndUpdateWhenExisting()
    {
        var analyzer = new CsvImportPreviewAnalyzer();
        var categories = new List<Category> { new() { Id = 1, Name = "Grains" } };
        var locations = new List<Location> { new() { Id = 2, Name = "Pantry" } };
        var csv = "Id,Name,Quantity,Unit,CategoryName,LocationId\n" +
                  "0,Rice,3,kg,Grains,2";

        var addPreview = await analyzer.AnalyzeAsync(csv, categories, new List<Item>(), locations);
        Assert.Equal(1, addPreview.Adds);
        Assert.Equal(0, addPreview.Error);
        Assert.Equal("Add", addPreview.Rows[0].Action);
        Assert.Equal("Grains", addPreview.Rows[0].CategoryDisplay);
        Assert.Equal("Pantry", addPreview.Rows[0].LocationDisplay);

        var existing = new List<Item> { new() { Id = 0, Name = "Rice" } };
        var updatePreview = await analyzer.AnalyzeAsync(csv, categories, existing, locations);
        Assert.Equal(1, updatePreview.Updates);
        Assert.Equal("Update", updatePreview.Rows[0].Action);
    }

    [Fact]
    public async Task Analyzer_FlagsAllValidationProblems()
    {
        var analyzer = new CsvImportPreviewAnalyzer();
        var categories = new List<Category> { new() { Id = 1, Name = "Grains" } };
        var locations = new List<Location> { new() { Id = 1, Name = "Pantry" } };
        var longName = new string('x', 201);
        var csv = "Id,Name,Quantity,Unit,CategoryId,CategoryName,LocationId,LocationName,ExpiryDate\n" +
                  $"0,,{2000000},THISUNITNAMEISWAYTOOLONGFORTHECOLUMN,42,,42,,\n" +
                  $"0,{longName},-5,ok,,,,,\n" +
                  "0,Beans,1,kg,,UnknownCat,,UnknownLoc,not-a-date";

        var preview = await analyzer.AnalyzeAsync(csv, categories, new List<Item>(), locations);

        Assert.Equal(3, preview.Total);
        Assert.Equal(0, preview.Valid);
        Assert.Equal(3, preview.Error);

        var allMessages = preview.Rows.SelectMany(r => r.Issues).Select(i => i.Message).ToList();
        Assert.Contains("Item name is required.", allMessages);
        Assert.Contains("Item name is too long (max 200).", allMessages);
        Assert.Contains("Unit is too long (max 32).", allMessages);
        Assert.Contains("Quantity is unrealistically large.", allMessages);
        Assert.Contains("Quantity cannot be negative.", allMessages);
        Assert.Contains("Invalid date format (expected yyyy-MM-dd).", allMessages);
        Assert.Contains("Unknown category id.", allMessages);
        Assert.Contains("Unknown category name.", allMessages);
        Assert.Contains("Unknown location id.", allMessages);
        Assert.Contains("Unknown location name.", allMessages);
    }

    [Fact]
    public async Task Analyzer_EmptyContent_ReturnsEmptyResult()
    {
        var analyzer = new CsvImportPreviewAnalyzer();
        var preview = await analyzer.AnalyzeAsync("", new List<Category>(), new List<Item>());
        Assert.Equal(0, preview.Total);
        Assert.Empty(preview.ValidItems);
    }

    [Fact]
    public async Task Analyzer_BuildErrorsCsv_EscapesSpecialCharacters()
    {
        var analyzer = new CsvImportPreviewAnalyzer();
        var csv = "Id,Name,Quantity,Unit,ExpiryDate\n" +
                  "0,\"Milk, semi\",1,L,not-a-date";
        var preview = await analyzer.AnalyzeAsync(csv, new List<Category>(), new List<Item>());

        var errorsCsv = CsvImportPreviewAnalyzer.BuildErrorsCsv(preview);

        Assert.StartsWith("Row,Field,Message,Value", errorsCsv);
        Assert.Contains("ExpiryDate", errorsCsv);
        Assert.Contains("Invalid date format (expected yyyy-MM-dd).", errorsCsv);
    }

    [Fact]
    public void IssueSummary_JoinsMessages()
    {
        var row = new CsvImportPreviewRow
        {
            Issues = new List<CsvImportIssue>
            {
                new() { Message = "first" },
                new() { Message = "second" }
            }
        };

        Assert.Equal("first; second", row.IssueSummary);
        Assert.False(row.IsValid);

        var clean = new CsvImportPreviewRow();
        Assert.Equal(string.Empty, clean.IssueSummary);
        Assert.True(clean.IsValid);
    }
}
