using System;
using System.Reflection;

namespace KitchenInventory.Desktop.Utilities;

/// <summary>
/// Provides centralized access to application version and commit information.
/// </summary>
public static class VersionInfo
{
    private static readonly Assembly _assembly = Assembly.GetExecutingAssembly();
    
    /// <summary>
    /// Gets the semantic version of the application (e.g., "1.2.3").
    /// </summary>
    public static string AppVersion
    {
        get
        {
            var version = _assembly.GetName().Version;
            return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
        }
    }
    
    /// <summary>
    /// Gets the full informational version string from assembly metadata.
    /// </summary>
    public static string InformationalVersion
    {
        get
        {
            var attr = _assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            return attr?.InformationalVersion ?? AppVersion;
        }
    }
    
    /// <summary>
    /// Gets the full commit SHA from the INVENTORY_COMMIT_SHA environment variable.
    /// Returns "unknown" if not available.
    /// </summary>
    public static string Commit
    {
        get
        {
            var commit = Environment.GetEnvironmentVariable("INVENTORY_COMMIT_SHA");
            return !string.IsNullOrWhiteSpace(commit) ? commit : "unknown";
        }
    }
    
    /// <summary>
    /// Gets the first 8 characters of the commit SHA for compact display.
    /// Returns "unknown" if the full commit is not available.
    /// </summary>
    public static string ShortCommit
    {
        get
        {
            var fullCommit = Commit;
            return fullCommit != "unknown" && fullCommit.Length >= 8 
                ? fullCommit.Substring(0, 8) 
                : "unknown";
        }
    }
    
    /// <summary>
    /// Generates a default diagnostics export filename with version and commit info.
    /// Format: diagnostics-{version}-{shortCommit}-{timestamp}.zip
    /// </summary>
    public static string GetDefaultDiagnosticsFileName()
    {
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var shortCommit = ShortCommit != "unknown" ? ShortCommit : "dev";
        return $"diagnostics-{AppVersion}-{shortCommit}-{timestamp}.zip";
    }
}