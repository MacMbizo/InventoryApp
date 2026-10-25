using System;
using System.IO;

namespace KitchenInventory.Desktop.Utilities;

/// <summary>
/// Resolves the application's writable data directory.
/// Defaults to <c>%LOCALAPPDATA%\InventoryApp</c>. Automated runs may redirect it
/// via the <c>INVENTORY_DATA_DIR</c> environment variable so that all app state
/// (logs, preferences, default database) stays inside a sandbox-friendly location
/// instead of the developer's profile.
/// </summary>
public static class AppPaths
{
    public const string DataDirEnvironmentVariable = "INVENTORY_DATA_DIR";

    public static string GetDataDirectory()
    {
        var overrideDir = Environment.GetEnvironmentVariable(DataDirEnvironmentVariable);
        var dir = !string.IsNullOrWhiteSpace(overrideDir)
            ? overrideDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InventoryApp");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetLogsDirectory()
    {
        var dir = Path.Combine(GetDataDirectory(), "logs");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// Where database backups are kept. The database is the only copy of the school's
    /// inventory, so the folder sits beside it under the same redirectable data directory.
    /// </summary>
    public static string GetBackupsDirectory()
    {
        var dir = Path.Combine(GetDataDirectory(), "backups");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
