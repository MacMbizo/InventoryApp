using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace KitchenInventory.Desktop.Utilities;

/// <summary>
/// Decides what counts as a quantity while it is being typed, and turns a pasted quantity into
/// the separator conventions of the machine it is being pasted on.
/// </summary>
/// <remarks>
/// Staff copy amounts out of spreadsheets and off websites, so a paste can arrive with a decimal
/// point or comma from another locale, with thousands separators, or with a trailing newline.
/// Typing is guided to the local convention (the box simply refuses the wrong separator), but a
/// paste is converted rather than silently swallowed, which is what used to happen: the paste
/// command was cancelled with no sign that anything was wrong.
/// </remarks>
public static class QuantityText
{
    /// <summary>How many digits are allowed after the decimal separator.</summary>
    public const int MaxDecimalPlaces = 3;

    // Characters that are only ever thousands separators in a number: spaces (including the
    // non-breaking and narrow no-break spaces used by fr-FR and de-DE) and the Swiss apostrophe.
    private static readonly char[] GroupSpaces = { '\u00A0', '\u202F', '\u2009', '\u2019', '\'' };

    private static readonly ConcurrentDictionary<string, Regex> TypingPatterns = new();

    /// <summary>
    /// True when <paramref name="proposed"/> could still become a valid quantity, which is the
    /// question a text box asks on every keystroke: empty, digits, and at most one occurrence of
    /// the local decimal separator with at most <see cref="MaxDecimalPlaces"/> digits after it.
    /// </summary>
    public static bool IsValidTyping(string? proposed, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (string.IsNullOrEmpty(proposed))
        {
            return true;
        }

        var pattern = TypingPatterns.GetOrAdd(
            culture.NumberFormat.NumberDecimalSeparator,
            separator => new Regex(
                $"^\\d*(?:{Regex.Escape(separator)}\\d{{0,{MaxDecimalPlaces}}})?$",
                RegexOptions.Compiled | RegexOptions.CultureInvariant));

        return pattern.IsMatch(proposed);
    }

    /// <summary>
    /// Rewrites a pasted value so that it uses <paramref name="culture"/>'s separators, returning
    /// the input unchanged when it cannot be recognised as a number — the validation rules then
    /// report it, which is better than guessing at what the user meant.
    /// </summary>
    public static string NormalizePasted(string? pasted, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        if (string.IsNullOrWhiteSpace(pasted))
        {
            return string.Empty;
        }

        var text = pasted.Trim();

        // A space can never be part of a number literal, so "1 500" is always 1500.
        text = RemoveGroupSpaces(text);
        if (text.Length == 0)
        {
            return text;
        }

        var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;
        var otherSeparator = decimalSeparator == "." ? ',' : '.';
        if (!text.Contains(otherSeparator, StringComparison.Ordinal))
        {
            return text;
        }

        var decimalIndex = text.IndexOf(decimalSeparator, StringComparison.Ordinal);
        if (decimalIndex >= 0)
        {
            // The local separator is the decimal point, so the other one can only be grouping —
            // but only remove it when it really does split whole thousands ("1,500.25").
            var integerPart = text[..decimalIndex];
            var tail = text[decimalIndex..];
            return integerPart.Contains(otherSeparator, StringComparison.Ordinal) &&
                   SplitsIntoWholeThousands(integerPart, otherSeparator)
                ? integerPart.Replace(otherSeparator.ToString(), string.Empty, StringComparison.Ordinal) + tail
                : text;
        }

        // Only the foreign separator is present: "1,500" is 1500, "1,5" is 1.5.
        if (SplitsIntoWholeThousands(text, otherSeparator))
        {
            return text.Replace(otherSeparator.ToString(), string.Empty, StringComparison.Ordinal);
        }

        return CountOf(text, otherSeparator) == 1
            ? text.Replace(otherSeparator.ToString(), decimalSeparator, StringComparison.Ordinal)
            : text;
    }

    /// <summary>Whether every group a separator splits off is exactly three digits, bar the first.</summary>
    public static bool SplitsIntoWholeThousands(string text, char separator)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var groups = text.Split(separator);
        if (groups.Length < 2)
        {
            return false;
        }

        var first = groups[0];
        if (first.Length > 0 && (first[0] == '+' || first[0] == '-'))
        {
            first = first[1..];
        }

        if (first.Length is 0 or > 3 || !AllDigits(first))
        {
            return false;
        }

        for (var i = 1; i < groups.Length; i++)
        {
            if (groups[i].Length != 3 || !AllDigits(groups[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string RemoveGroupSpaces(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsWhiteSpace(c) && Array.IndexOf(GroupSpaces, c) < 0)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static bool AllDigits(string text)
    {
        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static int CountOf(string text, char value)
    {
        var count = 0;
        foreach (var c in text)
        {
            if (c == value)
            {
                count++;
            }
        }

        return count;
    }
}
