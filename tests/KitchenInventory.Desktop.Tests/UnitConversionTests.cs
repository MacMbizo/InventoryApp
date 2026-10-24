using FluentAssertions;
using KitchenInventory.Desktop.Services;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public sealed class UnitConversionTests
{
    [Theory]
    [InlineData(1.5, "kg", "g", 1500)]
    [InlineData(500, "g", "kg", 0.5)]
    [InlineData(2, "kg", "mg", 2000000)]
    [InlineData(2, "l", "ml", 2000)]
    [InlineData(250, "ml", "l", 0.25)]
    [InlineData(1, "l", "cl", 100)]
    [InlineData(1, "dl", "ml", 100)]
    public void TryConvert_ConvertsWithinDimension(decimal value, string from, string to, decimal expected)
    {
        var ok = UnitConversionService.TryConvert(value, from, to, out var result);

        ok.Should().BeTrue();
        result.Should().Be(expected);
    }

    [Fact]
    public void TryConvert_IsCaseInsensitive()
    {
        UnitConversionService.TryConvert(1m, "KG", "G", out var result).Should().BeTrue();
        result.Should().Be(1000m);
    }

    [Theory]
    [InlineData("Kilograms", "kg")]
    [InlineData("gram", "g")]
    [InlineData("Litre", "l")]
    [InlineData("millilitres", "ml")]
    [InlineData("kilo", "kg")]
    public void TryGetCanonical_ResolvesAliases(string alias, string canonical)
    {
        UnitConversionService.TryGetCanonical(alias, out var resolved).Should().BeTrue();
        resolved.Should().Be(canonical);
    }

    [Theory]
    [InlineData(1.5, "Kilograms", "gram", 1500)]
    [InlineData(2, "litre", "ml", 2000)]
    public void TryConvert_AcceptsLongFormAliases(decimal value, string from, string to, decimal expected)
    {
        UnitConversionService.TryConvert(value, from, to, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Fact]
    public void TryConvert_IncompatibleDimensions_ReturnsFalse()
    {
        UnitConversionService.TryConvert(1m, "kg", "ml", out var result).Should().BeFalse();
        result.Should().Be(0m);
    }

    [Theory]
    [InlineData("pcs", "g")]
    [InlineData("kg", "pcs")]
    [InlineData("", "g")]
    [InlineData("kg", "")]
    public void TryConvert_UnknownUnit_ReturnsFalse(string from, string to)
    {
        UnitConversionService.TryConvert(1m, from, to, out _).Should().BeFalse();
    }

    [Fact]
    public void TryConvert_NullUnits_ReturnFalse()
    {
        UnitConversionService.TryConvert(1m, null, "g", out _).Should().BeFalse();
        UnitConversionService.TryConvert(1m, "g", null, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("kg", "g", true)]
    [InlineData("kg", "ml", false)]
    [InlineData("l", "ml", true)]
    [InlineData("kg", "pcs", false)]
    public void AreCompatible_ReflectsDimensions(string from, string to, bool expected)
    {
        UnitConversionService.AreCompatible(from, to).Should().Be(expected);
    }

    [Fact]
    public void TryGetCanonical_UnknownUnit_ReturnsFalse()
    {
        UnitConversionService.TryGetCanonical("pcs", out var canonical).Should().BeFalse();
        canonical.Should().BeEmpty();
    }

    [Fact]
    public void FormatConversion_ProducesReadableText()
    {
        UnitConversionService.FormatConversion(1.5m, "kg", "g").Should().Be("1.5 kg = 1500 g");
    }

    [Fact]
    public void FormatConversion_IncompatibleUnits_ExplainsFailure()
    {
        UnitConversionService.FormatConversion(1m, "kg", "ml").Should().Be("Cannot convert 1 kg to ml.");
    }

    [Fact]
    public void KnownUnits_ExposeMassAndVolumeSymbols()
    {
        UnitConversionService.KnownUnits.Should().Contain(new[] { "mg", "g", "kg", "ml", "cl", "dl", "l" });
        UnitConversionService.UnitsForDimension("mass").Should().Equal("mg", "g", "kg");
        UnitConversionService.UnitsForDimension("volume").Should().Equal("ml", "cl", "dl", "l");
    }

    [Fact]
    public void AliasCount_IsGreaterThanCanonicalUnitCount()
    {
        UnitConversionService.AliasCount.Should().BeGreaterThan(UnitConversionService.KnownUnits.Count);
    }
}
