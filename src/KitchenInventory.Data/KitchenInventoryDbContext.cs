using KitchenInventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace KitchenInventory.Data;

public class KitchenInventoryDbContext : DbContext
{
    public KitchenInventoryDbContext(DbContextOptions<KitchenInventoryDbContext> options) : base(options) { }

    public DbSet<Item> Items => Set<Item>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Item>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(200);
            b.Property(x => x.Unit).IsRequired().HasMaxLength(32);
            b.Property(x => x.Quantity).HasPrecision(18, 3);
            b.HasOne(x => x.Category)
             .WithMany()
             .HasForeignKey(x => x.CategoryId)
             .OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => x.CategoryId);

            b.HasOne(x => x.Location)
             .WithMany()
             .HasForeignKey(x => x.LocationId)
             .OnDelete(DeleteBehavior.SetNull);
            b.HasIndex(x => x.LocationId);
        });

        modelBuilder.Entity<Category>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(100);
            b.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<Location>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Name).IsRequired().HasMaxLength(100);
            b.HasIndex(x => x.Name).IsUnique();
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
             .OnDelete(DeleteBehavior.SetNull);

            b.HasIndex(x => new { x.ItemId, x.TimestampUtc });
        });

        // User entity
        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Username).IsRequired().HasMaxLength(100);
            b.Property(x => x.PasswordHash).IsRequired().HasMaxLength(512);
            b.Property(x => x.Role).IsRequired().HasMaxLength(32);
            b.Property(x => x.FailedLoginCount).HasDefaultValue(0);
            b.Property(x => x.LockoutUntil).HasConversion<DateTime?>();
            b.Property(x => x.IsDeleted).HasDefaultValue(false);
            b.Property(x => x.CreatedAtUtc).IsRequired();
            b.Property(x => x.UpdatedAtUtc).IsRequired();

            b.HasIndex(x => x.Username).IsUnique();
            b.HasIndex(x => x.Role);
        });
    }

    // Apply auditing timestamps for entities
    private void ApplyAuditTimestamps()
    {
        var nowUtc = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<Item>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAtUtc == default)
                {
                    entry.Entity.CreatedAtUtc = nowUtc;
                }
                entry.Entity.UpdatedAtUtc = nowUtc;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = nowUtc;
            }
        }

        foreach (var entry in ChangeTracker.Entries<StockMovement>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.TimestampUtc == default)
                {
                    entry.Entity.TimestampUtc = nowUtc;
                }
            }
        }

        foreach (var entry in ChangeTracker.Entries<User>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAtUtc == default)
                {
                    entry.Entity.CreatedAtUtc = nowUtc;
                }
                entry.Entity.UpdatedAtUtc = nowUtc;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = nowUtc;
            }
        }
    }

    public override int SaveChanges()
    {
        ApplyAuditTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }
}