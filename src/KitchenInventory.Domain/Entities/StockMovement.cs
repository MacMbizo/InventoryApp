namespace KitchenInventory.Domain.Entities;

public enum MovementType
{
    Add = 1,
    Consume = 2,
    Adjust = 3
}

public class StockMovement
{
    public int Id { get; set; }

    public int? ItemId { get; set; }
    public Item? Item { get; set; }

    public MovementType Type { get; set; }

    /// <summary>
    /// The signed change this movement made to the item's stock, so that
    /// <c>Item.Quantity == ΣAdd − ΣConsume + ΣAdjust</c> for the item.
    /// <see cref="MovementType.Add"/> and <see cref="MovementType.Consume"/> store a positive
    /// magnitude (the direction is carried by <see cref="Type"/>), while
    /// <see cref="MovementType.Adjust"/> stores the signed delta <c>newStock − oldStock</c>
    /// and may therefore be negative.
    /// Rows written before this convention was introduced stored an Adjust magnitude and
    /// cannot be reconciled; use <c>StockLedger.ReconcileAsync</c> to detect them.
    /// </summary>
    public decimal Quantity { get; set; }

    public string? Reason { get; set; }

    public string? User { get; set; }

    public DateTime TimestampUtc { get; set; }
}