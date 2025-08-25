using System.Globalization;
using KitchenInventory.Desktop.Validations;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public class DoubleRangeValidationRuleTests
{
    [Theory]
    [InlineData("en-US", "1.5", true)]
    [InlineData("en-US", "1,5", false)]
    [InlineData("de-DE", "1,5", true)]
    [InlineData("de-DE", "1.5", false)]
    [InlineData("fr-FR", "1234,567", true)]
    [InlineData("fr-FR", "1 234,5", true)]
    public void Parses_Decimals_With_Culture(string cultureName, string input, bool expectedValid)
    {
        var rule = new DoubleRangeValidationRule { Min = 0, Max = 1000000, AllowEmpty = false };
        var culture = new CultureInfo(cultureName);

        var result = rule.Validate(input, culture);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Rejects_Empty_When_Not_Allowed()
    {
        var rule = new DoubleRangeValidationRule { Min = 0, Max = 1000000, AllowEmpty = false };
        var culture = CultureInfo.InvariantCulture;
        var result = rule.Validate("", culture);
        Assert.False(result.IsValid);
    }
}