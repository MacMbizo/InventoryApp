namespace KitchenInventory.Desktop.Utilities;

/// <summary>
/// Pairs a count with the right form of its noun, so no message can read "Loaded 1 items".
/// </summary>
/// <remarks>
/// Both forms are passed in rather than derived from the singular. English plurals are not a
/// suffix rule — a naive <c>+ "s"</c> turns "category" into "categorys" — so a caller that knows
/// its noun supplies both spellings. What stays in one place is the rule itself: which side of the
/// pair a count takes, and the space between the number and the noun.
/// </remarks>
public static class PluralText
{
    /// <summary>The count followed by the matching noun: "1 item", "2 items", "0 rows".</summary>
    public static string Count(int count, string singular, string plural)
        => $"{count} {Noun(count, singular, plural)}";

    /// <summary>
    /// The noun alone, for messages that carry the number separately — a status line reading
    /// "Imported 2 items", a log template reading "Loaded {Count} {Noun}".
    /// </summary>
    public static string Noun(int count, string singular, string plural)
        => count == 1 ? singular : plural;
}
