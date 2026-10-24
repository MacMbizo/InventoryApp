using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using KitchenInventory.Desktop.ViewModels;
using KitchenInventory.Domain.Entities;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public sealed class AddItemViewModelTests
{
    private static AddItemViewModel Create(params Category[] categories)
        => new(categories.ToList());

    private static IEnumerable<string> ErrorsOf(AddItemViewModel vm, string property)
        => vm.GetErrors(property).Cast<string>();

    [Fact]
    public void NewInstance_WithoutName_HasErrorAndCannotAdd()
    {
        var vm = Create();

        vm.HasErrors.Should().BeTrue();
        vm.CanAdd.Should().BeFalse();
        ErrorsOf(vm, nameof(vm.ItemName)).Should().Contain("Item name is required.");
    }

    [Fact]
    public void ValidName_ClearsError()
    {
        var vm = Create();

        vm.ItemName = "Flour";

        vm.HasErrors.Should().BeFalse();
        vm.CanAdd.Should().BeTrue();
        ErrorsOf(vm, nameof(vm.ItemName)).Should().BeEmpty();
    }

    [Fact]
    public void NameTooLong_AddsError()
    {
        var vm = Create();

        vm.ItemName = new string('x', 201);

        ErrorsOf(vm, nameof(vm.ItemName)).Should().Contain("Item name is too long (max 200).");
    }

    [Fact]
    public void NegativeQuantity_AddsError()
    {
        var vm = Create();
        vm.ItemName = "Flour";

        vm.Quantity = -1m;

        ErrorsOf(vm, nameof(vm.Quantity)).Should().Contain("Quantity cannot be negative.");
    }

    [Fact]
    public void LargeQuantity_AddsError()
    {
        var vm = Create();
        vm.ItemName = "Flour";

        vm.Quantity = 1_000_001m;

        ErrorsOf(vm, nameof(vm.Quantity)).Should().Contain("Quantity is unrealistically large.");
    }

    [Fact]
    public void UnitTooLong_AddsError()
    {
        var vm = Create();
        vm.ItemName = "Flour";

        vm.Unit = new string('u', 33);

        ErrorsOf(vm, nameof(vm.Unit)).Should().Contain("Unit is too long (max 32).");
    }

    [Fact]
    public void OptionalProperties_ValidateWithoutErrors()
    {
        var vm = Create();
        vm.ItemName = "Flour";

        vm.SelectedCategoryId = 3;
        vm.SelectedLocationId = 4;
        vm.ExpiryDate = DateTime.Today.AddDays(5);

        vm.HasErrors.Should().BeFalse();
        vm.SelectedCategoryId.Should().Be(3);
        vm.SelectedLocationId.Should().Be(4);
        vm.ExpiryDate.Should().Be(DateTime.Today.AddDays(5));
    }

    [Fact]
    public void GetErrors_NullOrUnknownProperty_ReturnsEmpty()
    {
        var vm = Create();

        vm.GetErrors(null).Cast<string>().Should().BeEmpty();
        vm.GetErrors("NotAProperty").Cast<string>().Should().BeEmpty();
    }

    [Fact]
    public void SettingProperties_RaisesErrorsChangedAndPropertyChanged()
    {
        var vm = Create();
        var errorProps = new List<string?>();
        var changeProps = new List<string?>();
        vm.ErrorsChanged += (_, e) => errorProps.Add(e.PropertyName);
        vm.PropertyChanged += (_, e) => changeProps.Add(e.PropertyName);

        vm.ItemName = "Flour";

        errorProps.Should().Contain(nameof(vm.ItemName));
        changeProps.Should().Contain(nameof(vm.HasErrors)).And.Contain(nameof(vm.CanAdd));
    }

    [Fact]
    public void Collections_DefaultToEmpty_WhenNotSupplied()
    {
        var vm = Create();

        vm.Categories.Should().BeEmpty();
        vm.Locations.Should().BeEmpty();
    }

    [Fact]
    public void Collections_AreExposed_WhenSupplied()
    {
        var vm = new AddItemViewModel(
            new List<Category> { new() { Id = 1, Name = "Dry Goods" } },
            new List<Location> { new() { Id = 2, Name = "Dry Store" } });

        vm.Categories.Should().ContainSingle().Which.Name.Should().Be("Dry Goods");
        vm.Locations.Should().ContainSingle().Which.Name.Should().Be("Dry Store");
    }

    [Fact]
    public void AddCommand_CanExecute_TracksValidation()
    {
        var vm = Create();

        vm.AddCommand.CanExecute(null).Should().BeFalse();

        vm.ItemName = "Flour";

        vm.AddCommand.CanExecute(null).Should().BeTrue();
        vm.AddCommand.Execute(null); // no-op execute path
    }

    [Fact]
    public void AddCommand_CanExecuteChanged_RaisedWhenValidationChanges()
    {
        var vm = Create();
        var raised = false;
        vm.AddCommand.CanExecuteChanged += (_, __) => raised = true;

        vm.ItemName = "Flour";

        raised.Should().BeTrue();
    }
}
