using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KitchenInventory.Data;

namespace KitchenInventory.Desktop.Services;

/// <summary>One database copy sitting in the backups folder.</summary>
public sealed record BackupFileInfo(
    string Path,
    string FileName,
    DateTime CreatedUtc,
    long SizeBytes)
{
    /// <summary>Backups the app takes by itself on startup. These are the ones that get pruned.</summary>
    public bool IsAutomatic => FileName.StartsWith(DatabaseMaintenanceService.StartupBackupPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The copy taken just before a restore, so a restore can itself be undone.</summary>
    public bool IsSafetyCopy => FileName.StartsWith(DatabaseMaintenanceService.SafetyCopyPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Friendly description for the backup list.</summary>
    public string Description
    {
        get
        {
            var when = CreatedUtc.ToLocalTime().ToString("d MMM yyyy 'at' HH:mm");
            var size = SizeBytes >= 1024 * 1024
                ? $"{SizeBytes / (1024.0 * 1024.0):0.#} MB"
                : $"{Math.Max(1, SizeBytes / 1024)} KB";
            var kind = IsSafetyCopy ? " (taken before a restore)" : IsAutomatic ? " (automatic)" : string.Empty;
            return $"{when}{kind}  ·  {size}";
        }
    }
}

/// <summary>The safety copy a restore left behind, so the previous data can be recovered.</summary>
public sealed record RestoreResult(string SafetyCopyPath);

/// <summary>
/// Backup, restore and integrity checking for the application database. This exists because the
/// database is the only copy of a school's inventory: there must be a recent copy somewhere, and
/// there must be a way to tell whether the file is still sound.
/// </summary>
public interface IDatabaseMaintenanceService
{
    /// <summary>Where automatic and manual backups are kept.</summary>
    string BackupsDirectory { get; }

    /// <summary>The live database file, or null when the provider is not file-based (or in-memory).</summary>
    string? DatabaseFilePath { get; }

    /// <summary>False for providers where backing up the file is meaningless (for example Npgsql).</summary>
    bool SupportsFileBackup { get; }

    /// <summary>Runs the structural, referential and stock-ledger checks.</summary>
    Task<DatabaseIntegrityReport> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Writes a backup into the backups folder and returns its full path.</summary>
    Task<string> BackupNowAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a backup into <paramref name="folder"/> (a USB stick, for instance) and returns its
    /// full path. The file name is chosen here so every backup looks the same.
    /// </summary>
    Task<string> BackupToFolderAsync(string folder, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes the routine backup for this launch and prunes older automatic ones. Best effort:
    /// a failure here must never stop the app from starting.
    /// </summary>
    Task<string?> CreateStartupBackupAsync(CancellationToken cancellationToken = default);

    /// <summary>Every backup in the backups folder, newest first.</summary>
    IReadOnlyList<BackupFileInfo> ListBackups();

    /// <summary>
    /// Replaces the live database with <paramref name="backupPath"/>, first taking a safety copy
    /// of what is there now. The caller must restart the app afterwards: the file underneath it
    /// has changed, and no open connection can be trusted to describe it any more.
    /// </summary>
    Task<RestoreResult> RestoreAsync(string backupPath, CancellationToken cancellationToken = default);
}
