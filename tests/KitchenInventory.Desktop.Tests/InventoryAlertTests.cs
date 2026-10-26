using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

public sealed class InventoryAlertTests
{
    private static readonly DateTime Today = new(2025, 6, 15);

    private static Item MakeItem(
        string name,
        decimal quantity = 10m,
        string unit = "pcs",
        DateTime? expiry = null,
        Category? category = null,
        Location? location = null)
        => new Item
        {
            Id = name.GetHashCode() & 0x7fffffff,
            Name = name,
            Quantity = quantity,
            Unit = unit,
            ExpiryDate = expiry,
            Category = category,
            Location = location
        };

    [Fact]
    public void Evaluate_LowStock_ProducesWarning()
    {
        var alerts = InventoryAlertService.Evaluate(new[] { MakeItem("Flour", quantity: 2m, unit: "kg") }, 5m, 7, Today);

        alerts.Should().ContainSingle();
        alerts[0].Kind.Should().Be(AlertKind.LowStock);
        alerts[0].Severity.Should().Be(AlertSeverity.Warning);
        alerts[0].Message.Should().Be("Low stock (2 kg)");
        alerts[0].DaysToExpiry.Should().BeNull();
    }

    [Fact]
    public void Evaluate_OutOfStock_ProducesCritical()
    {
        var alerts = InventoryAlertService.Evaluate(new[] { MakeItem("Rice", quantity: 0m) }, 5m, 7, Today);

        alerts.Should().ContainSingle();
        alerts[0].Kind.Should().Be(AlertKind.OutOfStock);
        alerts[0].Severity.Should().Be(AlertSeverity.Critical);
    }

    [Fact]
    public void Evaluate_Expired_ProducesCriticalWithNegativeDays()
    {
        var alerts = InventoryAlertService.Evaluate(
            new[] { MakeItem("Yoghurt", expiry: Today.AddDays(-1)) }, 5m, 7, Today);

        alerts.Should().ContainSingle();
        alerts[0].Kind.Should().Be(AlertKind.Expired);
        alerts[0].Severity.Should().Be(AlertSeverity.Critical);
        alerts[0].DaysToExpiry.Should().Be(-1);
        alerts[0].Message.Should().Be("Expired yesterday");
    }

    [Fact]
    public void Evaluate_ExpiresToday_IsCriticalExpiringSoon()
    {
        var alerts = InventoryAlertService.Evaluate(
            new[] { MakeItem("Milk", expiry: Today) }, 5m, 7, Today);

        alerts.Should().ContainSingle();
        alerts[0].Kind.Should().Be(AlertKind.ExpiringSoon);
        alerts[0].Severity.Should().Be(AlertSeverity.Critical);
        alerts[0].DaysToExpiry.Should().Be(0);
        alerts[0].Message.Should().Be("Expires today");
    }

    [Fact]
    public void Evaluate_ExpiringWithinThreshold_ProducesWarning()
    {
        var alerts = InventoryAlertService.Evaluate(
            new[] { MakeItem("Cheese", expiry: Today.AddDays(3)) }, 5m, 7, Today);

        alerts.Should().ContainSingle();
        alerts[0].Kind.Should().Be(AlertKind.ExpiringSoon);
        alerts[0].Severity.Should().Be(AlertSeverity.Warning);
        alerts[0].DaysToExpiry.Should().Be(3);
        alerts[0].Message.Should().Be("Expires in 3 days");
    }

    [Fact]
    public void Evaluate_ExpiringTomorrow_UsesSingularWording()
    {
        var alerts = InventoryAlertService.Evaluate(
            new[] { MakeItem("Bread", expiry: Today.AddDays(1)) }, 5m, 7, Today);

        alerts.Should().ContainSingle();
        alerts[0].Message.Should().Be("Expires tomorrow");
    }

    [Fact]
    public void Evaluate_ExpiringBeyondThreshold_ProducesNoAlert()
    {
        var alerts = InventoryAlertService.Evaluate(
            new[] { MakeItem("Salt", expiry: Today.AddDays(30)) }, 5m, 7, Today);

        alerts.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_ItemCanProduceBothStockAndExpiryAlerts()
    {
        var alerts = InventoryAlertService.Evaluate(
            new[] { MakeItem("Butter", quantity: 0m, expiry: Today.AddDays(-2)) }, 5m, 7, Today);

        alerts.Should().HaveCount(2);
        alerts.Should().Contain(a => a.Kind == AlertKind.Expired);
        alerts.Should().Contain(a => a.Kind == AlertKind.OutOfStock);
    }

    [Fact]
    public void Evaluate_OrdersCriticalBeforeWarning()
    {
        var alerts = InventoryAlertService.Evaluate(new[]
        {
            MakeItem("LowSugar", quantity: 1m),       // LowStock -> Warning
            MakeItem("OutRice", quantity: 0m)          // OutOfStock -> Critical
        }, 5m, 7, Today);

        alerts.Should().HaveCount(2);
        alerts[0].Severity.Should().Be(AlertSeverity.Critical);
        alerts[1].Severity.Should().Be(AlertSeverity.Warning);
    }

    [Fact]
    public void Evaluate_NullItems_ReturnsEmpty()
    {
        InventoryAlertService.Evaluate(null, 5m, 7, Today).Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_NegativeExpiringSoonDays_TreatedAsZero()
    {
        var alerts = InventoryAlertService.Evaluate(
            new[] { MakeItem("Yoghurt", expiry: Today.AddDays(2)) }, 5m, -5, Today);

        alerts.Should().BeEmpty();
    }

    // ----- ItemsViewModel integration -----

    private sealed class TestDbContextFactory : IDbContextFactory<KitchenInventoryDbContext>
    {
        private readonly DbContextOptions<KitchenInventoryDbContext> _options;
        public TestDbContextFactory(DbContextOptions<KitchenInventoryDbContext> options) => _options = options;
        public KitchenInventoryDbContext CreateDbContext() => new KitchenInventoryDbContext(_options);
        public ValueTask<KitchenInventoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => new(new KitchenInventoryDbContext(_options));
    }

    private sealed class NullFileSaveService : IFileSaveService
    {
        public Task<bool> SaveTextAsAsync(string suggestedFileName, string content) => Task.FromResult(false);

        public Task<bool> SaveBytesAsAsync(string suggestedFileName, byte[] content, string filter, string defaultExt)
            => Task.FromResult(false);
    }

    private sealed class NullFileOpenService : IFileOpenService
    {
        public Task<string?> OpenTextFileAsync(string filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*")
            => Task.FromResult<string?>(null);
    }

    private sealed class NullCsvImportService : ICsvImportService
    {
        public Task<IReadOnlyList<Item>> ParseItemsAsync(string csvContent, List<Category> categories, List<Location>? locations = null)
            => Task.FromResult((IReadOnlyList<Item>)Array.Empty<Item>());
    }

    [StaFact]
    public async Task LoadAsync_PopulatesAlertsAndCounts()
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();
        try
        {
            var options = new DbContextOptionsBuilder<KitchenInventoryDbContext>().UseSqlite(conn).Options;
            using (var seed = new KitchenInventoryDbContext(options))
            {
                seed.Database.EnsureCreated();
                seed.Items.AddRange(
                    new Item { Name = "Flour", Quantity = 2m, Unit = "kg" },                              // low stock
                    new Item { Name = "Cheese", Quantity = 10m, Unit = "pcs", ExpiryDate = DateTime.Today.AddDays(3) }, // expiring soon
                    new Item { Name = "Rice", Quantity = 20m, Unit = "kg" });                             // no alert
                await seed.SaveChangesAsync();
            }

            var vm = new ItemsViewModel(
                new TestDbContextFactory(options),
                NullLogger<ItemsViewModel>.Instance,
                new NullFileSaveService(),
                new NullFileOpenService(),
                new NullCsvImportService());

            await vm.LoadAsync();

            vm.AlertsCount.Should().Be(2);
            vm.LowStockCount.Should().Be(1);
            vm.ExpiringSoonCount.Should().Be(1);
            vm.ExpiredCount.Should().Be(0);
            vm.CriticalAlertsCount.Should().Be(0);
            vm.Alerts.Should().Contain(a => a.Name == "Flour" && a.Kind == AlertKind.LowStock);
            vm.Alerts.Should().Contain(a => a.Name == "Cheese" && a.Kind == AlertKind.ExpiringSoon);
        }
        finally
        {
            conn.Dispose();
        }
    }

    // ----- the sentence a problem card leads with -----

    [Theory]
    [InlineData(AlertKind.LowStock, "Rice is running low")]
    [InlineData(AlertKind.OutOfStock, "Rice is out of stock")]
    [InlineData(AlertKind.Expired, "Rice has expired")]
    [InlineData(AlertKind.ExpiringSoon, "Rice is going out of date")]
    public void Headline_NamesTheItemAndWhatHappenedToIt(AlertKind kind, string expected)
    {
        var alert = new InventoryAlert(1, "Rice", "Grains", "Dry store", kind, AlertSeverity.Warning, "Expires in 3 days", 3);

        alert.Headline.Should().Be(expected);
    }

    // ----- the one line a list can show about an item -----

    [Fact]
    public void Status_HealthyItem_SaysOKOutLoud()
    {
        var status = ItemStatusService.Evaluate(MakeItem("Rice", quantity: 10m), 5m, 7, Today);

        status.Text.Should().Be("OK");
        status.Tone.Should().Be(ItemStatusTone.Ok);
        status.NeedsAttention.Should().BeFalse();
    }

    [Fact]
    public void Status_RunningLow_Watches()
    {
        var status = ItemStatusService.Evaluate(MakeItem("Flour", quantity: 2m, unit: "kg"), 5m, 7, Today);

        status.Text.Should().Be("Low stock (2 kg)");
        status.Tone.Should().Be(ItemStatusTone.Watch);
        status.NeedsAttention.Should().BeTrue();
    }

    [Fact]
    public void Status_OutOfStock_IsUrgent()
    {
        var status = ItemStatusService.Evaluate(MakeItem("Rice", quantity: 0m), 5m, 7, Today);

        status.Tone.Should().Be(ItemStatusTone.Urgent);
        status.Text.Should().Be("Out of stock");
    }

    [Fact]
    public void Status_Expired_LeadsWithTheExpiryRatherThanTheStock()
    {
        // Both wrong, and one line has to choose: the expiry is the thing to act on.
        var status = ItemStatusService.Evaluate(MakeItem("Butter", quantity: 0m, expiry: Today.AddDays(-2)), 5m, 7, Today);

        status.Tone.Should().Be(ItemStatusTone.Urgent);
        status.Text.Should().Be("Expired 2 days ago");
    }

    [Fact]
    public void Status_WordsItExactlyAsTheProblemsTabDoes()
    {
        var item = MakeItem("Cheese", quantity: 1m, unit: "kg", expiry: Today.AddDays(3));

        var status = ItemStatusService.Evaluate(item, 5m, 7, Today);

        status.Text.Should().Be(InventoryAlertService.Evaluate(new[] { item }, 5m, 7, Today)[0].Message);
    }

    [Fact]
    public void Status_FromLooseValues_ReachesTheSameAnswerAsFromTheItem()
    {
        // What a grid cell holds: the values on screen, and no entity behind them.
        var status = ItemStatusService.Evaluate(2m, Today.AddDays(3), 5m, 7, Today, "kg");

        status.Text.Should().Be(ItemStatusService.Evaluate(MakeItem("Cheese", quantity: 2m, unit: "kg", expiry: Today.AddDays(3)), 5m, 7, Today).Text);
        status.Text.Should().Be("Expires in 3 days");
    }

    [Fact]
    public void Status_ThresholdsFromTheCallerDecideTheTone()
    {
        var watch = ItemStatusService.Evaluate(4m, null, 5m, 7, Today, "kg");
        var allWell = ItemStatusService.Evaluate(4m, null, 3m, 7, Today, "kg");

        watch.Tone.Should().Be(ItemStatusTone.Watch);
        allWell.Tone.Should().Be(ItemStatusTone.Ok);
    }

    [Fact]
    public void Status_NoItem_HasNothingWrongWithIt()
    {
        var status = ItemStatusService.Evaluate((Item?)null, 5m, 7, Today);

        status.Text.Should().Be(ItemStatusService.AllWell);
        status.NeedsAttention.Should().BeFalse();
    }
}
