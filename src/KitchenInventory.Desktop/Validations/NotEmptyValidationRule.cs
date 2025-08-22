using System.Globalization;
using System.Windows.Controls;

namespace KitchenInventory.Desktop.Validations
{
    public class NotEmptyValidationRule : ValidationRule
    {
        public bool AllowWhitespace { get; set; } = false;

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            var s = value as string;
            if (s == null)
            {
                return new ValidationResult(false, "Value is required.");
            }

            if (!AllowWhitespace)
            {
                s = s.Trim();
            }

            if (string.IsNullOrEmpty(s))
            {
                return new ValidationResult(false, "Value is required.");
            }

            return ValidationResult.ValidResult;
        }
    }
}