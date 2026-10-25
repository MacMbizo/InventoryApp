using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace KitchenInventory.Desktop.Converters
{
    /// <summary>
    /// Turns a movement's <c>ItemId</c> into the item's name for the activity lists.
    /// A bare integer tells a kitchen user nothing; the columns previously showed a
    /// number under a technical "ItemId" header.
    /// </summary>
    /// <remarks>
    /// Expects two bound values: the movement's item id, and the view model's
    /// id-to-name lookup (which includes items that have since been removed).
    /// </remarks>
    public sealed class MovementItemNameConverter : IMultiValueConverter
    {
        /// <summary>Shown when the id is known but the item row no longer exists.</summary>
        public const string UnknownItemText = "(removed item)";

        /// <summary>Shown when a movement has no item at all.</summary>
        public const string NoItemText = "—";

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2) return string.Empty;

            // A nullable int boxes as int when it has a value, and as null when it does not.
            if (values[0] is not int itemId) return NoItemText;

            if (values[1] is IReadOnlyDictionary<int, string> names
                && names.TryGetValue(itemId, out var name)
                && !string.IsNullOrWhiteSpace(name))
            {
                return name;
            }

            return UnknownItemText;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
