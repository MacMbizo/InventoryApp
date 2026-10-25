using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using KitchenInventory.Desktop.Logging;
using Serilog;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

/// <summary>
/// The log folder sits on a school machine that nobody tends, so growth has to be bounded by
/// the sink itself. These tests drive the real Serilog file sink through
/// <see cref="LogRetention.WithLogRetention"/> with small limits so the pruning behaviour is
/// observable without writing hundreds of megabytes.
/// </summary>
public class LogRetentionTests : IDisposable
{
    private readonly string _root;

    public LogRetentionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "kitchen-log-retention-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private string[] LogFiles() =>
        Directory.GetFiles(_root, "log-*.txt").OrderBy(f => f, StringComparer.Ordinal).ToArray();

    [Fact]
    public void WithLogRetention_RollsBySize_AndKeepsOnlyTheNewestFiles()
    {
        const int retained = 4;
        const long sizeLimit = 1024;
        var logPath = Path.Combine(_root, "log-.txt");

        using (var logger = new LoggerConfiguration()
                   .WithLogRetention(logPath, retainedFileCountLimit: retained, fileSizeLimitBytes: sizeLimit)
                   .CreateLogger())
        {
            for (var i = 0; i < 500; i++)
            {
                logger.Information("event {Index} {Payload}", i, new string('x', 100));
            }
        }

        var files = LogFiles();

        // Writing far more than one file can hold must have rolled more than once…
        files.Should().HaveCount(retained, "the oldest files are deleted as new ones appear");

        // …and the newest file must hold the most recent event.
        var newest = new FileInfo(files.Last());
        newest.Exists.Should().BeTrue();
        File.ReadAllText(newest.FullName).Should().Contain("event 499");

        // The whole folder stays inside the policy, which is the point of the gate.
        var totalBytes = files.Sum(f => new FileInfo(f).Length);
        totalBytes.Should().BeLessThanOrEqualTo(retained * sizeLimit + sizeLimit);
    }

    [Fact]
    public void WithLogRetention_WritesTheAppLogNameTheSupportBundleLooksFor()
    {
        var logPath = Path.Combine(_root, "log-.txt");

        using (var logger = new LoggerConfiguration()
                   .WithLogRetention(logPath, retainedFileCountLimit: 5, fileSizeLimitBytes: 1024 * 1024)
                   .CreateLogger())
        {
            logger.Information("Kitchen inventory started");
        }

        // The support bundle collects "log-*.txt" from the logs folder; the retained sink must
        // keep producing exactly that shape, or a support case would arrive with no logs in it.
        var files = LogFiles();
        files.Should().ContainSingle();
        Path.GetFileName(files[0]).Should().Match("log-????????.txt");
        File.ReadAllText(files[0]).Should().Contain("Kitchen inventory started");
    }

    [Fact]
    public void WithLogRetention_RejectsABlankPath()
    {
        var configuration = new LoggerConfiguration();

        var act = () => configuration.WithLogRetention("   ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DefaultPolicy_IsBoundedToAMonthOfLogs()
    {
        // Documented policy: the app keeps at most 30 files of at most 10 MB, so the log folder
        // can never quietly fill a school's disk no matter how long the app is left running.
        LogRetention.RetainedFileCountLimit.Should().Be(30);
        LogRetention.FileSizeLimitBytes.Should().Be(10L * 1024 * 1024);
    }
}
