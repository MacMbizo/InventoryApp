using System.Globalization;
using FluentAssertions;
using KitchenInventory.Desktop.Utilities;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

/// <summary>
/// Quantities are typed on whatever keyboard the school has and pasted in from spreadsheets and
/// websites, so both the typing guard and the paste conversion have to follow the machine's own
/// separator convention rather than assuming en-US.
/// </summary>
public class QuantityTextTests
{
    private static readonly CultureInfo EnUs = new("en-US");
    private static readonly CultureInfo FrFr = new("fr-FR");
    private static readonly CultureInfo DeDe = new("de-DE");

    [Theory]
    [InlineData("en-US", "")]
    [InlineData("en-US", "12")]
    [InlineData("en-US", "12.5")]
    [InlineData("en-US", "12.345")]
    [InlineData("fr-FR", "12,5")]
    [InlineData("de-DE", "12,345")]
    public void IsValidTyping_AcceptsDigitsAndTheLocalSeparator(string cultureName, string proposed)
    {
        QuantityText.IsValidTyping(proposed, new CultureInfo(cultureName)).Should().BeTrue();
    }

    [Theory]
    [InlineData("en-US", "12,5")]
    [InlineData("fr-FR", "12.5")]
    [InlineData("de-DE", "12.5")]
    [InlineData("en-US", "12.3456")]
    [InlineData("en-US", "1.2.3")]
    [InlineData("en-US", "abc")]
    [InlineData("en-US", "-1")]
    public void IsValidTyping_RefusesWhatCouldNeverBeAQuantity(string cultureName, string proposed)
    {
        QuantityText.IsValidTyping(proposed, new CultureInfo(cultureName)).Should().BeFalse();
    }

    [Fact]
    public void IsValidTyping_AllowsAStrandedSeparatorWhileTyping()
    {
        // "12." is where every decimal entry passes through, so it must not be refused.
        QuantityText.IsValidTyping("12.", EnUs).Should().BeTrue();
    }

    [Theory]
    [InlineData("10.0")]
    [InlineData("10.00")]
    [InlineData("10.000")]
    public void WithoutStoredScale_DropsThePlacesTheAmountWasWrittenWith(string stored)
    {
        // A quantity read back from the database carries the scale it was stored with, which is
        // what made the edit dialog read "10.0" while the grid read "10".
        var value = decimal.Parse(stored, CultureInfo.InvariantCulture);

        QuantityText.WithoutStoredScale(value).Should().Be(10m);
        QuantityText.WithoutStoredScale(value).ToString(CultureInfo.InvariantCulture).Should().Be("10");
    }

    [Theory]
    [InlineData("2.5", "2.5")]
    [InlineData("0.25", "0.25")]
    [InlineData("1234.5", "1234.5")]
    [InlineData("0.0", "0")]
    [InlineData("1000000", "1000000")]
    public void WithoutStoredScale_KeepsAmountsThatNeedTheirPlaces(string stored, string expected)
    {
        var value = decimal.Parse(stored, CultureInfo.InvariantCulture);

        QuantityText.WithoutStoredScale(value).ToString(CultureInfo.InvariantCulture).Should().Be(expected);
    }

    [Theory]
    [InlineData("1,5", "1.5")]      // a comma decimal from a continental keyboard
    [InlineData("1,500", "1500")]   // thousands grouping, not a decimal
    [InlineData("1,500.25", "1500.25")]
    [InlineData(" 1 500 ", "1500")]
    [InlineData("1\u00A0500", "1500")]   // non-breaking space, as fr-FR groups
    [InlineData("1'500", "1500")]        // Swiss apostrophe grouping
    [InlineData("2.5\r\n", "2.5")]       // copied straight out of a spreadsheet
    [InlineData("abc", "abc")]           // unrecognised: left for the validator to report
    [InlineData("1,2,3", "1,2,3")]
    public void NormalizePasted_ConvertsForeignInputToTheLocalConvention(string pasted, string expected)
    {
        QuantityText.NormalizePasted(pasted, EnUs).Should().Be(expected);
    }

    [Theory]
    [InlineData("1.5", "1,5")]
    [InlineData("1 234,5", "1234,5")]
    [InlineData("1\u202F234,5", "1234,5")]   // narrow no-break space, as de-DE groups
    [InlineData("1,5", "1,5")]
    public void NormalizePasted_ConvertsToACommaDecimalCulture(string pasted, string expected)
    {
        QuantityText.NormalizePasted(pasted, FrFr).Should().Be(expected);
    }

    [Fact]
    public void NormalizePasted_ReturnsEmptyForWhitespace()
    {
        QuantityText.NormalizePasted("   ", EnUs).Should().BeEmpty();
        QuantityText.NormalizePasted(null, EnUs).Should().BeEmpty();
    }

    [Theory]
    [InlineData("1,500", true)]
    [InlineData("-1,500", true)]
    [InlineData("12,345,678", true)]
    [InlineData("1,5", false)]
    [InlineData("1234,567", false)]
    [InlineData("1,2,3", false)]
    [InlineData("500", false)]
    [InlineData("", false)]
    public void SplitsIntoWholeThousands_OnlyWhenEveryGroupIsAGroup(string text, bool expected)
    {
        QuantityText.SplitsIntoWholeThousands(text, ',').Should().Be(expected);
    }

    [Fact]
    public void NormalizedPaste_IsAcceptedByTheTypingGuard()
    {
        // The paste only helps if what it produces is then considered a legal quantity, which is
        // the check the dialog makes before writing the value into the box.
        foreach (var culture in new[] { EnUs, FrFr, DeDe })
        {
            foreach (var pasted in new[] { "1,5", "1.5", "1 500", "1,500.25", "1'500" })
            {
                var normalized = QuantityText.NormalizePasted(pasted, culture);
                if (decimal.TryParse(normalized, NumberStyles.Number, culture, out _))
                {
                    QuantityText.IsValidTyping(normalized, culture).Should().BeTrue(
                        $"'{pasted}' normalised to '{normalized}' under {culture.Name} should be typeable");
                }
            }
        }
    }
}
