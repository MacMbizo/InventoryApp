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

            if (!double.TryParse(s, NumberStyles.Float, cultureInfo, out var d))
            {
                return new ValidationResult(false, "Enter a valid number.");
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
    }
}