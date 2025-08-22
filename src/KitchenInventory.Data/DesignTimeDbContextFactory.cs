using System;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace KitchenInventory.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<KitchenInventoryDbContext>
{
    public KitchenInventoryDbContext CreateDbContext(string[] args)
    {
        // Mirror App.xaml.cs default resolution: %LocalAppData%/InventoryApp/kitchen.db
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var baseDir = Path.Combine(appData, "InventoryApp");
        Directory.CreateDirectory(baseDir);
        var dbPath = Path.Combine(baseDir, "kitchen.db");
        var connectionString = $"Data Source={dbPath}";

        var optionsBuilder = new DbContextOptionsBuilder<KitchenInventoryDbContext>()
            .UseSqlite(connectionString);

        return new KitchenInventoryDbContext(optionsBuilder.Options);
    }
}