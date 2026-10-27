using System;

namespace KitchenInventory.Domain.Entities;

public class Location
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    /// <summary>
    /// The name, for the same reason as <see cref="Category.ToString"/>: a location is shown as
    /// text in the filters and in the item dialog, and the type name is not that text.
    /// </summary>
    public override string ToString() => Name;
}
