using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using KitchenInventory.Data;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Domain.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

/// <summary>
/// Covers the safety net around the database: the integrity check, the consistent backup, and
/// the restore that always leaves a way back. The database is the only copy of a school's
/// inventory, so these are the paths that matter most when something goes wrong.
/// </summary>
public sealed class DatabaseMaintenanceTests : IDisposable
{
    private readonly string _root;
    private readonly string _backupsDir;
    private readonly string _dbPath;

    public DatabaseMaintenanceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kitchen-maintenance-tests", Guid.NewGuid().ToString("N"));
        _backupsDir = Path.Combine(_root, "backups");
        _dbPath = Path.Combine(_root, "kitchen.db");
        Directory.CreateDirectory(_backupsDir);
    }

    public void Dispose()
    {
        try { SqliteConnection.ClearAllPools(); } catch { /* best effort */ }
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private DbContextOptions<KitchenInventoryDbContext> Options()
        => new DbContextOptionsBuilder<KitchenInventoryDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

    private KitchenInventoryDbContext CreateDb()
    {
        var db = new KitchenInventoryDbContext(Options());
        db.Database.EnsureCreated();
        return db;
    }

    private DatabaseMaintenanceService CreateService()
        => new(new TestDbContextFactory(Options()), NullLogger<DatabaseMaintenanceService>.Instance, _backupsDir);

    private sealed class TestDbContextFactory : IDbContextFactory<KitchenInventoryDbContext>
    {
        private readonly DbContextOptions<KitchenInventoryDbContext> _options;

        public TestDbContextFactory(DbContextOptions<KitchenInventoryDbContext> options) => _options = options;

        public KitchenInventoryDbContext CreateDbContext() => new(_options);

        public ValueTask<KitchenInventoryDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => new(new KitchenInventoryDbContext(_options));
    }

    private async Task SeedAsync(
        string name,
        decimal quantity,
        params (MovementType Type, decimal Quantity)[] movements)
    {
        await using var db = CreateDb();
        var item = new Item { Name = name, Quantity = quantity, Unit = "kg" };
        db.Items.Add(item);
        await db.SaveChangesAsync();

        foreach (var (type, amount) in movements)
        {
            db.StockMovements.Add(new StockMovement
            {
                ItemId = item.Id,
                Type = type,
                Quantity = amount,
                TimestampUtc = DateTime.UtcNow
            });
        }
        await db.SaveChangesAsync();
    }

    // ---------------------------------------------------------------- integrity check

    [Fact]
    public async Task CheckAsync_ReportsHealthy_WhenStockMatchesItsHistory()
    {
        await SeedAsync("Rice", 7m, (MovementType.Add, 10m), (MovementType.Consume, 3m));
        using var db = CreateDb();

        var report = await DatabaseMaintenance.CheckAsync(db);

        report.IsHealthy.Should().BeTrue();
        report.Summary.Should().Contain("No problems");
        report.Findings.Should().OnlyContain(f => !f.IsProblem);
        report.Findings.Select(f => f.Area)
            .Should().Contain(new[] { "File structure", "Links between records", "Stock history" });
        report.FileSizeBytes.Should().BeGreaterThan(0);
        report.LastWrittenUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task CheckAsync_ReportsProblem_WhenStockDoesNotMatchItsHistory()
    {
        // 20 on hand with only 5 ever recorded: what a school would notice as "the numbers are wrong".
        await SeedAsync("Flour", 20m, (MovementType.Add, 5m));
        using var db = CreateDb();

        var report = await DatabaseMaintenance.CheckAsync(db);

        report.IsHealthy.Should().BeFalse();
        report.Summary.Should().Contain("1 problem");
        var problem = report.Findings.Should().ContainSingle(f => f.IsProblem).Subject;
        problem.Area.Should().Be("Stock history");
        problem.Message.Should().Contain("Flour");
    }

    [Fact]
    public async Task CheckAsync_CountsEveryProblem_InItsSummary()
    {
        await SeedAsync("Flour", 20m, (MovementType.Add, 5m));
        await SeedAsync("Sugar", 9m, (MovementType.Add, 2m));
        using var db = CreateDb();

        var report = await DatabaseMaintenance.CheckAsync(db);

        report.IsHealthy.Should().BeFalse();
        report.Summary.Should().Contain("2 problems");
        report.Findings.Count(f => f.IsProblem).Should().Be(2);
    }

    [Fact]
    public async Task CheckAsync_ThroughTheService_ReadsTheFileItReports()
    {
        await SeedAsync("Rice", 3m, (MovementType.Add, 3m));
        var service = CreateService();

        service.SupportsFileBackup.Should().BeTrue();
        service.DatabaseFilePath.Should().Be(_dbPath);

        var report = await service.CheckAsync();

        report.IsHealthy.Should().BeTrue();
        report.FileSizeBytes.Should().BeGreaterThan(0);
    }

    // ---------------------------------------------------------------- backup

    [Fact]
    public async Task BackupToAsync_WritesAReadableCopyWithTheSameData()
    {
        await SeedAsync("Rice", 4m, (MovementType.Add, 4m));
        var backupPath = Path.Combine(_backupsDir, "copy.db");

        using (var db = CreateDb())
        {
            await DatabaseMaintenance.BackupToAsync(db, backupPath);
        }

        File.Exists(backupPath).Should().BeTrue();
        using var copy = new KitchenInventoryDbContext(
            new DbContextOptionsBuilder<KitchenInventoryDbContext>()
                .UseSqlite($"Data Source={backupPath}")
                .Options);
        copy.Items.Select(i => i.Name).ToList().Should().Equal("Rice");
    }

    [Fact]
    public async Task BackupToAsync_ReplacesAnExistingFile()
    {
        // VACUUM INTO refuses a target that already exists, so a successful backup over one
        // proves the old file was removed first.
        await SeedAsync("Rice", 1m, (MovementType.Add, 1m));
        var backupPath = Path.Combine(_backupsDir, "copy.db");
        File.WriteAllText(backupPath, "this is not a database");

        using (var db = CreateDb())
        {
            await DatabaseMaintenance.BackupToAsync(db, backupPath);
        }

        File.ReadAllBytes(backupPath).Take(15).ToArray()
            .Should().Equal(System.Text.Encoding.ASCII.GetBytes("SQLite format 3"));
    }

    [Fact]
    public async Task BackupToAsync_RejectsAnEmptyDestination()
    {
        using var db = CreateDb();

        var act = async () => await DatabaseMaintenance.BackupToAsync(db, "   ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task BackupNowAsync_WritesAnAutomaticLookingManualCopy_TheUserCanFind()
    {
        await SeedAsync("Rice", 2m, (MovementType.Add, 2m));
        var service = CreateService();

        var path = await service.BackupNowAsync();

        Path.GetFileName(path).Should().StartWith(DatabaseMaintenanceService.ManualBackupPrefix);
        File.Exists(path).Should().BeTrue();
        service.ListBackups().Should().ContainSingle().Which.Path.Should().Be(path);
    }

    [Fact]
    public async Task BackupToFolderAsync_WritesIntoTheChosenFolder()
    {
        await SeedAsync("Rice", 3m, (MovementType.Add, 3m));
        var elsewhere = Path.Combine(_root, "usb-stick");
        var service = CreateService();

        var path = await service.BackupToFolderAsync(elsewhere);

        Path.GetDirectoryName(path).Should().Be(elsewhere);
        Path.GetFileName(path).Should().StartWith(DatabaseMaintenanceService.ManualBackupPrefix);
        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task BackupToFolderAsync_RejectsAnEmptyFolder()
    {
        await SeedAsync("Rice", 3m, (MovementType.Add, 3m));
        var service = CreateService();

        var act = async () => await service.BackupToFolderAsync("  ");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ---------------------------------------------------------------- startup backups

    [Fact]
    public async Task CreateStartupBackupAsync_WritesAnAutomaticBackup()
    {
        await SeedAsync("Rice", 2m, (MovementType.Add, 2m));
        var service = CreateService();

        var path = await service.CreateStartupBackupAsync();

        path.Should().NotBeNull();
        Path.GetFileName(path!).Should().StartWith(DatabaseMaintenanceService.StartupBackupPrefix);
        File.Exists(path!).Should().BeTrue();

        var backup = service.ListBackups().Should().ContainSingle().Subject;
        backup.IsAutomatic.Should().BeTrue();
        backup.IsSafetyCopy.Should().BeFalse();
    }

    [Fact]
    public async Task CreateStartupBackupAsync_KeepsOnlyTheFiveNewestAutomaticCopies()
    {
        await SeedAsync("Rice", 2m, (MovementType.Add, 2m));

        for (var day = 1; day <= 6; day++)
        {
            var old = Path.Combine(_backupsDir, $"{DatabaseMaintenanceService.StartupBackupPrefix}2020010{day}-000000.db");
            File.WriteAllText(old, "older than the one about to be written");
            File.SetLastWriteTimeUtc(old, new DateTime(2020, 1, day, 0, 0, 0, DateTimeKind.Utc));
        }

        // Nothing the user asked for, and nothing a restore left behind, may be pruned.
        var manual = Path.Combine(_backupsDir, $"{DatabaseMaintenanceService.ManualBackupPrefix}20200101-000000.db");
        var safety = Path.Combine(_backupsDir, $"{DatabaseMaintenanceService.SafetyCopyPrefix}20200101-000000.db");
        File.WriteAllText(manual, "manual");
        File.WriteAllText(safety, "safety");

        var service = CreateService();
        var newest = await service.CreateStartupBackupAsync();

        var automatic = service.ListBackups().Where(b => b.IsAutomatic).ToList();
        automatic.Should().HaveCount(DatabaseMaintenanceService.StartupBackupsToKeep);
        automatic[0].Path.Should().Be(newest);

        File.Exists(manual).Should().BeTrue();
        File.Exists(safety).Should().BeTrue();
    }

    // ---------------------------------------------------------------- the list

    [Fact]
    public void ListBackups_ReturnsNewestFirst_AndIgnoresForeignFiles()
    {
        var older = Path.Combine(_backupsDir, "kitchen-backup-20200101-000000.db");
        var newer = Path.Combine(_backupsDir, "kitchen-backup-20200102-000000.db");
        File.WriteAllText(older, "x");
        File.WriteAllText(newer, "x");
        File.SetLastWriteTimeUtc(older, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        File.WriteAllText(Path.Combine(_backupsDir, "read-me.txt"), "not a backup");

        var service = CreateService();

        service.ListBackups().Select(b => b.Path).Should().Equal(newer, older);
    }

    [Fact]
    public void ListBackups_IsEmpty_WhenTheFolderHasNoBackupsYet()
    {
        var service = CreateService();

        service.ListBackups().Should().BeEmpty();
    }

    [Fact]
    public void BackupFileInfo_DescribesASafetyCopyAndItsSize()
    {
        var info = new BackupFileInfo(
            @"C:\backups\before-restore-20260102-030405.db",
            "before-restore-20260102-030405.db",
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            2 * 1024 * 1024);

        info.IsSafetyCopy.Should().BeTrue();
        info.IsAutomatic.Should().BeFalse();
        info.Description.Should().Contain("taken before a restore");
        info.Description.Should().Contain("2 MB");
    }

    [Fact]
    public void BackupFileInfo_DescribesAnAutomaticCopyInKilobytes()
    {
        var info = new BackupFileInfo(
            @"C:\backups\auto-kitchen-20260102-030405.db",
            "auto-kitchen-20260102-030405.db",
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            512 * 1024);

        info.IsAutomatic.Should().BeTrue();
        info.IsSafetyCopy.Should().BeFalse();
        info.Description.Should().Contain("(automatic)");
        info.Description.Should().Contain("512 KB");
    }

    // ---------------------------------------------------------------- restore

    [Fact]
    public async Task RestoreAsync_PutsTheOlderDataBack_AndKeepsWhatWasThereBefore()
    {
        await SeedAsync("Rice", 1m, (MovementType.Add, 1m));
        var service = CreateService();
        var backupPath = await service.BackupNowAsync();

        // Time passes and the kitchen adds something the older backup does not know about.
        using (var db = CreateDb())
        {
            db.Items.Add(new Item { Name = "Beans", Quantity = 1m, Unit = "kg" });
            db.SaveChanges();
        }

        // Sidecars left over from the replaced file must not survive the restore. Pools are
        // cleared first so the live file (and its sidecars) can actually be touched.
        SqliteConnection.ClearAllPools();
        File.WriteAllText(_dbPath + "-wal", "stale write-ahead log");
        File.WriteAllText(_dbPath + "-shm", "stale shared memory");
        File.Exists(_dbPath + "-wal").Should().BeTrue();

        var result = await service.RestoreAsync(backupPath);

        File.Exists(_dbPath + "-wal").Should().BeFalse();
        File.Exists(_dbPath + "-shm").Should().BeFalse();

        Path.GetFileName(result.SafetyCopyPath).Should().StartWith(DatabaseMaintenanceService.SafetyCopyPrefix);
        File.Exists(result.SafetyCopyPath).Should().BeTrue();

        using (var restored = CreateDb())
        {
            restored.Items.Select(i => i.Name).ToList().Should().Equal("Rice");
        }

        using (var safety = new KitchenInventoryDbContext(
            new DbContextOptionsBuilder<KitchenInventoryDbContext>()
                .UseSqlite($"Data Source={result.SafetyCopyPath}")
                .Options))
        {
            safety.Items.Select(i => i.Name).OrderBy(n => n).ToList().Should().Equal("Beans", "Rice");
        }
    }

    [Fact]
    public async Task RestoreAsync_RejectsAMissingBackupFile()
    {
        await SeedAsync("Rice", 1m, (MovementType.Add, 1m));
        var service = CreateService();

        var act = async () => await service.RestoreAsync(Path.Combine(_backupsDir, "not-here.db"));

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    // ---------------------------------------------------------------- file-less data

    [Fact]
    public async Task BackupNowAsync_IsUnsupported_WhenTheDataIsNotInAFile()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<KitchenInventoryDbContext>()
            .UseSqlite(connection)
            .Options;
        var service = new DatabaseMaintenanceService(
            new TestDbContextFactory(options), NullLogger<DatabaseMaintenanceService>.Instance, _backupsDir);

        service.SupportsFileBackup.Should().BeFalse();
        service.DatabaseFilePath.Should().BeNull();

        var act = async () => await service.BackupNowAsync();
        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
