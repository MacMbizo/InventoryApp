using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KitchenInventory.Desktop.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public class DiagnosticsExporterTests
{
    private sealed class FakeDatabaseInfoService : IDatabaseInfoService
    {
        public DatabaseInfo Info { get; set; } = new(
            DatabaseProvider.Sqlite,
            "target",
            "1.0.0",
            3,
            0,
            new DatabaseHealth("OK", "Connected"),
            "Data Source=***",
            Path.GetTempPath(),
            null);

        public Exception? ThrowOnGet { get; set; }

        public Task<DatabaseInfo> GetInfoAsync(CancellationToken ct = default)
        {
            if (ThrowOnGet != null) throw ThrowOnGet;
            return Task.FromResult(Info);
        }

        public string RedactConnectionString(string connectionString) => connectionString;
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "KitchenInventory";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static IConfiguration EmptyConfiguration() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();

    [Fact]
    public async Task ExportAsync_BlankPath_Throws()
    {
        var exporter = new DiagnosticsExporter(new FakeDatabaseInfoService(), new FakeHostEnvironment(), EmptyConfiguration());
        await Assert.ThrowsAsync<ArgumentException>(() => exporter.ExportAsync("   "));
    }

    [Fact]
    public async Task ExportAsync_WritesEnvironmentAndDatabaseEntries()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "inv-diag-test-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(outDir, "bundle.zip");
        try
        {
            var exporter = new DiagnosticsExporter(new FakeDatabaseInfoService(), new FakeHostEnvironment(), EmptyConfiguration());

            await exporter.ExportAsync(zipPath);

            Assert.True(File.Exists(zipPath));
            using var zip = ZipFile.OpenRead(zipPath);
            var names = zip.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("environment.json", names);
            Assert.Contains("database.json", names);

            var dbEntry = zip.GetEntry("database.json")!;
            using var reader = new StreamReader(dbEntry.Open());
            var dbJson = reader.ReadToEnd();
            Assert.Contains("target", dbJson);
            Assert.Contains("AppliedMigrations", dbJson);
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExportAsync_IncludesLogsAndSqliteDatabaseFiles()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "inv-diag-test-" + Guid.NewGuid().ToString("N"));
        var logsDir = Path.Combine(outDir, "logs-src");
        var zipPath = Path.Combine(outDir, "bundle.zip");
        Directory.CreateDirectory(logsDir);
        File.WriteAllText(Path.Combine(logsDir, "log-20260101.txt"), "hello log");

        var dbFile = Path.Combine(outDir, "kitchen.db");
        File.WriteAllText(dbFile, "sqlite");
        File.WriteAllText(dbFile + "-shm", "shm");
        File.WriteAllText(dbFile + "-wal", "wal");

        try
        {
            var fake = new FakeDatabaseInfoService
            {
                Info = new DatabaseInfo(
                    DatabaseProvider.Sqlite,
                    dbFile,
                    "1.0.0",
                    1,
                    0,
                    new DatabaseHealth("OK", "Connected"),
                    "redacted",
                    logsDir,
                    dbFile)
            };
            var exporter = new DiagnosticsExporter(fake, new FakeHostEnvironment(), EmptyConfiguration());

            await exporter.ExportAsync(zipPath);

            using var zip = ZipFile.OpenRead(zipPath);
            var names = zip.Entries.Select(e => e.FullName).ToList();
            Assert.Contains("logs/log-20260101.txt", names);
            Assert.Contains("db/kitchen.db", names);
            Assert.Contains("db/kitchen.db-shm", names);
            Assert.Contains("db/kitchen.db-wal", names);
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExportAsync_DatabaseInfoFailure_PropagatesAndLeavesNoZip()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "inv-diag-test-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(outDir, "bundle.zip");
        try
        {
            var fake = new FakeDatabaseInfoService { ThrowOnGet = new InvalidOperationException("boom") };
            var exporter = new DiagnosticsExporter(fake, new FakeHostEnvironment(), EmptyConfiguration());

            await Assert.ThrowsAsync<InvalidOperationException>(() => exporter.ExportAsync(zipPath));

            Assert.False(File.Exists(zipPath));
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }

    [Fact]
    public async Task ExportAsync_CancelledToken_Throws()
    {
        var outDir = Path.Combine(Path.GetTempPath(), "inv-diag-test-" + Guid.NewGuid().ToString("N"));
        var zipPath = Path.Combine(outDir, "bundle.zip");
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            var exporter = new DiagnosticsExporter(new FakeDatabaseInfoService(), new FakeHostEnvironment(), EmptyConfiguration());

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporter.ExportAsync(zipPath, cts.Token));
        }
        finally
        {
            if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
        }
    }
}
