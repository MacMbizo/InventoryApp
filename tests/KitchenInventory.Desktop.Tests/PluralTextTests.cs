using FluentAssertions;
using KitchenInventory.Desktop.Utilities;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

/// <summary>
/// The app talks about countable things — items, categories, locations, users, report rows — and
/// a count of one used to read "Loaded 1 items" because the noun was pluralised by appending "s".
/// These tests pin the two forms, including the irregular "category"/"categories" pair that a
/// suffix rule cannot get right.
/// </summary>
public class PluralTextTests
{
    [Theory]
    [InlineData(0, "0 items")]
    [InlineData(1, "1 item")]
    [InlineData(2, "2 items")]
    [InlineData(-1, "-1 items")]
    public void Count_PairsTheNumberWithTheRightFormOfTheNoun(int count, string expected)
    {
        PluralText.Count(count, "item", "items").Should().Be(expected);
    }

    [Theory]
    [InlineData(1, "category")]
    [InlineData(0, "categories")]
    [InlineData(2, "categories")]
    public void Count_UsesTheSuppliedPlural_SoIrregularNounsAreNotMangled(int count, string expectedNoun)
    {
        PluralText.Count(count, "category", "categories").Should().Be($"{count} {expectedNoun}");
    }

    [Theory]
    [InlineData(1, "item")]
    [InlineData(2, "items")]
    [InlineData(0, "items")]
    public void Noun_ReturnsTheNounAlone_ForMessagesThatCarryTheNumberSeparately(int count, string expected)
    {
        PluralText.Noun(count, "item", "items").Should().Be(expected);
    }
}
