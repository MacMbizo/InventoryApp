using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql.EntityFrameworkCore.PostgreSQL;

namespace KitchenInventory.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<KitchenInventoryDbContext>
{
    public KitchenInventoryDbContext CreateDbContext(string[] args)
    {
        // Prefer explicit design-time env vars; otherwise mirror App.xaml.cs SQLite default
        var provider = Environment.GetEnvironmentVariable("INVENTORY_DB_PROVIDER") ?? "Sqlite";
        var explicitCs = Environment.GetEnvironmentVariable("INVENTORY_DB_CONNECTION");

        DbContextOptionsBuilder<KitchenInventoryDbContext> optionsBuilder = new();

        if (string.Equals(provider, "Npgsql", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(explicitCs))
        {
            optionsBuilder.UseNpgsql(explicitCs);
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var baseDir = Path.Combine(appData, "InventoryApp");
            Directory.CreateDirectory(baseDir);
            var dbPath = Path.Combine(baseDir, "kitchen.db");
            var connectionString = $"Data Source={dbPath}";
            optionsBuilder.UseSqlite(connectionString);
        }

        return new KitchenInventoryDbContext(optionsBuilder.Options);
    }
}