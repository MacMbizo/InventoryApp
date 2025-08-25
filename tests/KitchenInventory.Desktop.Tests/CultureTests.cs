using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public class CultureTests
{
    [StaFact]
    public void QuantityRegex_RespectsCurrentCultureDecimalSeparator()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUICulture = CultureInfo.CurrentUICulture;
        try
        {
            var culture = new CultureInfo("fr-FR"); // uses comma as decimal separator
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            // Force type initialization after setting culture
            var mwType = typeof(KitchenInventory.Desktop.MainWindow);

            // Access private static readonly Regex QuantityRegex via reflection
            var field = mwType.GetField("QuantityRegex", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(field);
            var regex = field!.GetValue(null) as Regex;
            Assert.NotNull(regex);

            // In fr-FR, comma should be accepted, dot should be rejected
            Assert.True(regex!.IsMatch("1,25"));
            Assert.True(regex!.IsMatch(",5"));
            Assert.False(regex!.IsMatch("1.25"));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }
}