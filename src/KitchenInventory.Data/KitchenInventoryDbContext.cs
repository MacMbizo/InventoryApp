using KitchenInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KitchenInventory.Data;

public class KitchenInventoryDbContext : DbContext
{
    public KitchenInventoryDbContext(DbContextOptions<KitchenInventoryDbContext> options) : base(options) { }

    public DbSet<Item> Items => Set<Item>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Item>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(200);
            b.Property(x => x.Unit).IsRequired().HasMaxLength(32);
            b.Property(x => x.Quantity).HasPrecision(18, 3);
        });
    }
}