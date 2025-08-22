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

    public int ItemId { get; set; }
    public Item? Item { get; set; }

    public MovementType Type { get; set; }

    public decimal Quantity { get; set; }

    public string? Reason { get; set; }

    public string? User { get; set; }

    public DateTime TimestampUtc { get; set; }
}