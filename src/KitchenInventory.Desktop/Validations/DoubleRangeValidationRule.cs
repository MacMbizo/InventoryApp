using System;
using System.Globalization;
using System.Windows.Controls;

namespace KitchenInventory.Desktop.Validations
{
    public class DoubleRangeValidationRule : ValidationRule
    {
        public double? Min { get; set; }
        public double? Max { get; set; }
        public bool AllowEmpty { get; set; } = false;

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            var s = value as string;
            if (string.IsNullOrWhiteSpace(s))
            {
                return AllowEmpty
                    ? ValidationResult.ValidResult
                    : new ValidationResult(false, "A numeric value is required.");
            }

            if (!double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, cultureInfo, out var d))
            {
                return new ValidationResult(false, "Enter a valid number.");
            }

            // If input contains group separators, ensure they are used in valid positions for this culture.
            if (ContainsGroupSeparator(s, cultureInfo))
            {
                if (!GroupingMatchesInput(s, d, cultureInfo))
                {
                    return new ValidationResult(false, "Enter a valid number.");
                }
            }

            if (Min.HasValue && d < Min.Value)
            {
                return new ValidationResult(false, $"Must be >= {Min.Value}.");
            }

            if (Max.HasValue && d > Max.Value)
            {
                return new ValidationResult(false, $"Must be <= {Max.Value}.");
            }

            return ValidationResult.ValidResult;
        }

        private static bool ContainsGroupSeparator(string s, CultureInfo culture)
        {
            var nfi = culture.NumberFormat;
            var gs = nfi.NumberGroupSeparator;
            if (!string.IsNullOrEmpty(gs) && s.Contains(gs)) return true;

            // Common case: cultures like fr-FR use non-breaking space; users may type a normal space
            if (gs == "\u00A0" && s.Contains(" ")) return true;

            // If group separator itself is whitespace, be lenient and detect any space
            if (string.IsNullOrWhiteSpace(gs) && s.Contains(" ")) return true;

            return false;
        }

        private static bool GroupingMatchesInput(string input, double parsedValue, CultureInfo culture)
        {
            var nfi = culture.NumberFormat;
            // Normalize input: trim and remove sign
            var s = input.Trim();
            if (s.StartsWith(nfi.NegativeSign)) s = s.Substring(nfi.NegativeSign.Length);
            else if (s.StartsWith(nfi.PositiveSign)) s = s.Substring(nfi.PositiveSign.Length);

            // Split off integer part from input using culture decimal separator
            var decSep = nfi.NumberDecimalSeparator;
            var decIdx = s.IndexOf(decSep, StringComparison.Ordinal);
            var intPartInput = decIdx >= 0 ? s.Substring(0, decIdx) : s;

            // Canonicalize any whitespace-like separators in user input to the culture's group separator
            var gs = nfi.NumberGroupSeparator;
            intPartInput = CanonicalizeGroupSeparators(intPartInput, gs);

            // Format integer part of the parsed value using culture grouping (N0)
            var intPartFormatted = Math.Truncate(Math.Abs(parsedValue)).ToString("N0", culture);

            return string.Equals(intPartInput, intPartFormatted, StringComparison.Ordinal);
        }

        private static string CanonicalizeGroupSeparators(string s, string groupSeparator)
        {
            // Replace common whitespace variants with the culture's group separator
            return s
                .Replace("\u202F", groupSeparator) // Narrow no-break space
                .Replace("\u00A0", groupSeparator) // No-break space
                .Replace(" ", groupSeparator);     // Regular space
        }
    }
}