using System;
using System.Globalization;
using System.Windows.Data;
using KitchenInventory.Domain.Entities;

namespace KitchenInventory.Desktop.Converters
{
    /// <summary>
    /// Renders a <see cref="MovementType"/> as the words a kitchen actually uses.
    /// Binding the enum directly showed users raw database values ("Add", "Consume",
    /// "Adjust") in the activity lists.
    /// </summary>
    public sealed class MovementTypeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value switch
            {
                MovementType.Add => "Taken in",
                MovementType.Consume => "Taken out",
                MovementType.Adjust => "Recount",
                _ => value?.ToString() ?? string.Empty
            };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
