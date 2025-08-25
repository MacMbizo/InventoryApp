using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace KitchenInventory.Desktop.Services;

public class DatabaseInfoService : IDatabaseInfoService
{
    private readonly IDbContextFactory<KitchenInventory.Data.KitchenInventoryDbContext> _factory;

    public DatabaseInfoService(IDbContextFactory<KitchenInventory.Data.KitchenInventoryDbContext> factory)
    {
        _factory = factory;
    }

    public async Task<DatabaseInfo> GetInfoAsync(CancellationToken ct = default)
    {
        using var ctx = await _factory.CreateDbContextAsync(ct);
        var providerName = ctx.Database.ProviderName ?? string.Empty;
        var provider = MapProvider(providerName);
        var connection = ctx.Database.GetDbConnection();
        var cs = connection.ConnectionString ?? string.Empty;
        var providerVersion = ResolveProviderVersion(providerName);

        int applied = 0;
        int pending = 0;
        DatabaseHealth health;

        try
        {
            var canConnect = await ctx.Database.CanConnectAsync(ct);
            if (canConnect)
            {
                applied = (await ctx.Database.GetAppliedMigrationsAsync(ct)).Count();
                pending = (await ctx.Database.GetPendingMigrationsAsync(ct)).Count();
                health = new("OK", "Connected");
            }
            else
            {
                health = new("Error", "Cannot connect");
            }
        }
        catch (Exception ex)
        {
            health = new("Error", $"{ex.GetType().Name}: {ex.Message}");
        }

        string target = GetTarget(provider, cs);
        string logsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InventoryApp", "logs");
        string? dbPath = provider == DatabaseProvider.Sqlite ? ExtractSqlitePath(cs) : null;
        var redacted = RedactConnectionString(cs);

        return new DatabaseInfo(provider, target, providerVersion, applied, pending, health, redacted, logsDir, dbPath);
    }

    private static DatabaseProvider MapProvider(string providerName)
    {
        if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase)) return DatabaseProvider.Sqlite;
        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)) return DatabaseProvider.Npgsql;
        return DatabaseProvider.Unknown;
    }

    private static string GetTarget(DatabaseProvider provider, string cs)
    {
        if (provider == DatabaseProvider.Sqlite)
        {
            return ExtractSqlitePath(cs) ?? "(in-memory or unknown)";
        }
        if (provider == DatabaseProvider.Npgsql)
        {
            var host = ExtractValue(cs, "Host") ?? "localhost";
            var port = ExtractValue(cs, "Port") ?? "5432";
            var db = ExtractValue(cs, "Database") ?? "(unknown)";
            return $"{host}:{port}/{db}";
        }
        return "(unknown)";
    }

    private static string? ExtractSqlitePath(string cs)
    {
        var m = Regex.Match(cs, @"(?i)(Data Source|DataSource)\s*=\s*([^;]+)");
        return m.Success ? m.Groups[2].Value.Trim() : null;
    }

    private static string? ExtractValue(string cs, string key)
    {
        var m = Regex.Match(cs, $@"(?i){Regex.Escape(key)}\s*=\s*([^;]+)");
        return m.Success ? m.Groups[1].Value.Trim() : null;
    }

    private static string ResolveProviderVersion(string providerName)
    {
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, providerName, StringComparison.OrdinalIgnoreCase));
            if (asm != null) return asm.GetName().Version?.ToString() ?? "unknown";
        }
        catch { }

        var fallback = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a =>
                (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) && a.GetName().Name!.Contains("EntityFrameworkCore.Sqlite", StringComparison.OrdinalIgnoreCase)) ||
                (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) && a.GetName().Name!.Contains("Npgsql.EntityFrameworkCore.PostgreSQL", StringComparison.OrdinalIgnoreCase))
            );
        return fallback?.GetName().Version?.ToString() ?? "unknown";
    }

    public string RedactConnectionString(string cs)
    {
        if (string.IsNullOrWhiteSpace(cs)) return cs;
        // Mask sensitive keys; keep format intact
        var keys = new[] { "Password", "Pwd", "Username", "User ID", "UserId" };
        foreach (var k in keys)
        {
            cs = Regex.Replace(cs, $@"(?i)\b{Regex.Escape(k)}\s*=\s*[^;]*", $"{k}=***");
        }
        return cs;
    }
}