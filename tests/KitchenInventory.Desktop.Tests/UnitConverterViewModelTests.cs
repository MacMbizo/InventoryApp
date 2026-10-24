using System.Collections.Generic;
using FluentAssertions;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Desktop.ViewModels;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public sealed class UnitConverterViewModelTests
{
    [Fact]
    public void Defaults_ProduceKilogramToGramResult()
    {
        var vm = new UnitConverterViewModel();

        vm.InputText.Should().Be("1");
        vm.FromUnit.Should().Be("kg");
        vm.ToUnit.Should().Be("g");
        vm.ResultText.Should().Be("1 kg = 1000 g");
    }

    [Fact]
    public void ChangingInput_Recomputes()
    {
        var vm = new UnitConverterViewModel { InputText = "2" };

        vm.ResultText.Should().Be("2 kg = 2000 g");
    }

    [Fact]
    public void ChangingUnits_Recomputes()
    {
        var vm = new UnitConverterViewModel { FromUnit = "l", ToUnit = "ml" };

        vm.ResultText.Should().Be("1 l = 1000 ml");
    }

    [Fact]
    public void InvalidInput_ShowsMessage()
    {
        var vm = new UnitConverterViewModel { InputText = "abc" };

        vm.ResultText.Should().Be("Enter a valid number.");
    }

    [Fact]
    public void Swap_SwapsUnitsAndRecomputes()
    {
        var vm = new UnitConverterViewModel { InputText = "1500", FromUnit = "g", ToUnit = "kg" };

        vm.Swap();

        vm.FromUnit.Should().Be("kg");
        vm.ToUnit.Should().Be("g");
        vm.ResultText.Should().Be("1500 kg = 1500000 g");
    }

    [Fact]
    public void Units_ExposesKnownUnits()
    {
        var vm = new UnitConverterViewModel();

        vm.Units.Should().Equal(UnitConversionService.KnownUnits);
    }

    [Fact]
    public void PropertyChanged_RaisedForInputAndResult()
    {
        var vm = new UnitConverterViewModel();
        var props = new List<string?>();
        vm.PropertyChanged += (_, e) => props.Add(e.PropertyName);

        vm.InputText = "5";

        props.Should().Contain(nameof(vm.InputText)).And.Contain(nameof(vm.ResultText));
    }

    [Fact]
    public void SettingSameValue_DoesNotRaiseOrChangeResult()
    {
        var vm = new UnitConverterViewModel();
        var count = 0;
        vm.PropertyChanged += (_, __) => count++;

        vm.InputText = "1"; // identical to the default

        count.Should().Be(0);
    }
}
