using System;
using System.Globalization;
using System.Windows.Data;

namespace KitchenInventory.Desktop.Converters;

/// <summary>How <see cref="FriendlyDateConverter"/> renders the date it is handed.</summary>
public enum FriendlyDateMode
{
    /// <summary>"30 Nov 2026" — the date as a reader would say it, not "2026-11-30".</summary>
    Date,

    /// <summary>"in 6 days" / "yesterday" — how far away the date is, which is usually the question.</summary>
    Relative,

    /// <summary>"25 Oct 2026 at 21:04" — a stored UTC instant in the reader's own time zone.</summary>
    LocalTime
}

/// <summary>
/// Renders the dates the app stores for people rather than machines: "30 Nov 2026" instead of
/// "2026-11-30", "in 6 days" when the distance is what matters, and UTC instants in the reader's
/// own clock with the seconds dropped.
/// </summary>
public sealed class FriendlyDateConverter : IValueConverter
{
    /// <summary>Which rendering to produce. Set from XAML; a plain date by default.</summary>
    public FriendlyDateMode Mode { get; set; } = FriendlyDateMode.Date;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Nothing to describe: an empty cell rather than the word "null", and — because a tooltip
        // bound to this is also nothing — no empty tooltip box following the mouse.
        if (value is null) return null;
        if (AsDate(value) is not DateTime moment) return null;

        return Mode switch
        {
            FriendlyDateMode.Relative => Relative(moment.Date),
            // The column is stored in UTC and may come back from SQLite without its Kind, so the
            // kind is restated here; otherwise ToLocalTime would add the offset on top of a value
            // it already treats as local.
            FriendlyDateMode.LocalTime => DateTime.SpecifyKind(moment, DateTimeKind.Utc)
                .ToLocalTime()
                .ToString("d MMM yyyy 'at' HH:mm", CultureInfo.InvariantCulture),
            _ => moment.ToString("d MMM yyyy", CultureInfo.InvariantCulture)
        };
    }

    /// <summary>
    /// Spells out the distance in whole days, which is how a person thinks about a use-by date:
    /// "in 6 days" answers the question the date is being read for.
    /// </summary>
    private static string Relative(DateTime date)
    {
        var days = (date - DateTime.Today).Days;
        return days switch
        {
            0 => "today",
            1 => "tomorrow",
            -1 => "yesterday",
            > 1 => $"in {days} days",
            _ => $"{-days} days ago"
        };
    }

    private static DateTime? AsDate(object value)
        => value switch
        {
            DateTime dt => dt,
            DateTimeOffset dto => dto.UtcDateTime,
            string s when DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) => parsed,
            _ => null
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
