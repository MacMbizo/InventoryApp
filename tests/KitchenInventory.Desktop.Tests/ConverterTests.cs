using System;
using System.Globalization;
using System.Windows;
using FluentAssertions;
using KitchenInventory.Desktop.Converters;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public sealed class ConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // ---------- NeedsAttentionConverter (IMultiValueConverter) ----------

    private static object NeedsAttention(object[] values, CultureInfo? culture = null)
        => new NeedsAttentionConverter().Convert(values, typeof(bool), null!, culture ?? Inv);

    [Fact]
    public void NeedsAttention_LowStock_ReturnsTrue()
        => NeedsAttention(new object[] { 2m, null!, 5m, 7 }).Should().Be(true);

    [Fact]
    public void NeedsAttention_SufficientStock_NoExpiry_ReturnsFalse()
        => NeedsAttention(new object[] { 10m, null!, 5m, 7 }).Should().Be(false);

    [Fact]
    public void NeedsAttention_ExpiringWithinWindow_ReturnsTrue()
        => NeedsAttention(new object[] { 10m, DateTime.Today.AddDays(3), 5m, 7 }).Should().Be(true);

    [Fact]
    public void NeedsAttention_ExpiryBeyondWindow_ReturnsFalse()
        => NeedsAttention(new object[] { 10m, DateTime.Today.AddDays(30), 5m, 7 }).Should().Be(false);

    [Fact]
    public void NeedsAttention_HandlesDoubleValues()
        => NeedsAttention(new object[] { 1.5d, null!, 5.0d, 7 }).Should().Be(true);

    [Fact]
    public void NeedsAttention_ParsesStringValues()
        => NeedsAttention(new object[] { "2", "2099-01-01", "5", "7" }).Should().Be(true);

    [Fact]
    public void NeedsAttention_UsesDefaults_WhenOptionalArgumentsMissing()
    {
        NeedsAttention(new object[] { 3m }).Should().Be(true);   // below default threshold 5
        NeedsAttention(new object[] { 50m }).Should().Be(false);
    }

    [Fact]
    public void NeedsAttention_NullValues_ReturnsFalse()
        => NeedsAttention(null!).Should().Be(false);             // triggers the catch path

    [Fact]
    public void NeedsAttention_ConvertBack_Throws()
        => FluentActions
            .Invoking(() => new NeedsAttentionConverter().ConvertBack(true, new[] { typeof(decimal) }, null!, Inv))
            .Should().Throw<NotSupportedException>();

    // ---------- DoubleDecimalConverter ----------

    private static readonly DoubleDecimalConverter DoubleConv = new();

    [Fact]
    public void DoubleDecimal_Convert_Decimal_ReturnsDouble()
        => DoubleConv.Convert(2.5m, typeof(double), null!, Inv).Should().Be(2.5d);

    [Fact]
    public void DoubleDecimal_Convert_Double_PassesThrough()
        => DoubleConv.Convert(3.25d, typeof(double), null!, Inv).Should().Be(3.25d);

    [Fact]
    public void DoubleDecimal_Convert_NumericString_Parses()
        => DoubleConv.Convert("4.5", typeof(double), null!, Inv).Should().Be(4.5d);

    [Fact]
    public void DoubleDecimal_Convert_Unsupported_ReturnsZero()
        => DoubleConv.Convert(new object(), typeof(double), null!, Inv).Should().Be(0d);

    [Fact]
    public void DoubleDecimal_ConvertBack_Double_ReturnsDecimal()
        => DoubleConv.ConvertBack(2.5d, typeof(decimal), null!, Inv).Should().Be(2.5m);

    [Fact]
    public void DoubleDecimal_ConvertBack_Decimal_PassesThrough()
        => DoubleConv.ConvertBack(7.5m, typeof(decimal), null!, Inv).Should().Be(7.5m);

    [Fact]
    public void DoubleDecimal_ConvertBack_NumericString_Parses()
        => DoubleConv.ConvertBack("6.25", typeof(decimal), null!, Inv).Should().Be(6.25m);

    [Fact]
    public void DoubleDecimal_ConvertBack_Unsupported_ReturnsZero()
        => DoubleConv.ConvertBack(new object(), typeof(decimal), null!, Inv).Should().Be(0m);

    // ---------- HideZeroIdConverter ----------

    private static readonly HideZeroIdConverter HideConv = new();

    [Fact]
    public void HideZeroId_Null_ReturnsCollapsed()
        => HideConv.Convert(null!, typeof(Visibility), null!, Inv).Should().Be(Visibility.Collapsed);

    [Fact]
    public void HideZeroId_ZeroInt_ReturnsCollapsed()
        => HideConv.Convert(0, typeof(Visibility), null!, Inv).Should().Be(Visibility.Collapsed);

    [Fact]
    public void HideZeroId_NonZeroInt_ReturnsVisible()
        => HideConv.Convert(5, typeof(Visibility), null!, Inv).Should().Be(Visibility.Visible);

    [Fact]
    public void HideZeroId_ZeroString_ReturnsCollapsed()
        => HideConv.Convert("0", typeof(Visibility), null!, Inv).Should().Be(Visibility.Collapsed);

    [Fact]
    public void HideZeroId_NonNumeric_ReturnsCollapsed()
        => HideConv.Convert("abc", typeof(Visibility), null!, Inv).Should().Be(Visibility.Collapsed);

    [Fact]
    public void HideZeroId_ConvertBack_Throws()
        => FluentActions
            .Invoking(() => HideConv.ConvertBack(Visibility.Visible, typeof(int), null!, Inv))
            .Should().Throw<NotSupportedException>();

    // ---------- InverseBooleanConverter ----------

    private static readonly InverseBooleanConverter InverseConv = new();

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void InverseBoolean_Convert_BoolInverts(bool input, bool expected)
        => InverseConv.Convert(input, typeof(bool), null!, Inv).Should().Be(expected);

    [Fact]
    public void InverseBoolean_Convert_Null_ReturnsTrue()
        => InverseConv.Convert(null!, typeof(bool), null!, Inv).Should().Be(true);

    [Fact]
    public void InverseBoolean_Convert_BoolString_Parses()
        => InverseConv.Convert("true", typeof(bool), null!, Inv).Should().Be(false);

    [Fact]
    public void InverseBoolean_Convert_NonBool_ReturnsTrue()
        => InverseConv.Convert(123, typeof(bool), null!, Inv).Should().Be(true);

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void InverseBoolean_ConvertBack_BoolInverts(bool input, bool expected)
        => InverseConv.ConvertBack(input, typeof(bool), null!, Inv).Should().Be(expected);

    [Fact]
    public void InverseBoolean_ConvertBack_Null_ReturnsTrue()
        => InverseConv.ConvertBack(null!, typeof(bool), null!, Inv).Should().Be(true);

    [Fact]
    public void InverseBoolean_ConvertBack_BoolString_Parses()
        => InverseConv.ConvertBack("false", typeof(bool), null!, Inv).Should().Be(true);

    [Fact]
    public void InverseBoolean_ConvertBack_NonBool_ReturnsTrue()
        => InverseConv.ConvertBack(123, typeof(bool), null!, Inv).Should().Be(true);
}
