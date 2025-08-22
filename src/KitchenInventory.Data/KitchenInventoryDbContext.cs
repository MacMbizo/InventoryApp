using KitchenInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KitchenInventory.Data;

public class KitchenInventoryDbContext : DbContext
{
    public KitchenInventoryDbContext(DbContextOptions<KitchenInventoryDbContext> options) : base(options) { }

    public DbSet<Item> Items => Set<Item>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Item>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(200);
            b.Property(x => x.Unit).IsRequired().HasMaxLength(32);
            b.Property(x => x.Quantity).HasPrecision(18, 3);
        });

        modelBuilder.Entity<StockMovement>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Type).IsRequired();
            b.Property(x => x.Quantity).HasPrecision(18, 3);
            b.Property(x => x.TimestampUtc).IsRequired();
            b.Property(x => x.Reason).HasMaxLength(256);
            b.Property(x => x.User).HasMaxLength(128);

            b.HasOne(x => x.Item)
             .WithMany()
             .HasForeignKey(x => x.ItemId)
             .OnDelete(DeleteBehavior.Cascade);

            b.HasIndex(x => new { x.ItemId, x.TimestampUtc });
        });
    }
}