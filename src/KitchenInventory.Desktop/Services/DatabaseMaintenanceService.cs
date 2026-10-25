using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KitchenInventory.Data;
using KitchenInventory.Desktop.Utilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace KitchenInventory.Desktop.Services;

/// <inheritdoc cref="IDatabaseMaintenanceService"/>
public sealed class DatabaseMaintenanceService : IDatabaseMaintenanceService
{
    /// <summary>Automatic backups are named like this, and are the ones that get pruned.</summary>
    public const string StartupBackupPrefix = "auto-kitchen-";

    /// <summary>Backups the user asked for. Kept until the user deletes them.</summary>
    public const string ManualBackupPrefix = "kitchen-backup-";

    /// <summary>Copies taken immediately before a restore.</summary>
    public const string SafetyCopyPrefix = "before-restore-";

    /// <summary>How many automatic startup backups to keep.</summary>
    public const int StartupBackupsToKeep = 5;

    private readonly IDbContextFactory<KitchenInventoryDbContext> _factory;
    private readonly ILogger<DatabaseMaintenanceService> _logger;
    private readonly string _backupsDirectory;

    public DatabaseMaintenanceService(
        IDbContextFactory<KitchenInventoryDbContext> factory,
        ILogger<DatabaseMaintenanceService> logger,
        string? backupsDirectory = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        // The directory is injectable so tests never touch the real profile.
        _backupsDirectory = backupsDirectory ?? AppPaths.GetBackupsDirectory();
    }

    public string BackupsDirectory => _backupsDirectory;

    public string? DatabaseFilePath
    {
        get
        {
            var dataSource = ResolveDataSource();
            return string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:"
                ? null
                : dataSource;
        }
    }

    public bool SupportsFileBackup => DatabaseFilePath != null;

    public Task<DatabaseIntegrityReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        ValidateFileBackupSupported();
        return RunAsync(db => DatabaseMaintenance.CheckAsync(db, cancellationToken));
    }

    public async Task<string> BackupNowAsync(CancellationToken cancellationToken = default)
    {
        ValidateFileBackupSupported();
        var destination = Path.Combine(_backupsDirectory, $"{ManualBackupPrefix}{Timestamp()}.db");
        return await WriteBackupAsync(destination, cancellationToken);
    }

    public async Task<string> BackupToFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        ValidateFileBackupSupported();
        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new ArgumentException("Choose a folder for the backup.", nameof(folder));
        }

        var destination = Path.Combine(folder, $"{ManualBackupPrefix}{Timestamp()}.db");
        return await WriteBackupAsync(destination, cancellationToken);
    }

    private async Task<string> WriteBackupAsync(string destinationPath, CancellationToken cancellationToken)
    {
        await RunAsync(async db => await DatabaseMaintenance.BackupToAsync(db, destinationPath, cancellationToken));
        _logger.LogInformation("Database backed up to {Path}", destinationPath);
        return Path.GetFullPath(destinationPath);
    }

    public async Task<string?> CreateStartupBackupAsync(CancellationToken cancellationToken = default)
    {
        if (!SupportsFileBackup)
        {
            return null;
        }

        // A backup that fails must not stop the app from starting, so everything here is caught.
        try
        {
            var destination = Path.Combine(_backupsDirectory, $"{StartupBackupPrefix}{Timestamp()}.db");
            var path = await WriteBackupAsync(destination, cancellationToken);
            PruneAutomaticBackups();
            return path;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The automatic startup backup could not be written; continuing without it");
            return null;
        }
    }

    public IReadOnlyList<BackupFileInfo> ListBackups()
    {
        if (!Directory.Exists(_backupsDirectory))
        {
            return Array.Empty<BackupFileInfo>();
        }

        try
        {
            return new DirectoryInfo(_backupsDirectory)
                .GetFiles("*.db")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => new BackupFileInfo(f.FullName, f.Name, f.LastWriteTimeUtc, f.Length))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the backups folder at {Directory}", _backupsDirectory);
            return Array.Empty<BackupFileInfo>();
        }
    }

    public async Task<RestoreResult> RestoreAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        ValidateFileBackupSupported();
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            throw new FileNotFoundException("That backup file could not be found.", backupPath);
        }

        var livePath = DatabaseFilePath!;
        var safetyCopy = Path.Combine(_backupsDirectory, $"{SafetyCopyPrefix}{Timestamp()}.db");

        // Keep what is there now before anything is replaced, so a restore can itself be undone.
        await RunAsync(async db => await DatabaseMaintenance.BackupToAsync(db, safetyCopy, cancellationToken));
        _logger.LogInformation("Safety copy of the current database written to {Path}", safetyCopy);

        // SQLite keeps pooled connections to the file; they must go before it can be replaced.
        SqliteConnection.ClearAllPools();

        File.Copy(backupPath, livePath, overwrite: true);
        DeleteSidecarFiles(livePath);

        _logger.LogInformation("Database restored from {Backup} over {Live}", backupPath, livePath);
        return new RestoreResult(safetyCopy);
    }

    /// <summary>Deletes the <c>-wal</c> and <c>-shm</c> files belonging to <paramref name="databasePath"/>.</summary>
    /// <remarks>
    /// Stale sidecars describe the file that was just replaced. Left in place, SQLite would try to
    /// replay a write-ahead log that belongs to different data.
    /// </remarks>
    internal static void DeleteSidecarFiles(string databasePath)
    {
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            try
            {
                var sidecar = databasePath + suffix;
                if (File.Exists(sidecar))
                {
                    File.Delete(sidecar);
                }
            }
            catch
            {
                // Best effort: an undeletable sidecar is not worth failing the restore for.
            }
        }
    }

    /// <summary>Keeps the newest <see cref="StartupBackupsToKeep"/> automatic backups.</summary>
    internal int PruneAutomaticBackups()
    {
        var removed = 0;
        try
        {
            var automatic = new DirectoryInfo(_backupsDirectory)
                .GetFiles($"{StartupBackupPrefix}*.db")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(StartupBackupsToKeep)
                .ToList();

            foreach (var file in automatic)
            {
                try
                {
                    file.Delete();
                    removed++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not remove the old automatic backup {Name}", file.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not tidy up the backups folder at {Directory}", _backupsDirectory);
        }

        return removed;
    }

    private void ValidateFileBackupSupported()
    {
        if (!SupportsFileBackup)
        {
            throw new NotSupportedException(
                "This build stores its data outside a file, so file backups are not available.");
        }
    }

    private string? ResolveDataSource()
    {
        try
        {
            using var db = _factory.CreateDbContext();
            if (!db.Database.IsSqlite())
            {
                return null;
            }
            return db.Database.GetDbConnection().DataSource;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not determine the database file path");
            return null;
        }
    }

    private async Task<T> RunAsync<T>(Func<KitchenInventoryDbContext, Task<T>> operation)
    {
        await using var db = await _factory.CreateDbContextAsync();
        return await operation(db);
    }

    private async Task RunAsync(Func<KitchenInventoryDbContext, Task> operation)
    {
        await using var db = await _factory.CreateDbContextAsync();
        await operation(db);
    }

    private static string Timestamp() => DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
}
