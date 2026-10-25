using System;
using Serilog;

namespace KitchenInventory.Desktop.Logging;

/// <summary>
/// The single place that decides how much log history the app keeps. A school machine runs
/// unattended, so the log folder must not be allowed to grow without limit: each file is
/// capped and only the newest <see cref="RetainedFileCountLimit"/> files stay on disk, which
/// bounds the whole folder at roughly
/// <see cref="RetainedFileCountLimit"/> × <see cref="FileSizeLimitBytes"/> (300 MB by default).
/// </summary>
public static class LogRetention
{
    /// <summary>How many log files stay on disk. One file per day, plus any size rollovers.</summary>
    public const int RetainedFileCountLimit = 30;

    /// <summary>How large one log file may grow before it rolls over (10 MB).</summary>
    public const long FileSizeLimitBytes = 10L * 1024 * 1024;

    /// <summary>
    /// Writes to <paramref name="logFilePath"/> with the retention policy applied: daily files,
    /// rolled early if one grows past the size limit, with the oldest deleted as new ones appear.
    /// The limits are parameters only so the policy can be exercised in tests without writing
    /// hundreds of megabytes; the app always uses the defaults above.
    /// </summary>
    public static LoggerConfiguration WithLogRetention(
        this LoggerConfiguration configuration,
        string logFilePath,
        int retainedFileCountLimit = RetainedFileCountLimit,
        long fileSizeLimitBytes = FileSizeLimitBytes)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);

        return configuration.WriteTo.File(
            logFilePath,
            rollingInterval: RollingInterval.Day,
            rollOnFileSizeLimit: true,
            fileSizeLimitBytes: fileSizeLimitBytes,
            retainedFileCountLimit: retainedFileCountLimit,
            shared: false);
    }
}
