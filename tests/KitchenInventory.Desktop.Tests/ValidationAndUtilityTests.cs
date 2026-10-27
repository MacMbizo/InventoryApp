using System;
using System.Globalization;
using FluentAssertions;
using KitchenInventory.Desktop.Services;
using KitchenInventory.Desktop.Utilities;
using KitchenInventory.Desktop.Validations;
using KitchenInventory.Desktop.ViewModels;
using KitchenInventory.Domain.Entities;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public sealed class ValidationAndUtilityTests
{
    // ---------- Entity text ----------
    // The closed drop-down shows the selected object by calling ToString() on it, so a filter
    // displaying "KitchenInventory.Domain.Entities.Category" is what these protect against.

    [Fact]
    public void Category_ShowsItsName_NotItsTypeName()
    {
        var category = new Category { Id = 3, Name = "Dry goods" };

        category.ToString().Should().Be("Dry goods");
        category.ToString().Should().NotContain("KitchenInventory.Domain.Entities");
    }

    [Fact]
    public void Location_ShowsItsName_NotItsTypeName()
    {
        var location = new Location { Id = 4, Name = "Store cupboard" };

        location.ToString().Should().Be("Store cupboard");
        location.ToString().Should().NotContain("KitchenInventory.Domain.Entities");
    }

    [Fact]
    public void ReportOption_ShowsItsName_NotTheWholeRecord()
    {
        var option = new ReportOption(ReportKind.StockOnHand, "Stock on Hand");

        option.ToString().Should().Be("Stock on Hand");
        option.ToString().Should().NotContain("ReportOption");
    }

    // ---------- NotEmptyValidationRule ----------

    private static readonly NotEmptyValidationRule Rule = new();

    [Fact]
    public void NotEmpty_Null_IsInvalid()
    {
        var result = Rule.Validate(null!, CultureInfo.InvariantCulture);

        result.IsValid.Should().BeFalse();
        result.ErrorContent.Should().Be("Value is required.");
    }

    [Fact]
    public void NotEmpty_EmptyString_IsInvalid()
        => Rule.Validate(string.Empty, CultureInfo.InvariantCulture).IsValid.Should().BeFalse();

    [Fact]
    public void NotEmpty_Whitespace_IsInvalid_ByDefault()
        => Rule.Validate("   ", CultureInfo.InvariantCulture).IsValid.Should().BeFalse();

    [Fact]
    public void NotEmpty_Whitespace_IsValid_WhenAllowed()
    {
        var rule = new NotEmptyValidationRule { AllowWhitespace = true };

        rule.Validate("   ", CultureInfo.InvariantCulture).IsValid.Should().BeTrue();
    }

    [Fact]
    public void NotEmpty_Content_IsValid()
        => Rule.Validate("Flour", CultureInfo.InvariantCulture).IsValid.Should().BeTrue();

    // ---------- VersionInfo ----------

    [Fact]
    public void AppVersion_HasSemanticShape()
        => VersionInfo.AppVersion.Should().MatchRegex(@"^\d+\.\d+\.\d+$");

    [Fact]
    public void InformationalVersion_IsPopulated()
        => VersionInfo.InformationalVersion.Should().NotBeNullOrWhiteSpace();

    [Fact]
    public void Commit_IsNeverBlank()
        => VersionInfo.Commit.Should().NotBeNullOrWhiteSpace();

    [Fact]
    public void ShortCommit_IsEitherUnknownOrEightChars()
    {
        var shortCommit = VersionInfo.ShortCommit;

        if (shortCommit == "unknown")
        {
            VersionInfo.Commit.Should().Be("unknown");
        }
        else
        {
            shortCommit.Should().HaveLength(8);
        }
    }

    [Fact]
    public void GetDefaultDiagnosticsFileName_HasExpectedShape()
    {
        var name = VersionInfo.GetDefaultDiagnosticsFileName();

        name.Should().StartWith("diagnostics-").And.EndWith(".zip");
    }

    [Fact]
    public void Commit_ReflectsEnvironmentVariable()
    {
        var original = Environment.GetEnvironmentVariable("INVENTORY_COMMIT_SHA");
        try
        {
            Environment.SetEnvironmentVariable("INVENTORY_COMMIT_SHA", "abcdef0123456789");

            VersionInfo.Commit.Should().Be("abcdef0123456789");
            VersionInfo.ShortCommit.Should().Be("abcdef01");
            VersionInfo.GetDefaultDiagnosticsFileName().Should().Contain("abcdef01");
        }
        finally
        {
            Environment.SetEnvironmentVariable("INVENTORY_COMMIT_SHA", original);
        }
    }

    [Fact]
    public void Commit_IsUnknown_WhenEnvironmentVariableMissing()
    {
        var original = Environment.GetEnvironmentVariable("INVENTORY_COMMIT_SHA");
        try
        {
            Environment.SetEnvironmentVariable("INVENTORY_COMMIT_SHA", null);

            VersionInfo.Commit.Should().Be("unknown");
            VersionInfo.ShortCommit.Should().Be("unknown");
            VersionInfo.GetDefaultDiagnosticsFileName().Should().Contain("-dev-");
        }
        finally
        {
            Environment.SetEnvironmentVariable("INVENTORY_COMMIT_SHA", original);
        }
    }
}
