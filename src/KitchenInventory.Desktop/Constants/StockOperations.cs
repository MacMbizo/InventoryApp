namespace KitchenInventory.Desktop.Constants;

/// <summary>
/// The stock-operation names as they are shown to the user. These strings label the
/// toolbar buttons, title the stock dialog and drive its calculation, so they live in
/// one place instead of being repeated as literals.
/// </summary>
/// <remarks>
/// Deliberately plain language: a school kitchen records stock arriving, stock being
/// used, and a stock take. "Adjust" and "consume" are database words, not kitchen words.
/// </remarks>
public static class StockOperations
{
    /// <summary>A delivery or any other increase: "Take in stock".</summary>
    public const string TakeIn = "Take in stock";

    /// <summary>Stock used in cooking: "Take out stock".</summary>
    public const string TakeOut = "Take out stock";

    /// <summary>A stock take that sets the quantity to what was actually counted: "Recount".</summary>
    public const string Recount = "Recount";
}
