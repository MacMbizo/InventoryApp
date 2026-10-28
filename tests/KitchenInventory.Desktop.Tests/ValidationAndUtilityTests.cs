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

    // ---------- The spreadsheet the item import expects ----------
    // The import has always wanted a particular shape and never said so anywhere, which is only
    // discovered by feeding it a file and reading the complaints. The template is the shape
    // written down, so what it says has to be exactly what the import reads.

    private static readonly Category[] NoCategories = Array.Empty<Category>();
    private static readonly Location[] NoLocations = Array.Empty<Location>();

    [Fact]
    public void Template_StartsWithItsOwnHeader()
    {
        var lines = CsvImportService.BuildTemplateCsv().Split('\n');

        lines[0].Trim().Should().Be(CsvImportService.TemplateHeader);
        lines[0].Trim().Should().Be("Name,Quantity,Unit,CategoryName,LocationName,ExpiryDate");
    }

    [Fact]
    public void Template_IsItselfImportable()
    {
        // The strongest statement available about the example: the import would accept it.
        CsvImportService.DescribeHeaderProblem(CsvImportService.BuildTemplateCsv()).Should().BeNull();
    }

    [Fact]
    public void Template_CarriesNoIdColumns_SoAFileFromAnotherMachineCannotMatchTheWrongItem()
        => CsvImportService.TemplateHeader.Should().NotContain("Id");

    [Fact]
    public void Template_LeavesTheSecondRowBlankToShowWhichColumnsAreOptional()
    {
        var rows = CsvImportService.BuildTemplateCsv().Split('\n');

        // Header, the filled-in example, the one that leaves everything optional out.
        rows.Should().HaveCountGreaterThanOrEqualTo(3);
        rows[1].Should().StartWith("Rice,10,kg,");
        rows[2].Trim().Should().Be("Milk,12,L,,,");

        var expiry = rows[1].Split(',')[5].Trim();
        DateTime.TryParseExact(expiry, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
            .Should().BeTrue($"the example expiry '{expiry}' is not written as yyyy-MM-dd");
    }

    [Fact]
    public void Template_NamesACategoryAndLocationThatAlreadyExist()
    {
        var categories = new[]
        {
            new Category { Id = 0, Name = "All" },      // the filter sentinel, not a real category
            new Category { Id = 7, Name = "Dry goods" }
        };
        var locations = new[]
        {
            new Location { Id = 0, Name = "All" },
            new Location { Id = 9, Name = "Store cupboard" }
        };

        var row = CsvImportService.BuildTemplateCsv(categories, locations).Split('\n')[1].Split(',');

        // Naming one that does not exist would make every row of the example fail to import.
        row[3].Should().Be("Dry goods");
        row[4].Should().Be("Store cupboard");
    }

    [Fact]
    public void Template_LeavesCategoryAndLocationEmpty_WhenThereAreNoneYet()
    {
        var row = CsvImportService.BuildTemplateCsv(NoCategories, NoLocations).Split('\n')[1].Split(',');

        row[3].Should().BeEmpty();
        row[4].Should().BeEmpty();
    }

    [Fact]
    public void DescribeHeaderProblem_AcceptsAFileThatNamesItsColumns()
    {
        CsvImportService.DescribeHeaderProblem("Name,Quantity,Unit\nRice,10,kg").Should().BeNull();
        CsvImportService.DescribeHeaderProblem("  name , qty \nRice,10").Should().BeNull();
    }

    [Fact]
    public void DescribeHeaderProblem_FlagsAFirstLineThatNamesNoKnownColumn()
    {
        // What a file saved out of a spreadsheet that was never meant for this app looks like.
        var problem = CsvImportService.DescribeHeaderProblem("Item,Qty,Notes\nRice,10,plenty");

        problem.Should().NotBeNull();
        problem.Should().Contain("first line");
        problem.Should().Contain("Importing from a spreadsheet");
    }

    [Fact]
    public void DescribeHeaderProblem_FlagsAHeaderWithNoNameColumn()
    {
        var problem = CsvImportService.DescribeHeaderProblem("Quantity,Unit\n10,kg");

        problem.Should().NotBeNull();
        problem.Should().Contain("Name");
        problem.Should().Contain("Importing from a spreadsheet");
    }

    [Fact]
    public void DescribeHeaderProblem_FlagsAnEmptyFile()
        => CsvImportService.DescribeHeaderProblem("   ").Should().Be("That file is empty.");

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
