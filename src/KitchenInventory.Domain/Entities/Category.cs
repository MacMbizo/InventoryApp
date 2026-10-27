using System;

namespace KitchenInventory.Domain.Entities;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>
    /// The name. A category is only ever shown as text — the closed drop-down shows the chosen
    /// object, and a screen reader reads out the same thing — and without this the text is the
    /// type name, "KitchenInventory.Domain.Entities.Category".
    /// </summary>
    public override string ToString() => Name;
}