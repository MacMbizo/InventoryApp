using System;

namespace KitchenInventory.Domain.Entities;

public class Item
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "pcs";
    public DateTime? ExpiryDate { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public int? LocationId { get; set; }
    public Location? Location { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>
    /// Soft-delete flag. Deleted items are hidden from every read path by a global query
    /// filter, but the row and its movement history are retained for audit. The stock is
    /// written off with an Adjust movement and <see cref="Quantity"/> is zeroed, so the
    /// ledger invariant still holds for the hidden row.
    /// </summary>
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Optimistic-concurrency token. SQLite has no native rowversion, so this is a random
    /// value the context regenerates on every insert and update. A stale writer's save
    /// matches no rows and fails the concurrency check instead of silently overwriting.
    /// </summary>
    public Guid RowVersion { get; set; }
}