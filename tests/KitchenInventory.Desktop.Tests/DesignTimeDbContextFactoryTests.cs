using System;
using KitchenInventory.Data;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public class DesignTimeDbContextFactoryTests
{
    private static IDisposable WithEnv(string? provider, string? connection, out Action restore)
    {
        var originalProvider = Environment.GetEnvironmentVariable("INVENTORY_DB_PROVIDER");
        var originalConnection = Environment.GetEnvironmentVariable("INVENTORY_DB_CONNECTION");
        Environment.SetEnvironmentVariable("INVENTORY_DB_PROVIDER", provider);
        Environment.SetEnvironmentVariable("INVENTORY_DB_CONNECTION", connection);
        restore = () =>
        {
            Environment.SetEnvironmentVariable("INVENTORY_DB_PROVIDER", originalProvider);
            Environment.SetEnvironmentVariable("INVENTORY_DB_CONNECTION", originalConnection);
        };
        return new NoopDisposable();
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }

    [Fact]
    public void CreateDbContext_NoProviderConfigured_DefaultsToSqlite()
    {
        using var _ = WithEnv(null, null, out var restore);
        try
        {
            var factory = new DesignTimeDbContextFactory();
            using var ctx = factory.CreateDbContext(Array.Empty<string>());
            Assert.Contains("Sqlite", ctx.Database.ProviderName, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            restore();
        }
    }

    [Fact]
    public void CreateDbContext_NpgsqlWithConnection_UsesNpgsql()
    {
        using var _ = WithEnv("Npgsql", "Host=localhost;Database=kitchen;Username=u;Password=p", out var restore);
        try
        {
            var factory = new DesignTimeDbContextFactory();
            using var ctx = factory.CreateDbContext(Array.Empty<string>());
            Assert.Contains("Npgsql", ctx.Database.ProviderName, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            restore();
        }
    }

    [Fact]
    public void CreateDbContext_NpgsqlWithoutConnection_FallsBackToSqlite()
    {
        using var _ = WithEnv("Npgsql", null, out var restore);
        try
        {
            var factory = new DesignTimeDbContextFactory();
            using var ctx = factory.CreateDbContext(Array.Empty<string>());
            Assert.Contains("Sqlite", ctx.Database.ProviderName, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            restore();
        }
    }

    [Fact]
    public void CreateDbContext_ExplicitSqliteProvider_UsesSqlite()
    {
        using var _ = WithEnv("Sqlite", "Data Source=ignored.db", out var restore);
        try
        {
            var factory = new DesignTimeDbContextFactory();
            using var ctx = factory.CreateDbContext(Array.Empty<string>());
            Assert.Contains("Sqlite", ctx.Database.ProviderName, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            restore();
        }
    }
}
