using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using FluentAssertions;
using KitchenInventory.Desktop.Converters;
using KitchenInventory.Desktop.Services;
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

    // ---------- QuantityDisplayConverter ----------

    private static readonly QuantityDisplayConverter QtyConv = new();

    [Theory]
    [InlineData(10d, "10")]
    [InlineData(0d, "0")]
    [InlineData(2.5d, "2.5")]
    [InlineData(0.25d, "0.25")]
    [InlineData(-2.5d, "-2.5")]
    public void QuantityDisplay_TrimsTrailingZeros(double value, string expected)
        => QtyConv.Convert((decimal)value, typeof(string), null!, Inv).Should().Be(expected);

    [Fact]
    public void QuantityDisplay_KeepsThousandsSeparator()
        => QtyConv.Convert(1234.5m, typeof(string), null!, Inv).Should().Be("1,234.5");

    [Fact]
    public void QuantityDisplay_Null_RendersNothing()
        => QtyConv.Convert(null!, typeof(string), null!, Inv).Should().BeNull();

    [Fact]
    public void QuantityDisplay_UnreadableValue_PassesThrough()
        => QtyConv.Convert("two", typeof(string), null!, Inv).Should().Be("two");

    [Fact]
    public void QuantityDisplay_ConvertBack_Decimal_PassesThrough()
        => QtyConv.ConvertBack(2.5m, typeof(decimal), null!, Inv).Should().Be(2.5m);

    [Fact]
    public void QuantityDisplay_ConvertBack_ParsesTheLocalConvention()
        => QtyConv.ConvertBack("1.5", typeof(decimal), null!, Inv).Should().Be(1.5m);

    [Fact]
    public void QuantityDisplay_ConvertBack_UnreadableInput_LeavesTheValueAlone()
        => QtyConv.ConvertBack("abc", typeof(decimal), null!, Inv).Should().Be(Binding.DoNothing);

    // ---------- FriendlyDateConverter ----------

    private static readonly FriendlyDateConverter DateConv = new();
    private static readonly FriendlyDateConverter RelConv = new() { Mode = FriendlyDateMode.Relative };
    private static readonly FriendlyDateConverter TimeConv = new() { Mode = FriendlyDateMode.LocalTime };

    [Fact]
    public void FriendlyDate_ReadsAsSpoken()
        => DateConv.Convert(new DateTime(2026, 11, 30), typeof(string), null!, Inv).Should().Be("30 Nov 2026");

    [Fact]
    public void FriendlyDate_NonEnglishMachineCulture_StillReadsAsEnglish()
        => DateConv.Convert(new DateTime(2026, 11, 30), typeof(string), null!, new CultureInfo("de-DE"))
            .Should().Be("30 Nov 2026");

    [Fact]
    public void FriendlyDate_ParsesAnIsoString()
        => DateConv.Convert("2026-11-30", typeof(string), null!, Inv).Should().Be("30 Nov 2026");

    [Fact]
    public void FriendlyDate_Null_RendersNothing()
        => DateConv.Convert(null!, typeof(string), null!, Inv).Should().BeNull();

    [Fact]
    public void FriendlyDate_UnreadableValue_RendersNothing()
        => DateConv.Convert(new object(), typeof(string), null!, Inv).Should().BeNull();

    [Theory]
    [InlineData(0, "today")]
    [InlineData(1, "tomorrow")]
    [InlineData(-1, "yesterday")]
    [InlineData(6, "in 6 days")]
    [InlineData(-3, "3 days ago")]
    public void FriendlyDate_Relative_SpellsOutTheDistance(int offsetDays, string expected)
        => RelConv.Convert(DateTime.Today.AddDays(offsetDays), typeof(string), null!, Inv).Should().Be(expected);

    [Fact]
    public void FriendlyDate_LocalTime_ShowsTheReadersClockWithoutSeconds()
    {
        var utc = DateTime.SpecifyKind(new DateTime(2026, 11, 30, 21, 4, 5), DateTimeKind.Utc);

        TimeConv.Convert(utc, typeof(string), null!, Inv).Should().BeOfType<string>()
            .Which.Should().MatchRegex(@"^\d{1,2} [A-Z][a-z]{2} \d{4} at \d{2}:\d{2}$");
    }

    [Fact]
    public void FriendlyDate_LocalTime_TreatsAValueWithoutAKindAsUtc()
    {
        // What SQLite hands back for a UTC column: the instant is right, the Kind is not stated.
        var stored = new DateTime(2026, 11, 30, 21, 4, 0);
        var expected = DateTime.SpecifyKind(stored, DateTimeKind.Utc)
            .ToLocalTime()
            .ToString("d MMM yyyy 'at' HH:mm", Inv);

        TimeConv.Convert(stored, typeof(string), null!, Inv).Should().Be(expected);
    }

    [Fact]
    public void FriendlyDate_ConvertBack_Throws()
        => FluentActions
            .Invoking(() => DateConv.ConvertBack("30 Nov 2026", typeof(DateTime), null!, Inv))
            .Should().Throw<NotSupportedException>();

    // ---------- ItemStatusConverter (IMultiValueConverter) ----------

    private static readonly ItemStatusConverter StatusConv = new();

    private static ItemStatus StatusOf(decimal quantity, DateTime? expiry, string unit = "kg",
        decimal threshold = 5m, int soonDays = 7)
        => (ItemStatus)StatusConv.Convert(
            new object[] { quantity, expiry!, threshold, soonDays, unit }, typeof(ItemStatus), null!, Inv);

    [Fact]
    public void ItemStatus_AHealthyRow_SaysOK()
    {
        var status = StatusOf(10m, null);

        status.Text.Should().Be("OK");
        status.Tone.Should().Be(ItemStatusTone.Ok);
    }

    [Fact]
    public void ItemStatus_RunningLow_Watches_AndKeepsTheUnit()
        => StatusOf(2m, null).Should().Be(new ItemStatus("Low stock (2 kg)", ItemStatusTone.Watch));

    [Fact]
    public void ItemStatus_ItemExpiringSoon_Watches()
    {
        var status = StatusOf(10m, DateTime.Today.AddDays(2));

        status.Tone.Should().Be(ItemStatusTone.Watch);
        status.Text.Should().Be("Expires in 2 days");
    }

    [Fact]
    public void ItemStatus_ItemAlreadyExpired_IsUrgent()
        => StatusOf(10m, DateTime.Today.AddDays(-3)).Tone.Should().Be(ItemStatusTone.Urgent);

    [Fact]
    public void ItemStatus_UsesTheCallersThresholdsRatherThanItsOwn()
        => StatusOf(4m, null, threshold: 3m).Tone.Should().Be(ItemStatusTone.Ok);

    [Fact]
    public void ItemStatus_HandlesDoubleValues()
        => ((ItemStatus)StatusConv.Convert(
            new object[] { 1.5d, null!, 5.0d, 7, "kg" }, typeof(ItemStatus), null!, Inv))
            .Tone.Should().Be(ItemStatusTone.Watch);

    [Fact]
    public void ItemStatus_ParsesStringValues()
        => ((ItemStatus)StatusConv.Convert(
            new object[] { "2", "2099-01-01", "5", "7", "kg" }, typeof(ItemStatus), null!, Inv))
            .Tone.Should().Be(ItemStatusTone.Watch);

    [Fact]
    public void ItemStatus_NoValuesAtAll_SaysOK()
        => ((ItemStatus)StatusConv.Convert(new object[0], typeof(ItemStatus), null!, Inv))
            .Tone.Should().Be(ItemStatusTone.Ok);

    [Fact]
    public void ItemStatus_UnreadableQuantity_SaysNothingRatherThanOutOfStock()
    {
        // A cell mid-edit must not accuse the item of being out of stock.
        var status = (ItemStatus)StatusConv.Convert(
            new object[] { "nonsense", null!, 5m, 7, "kg" }, typeof(ItemStatus), null!, Inv);

        status.Tone.Should().Be(ItemStatusTone.Ok);
    }

    [Fact]
    public void ItemStatus_ConvertBack_Throws()
        => FluentActions
            .Invoking(() => StatusConv.ConvertBack(new ItemStatus("OK", ItemStatusTone.Ok), new[] { typeof(decimal) }, null!, Inv))
            .Should().Throw<NotSupportedException>();
}
