using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using KitchenInventory.Desktop.Services;
using Xunit;

namespace KitchenInventory.Desktop.Tests;

public class ReportExporterTests
{
    private static ReportTable Table(params string[][] rows)
        => new("Stock on Hand", new[] { "Id", "Name", "Quantity" }, rows.Select(r => (IReadOnlyList<string>)r).ToList());

    [Fact]
    public void ToCsv_WritesHeaderAndRows()
    {
        var csv = ReportExporter.ToCsv(Table(
            new[] { "1", "Rice", "10" },
            new[] { "2", "Milk", "0.5" }));

        var lines = csv.TrimEnd('\n').Split('\n');
        Assert.Equal("Id,Name,Quantity", lines[0]);
        Assert.Equal("1,Rice,10", lines[1]);
        Assert.Equal("2,Milk,0.5", lines[2]);
    }

    [Fact]
    public void ToCsv_EscapesCommasQuotesAndNewlines()
    {
        var csv = ReportExporter.ToCsv(Table(
            new[] { "1", "Rice, white", "10" },
            new[] { "2", "Say \"hi\"", "1" },
            new[] { "3", "Two\nlines", "2" }));

        Assert.Contains("\"Rice, white\"", csv);
        Assert.Contains("\"Say \"\"hi\"\"\"", csv);
        Assert.Contains("\"Two\nlines\"", csv);
    }

    [Fact]
    public void ToCsv_HandlesShortAndMissingRows()
    {
        var table = new ReportTable("T", new[] { "A", "B", "C" },
            new List<IReadOnlyList<string>> { new[] { "1" }, Array.Empty<string>() });

        var csv = ReportExporter.ToCsv(table).TrimEnd('\n').Split('\n');

        Assert.Equal("A,B,C", csv[0]);
        Assert.Equal("1,,", csv[1]);
        Assert.Equal(",,", csv[2]);
    }

    [Fact]
    public void ToCsv_NullTable_Throws()
        => Assert.Throws<ArgumentNullException>(() => ReportExporter.ToCsv(null!));

    [Fact]
    public void ToPlainText_IncludesTitleRowsAndCount()
    {
        var text = ReportExporter.ToPlainText(Table(new[] { "1", "Rice", "10" }));

        Assert.StartsWith("Stock on Hand", text);
        Assert.Contains("Id", text);
        Assert.Contains("Rice", text);
        Assert.Contains("1 row", text);
    }

    [Fact]
    public void ToPlainText_UsesPluralRowLabel()
    {
        var text = ReportExporter.ToPlainText(Table(new[] { "1", "Rice", "10" }, new[] { "2", "Milk", "0" }));
        Assert.Contains("2 rows", text);
    }

    [Fact]
    public void ToPlainText_RespectsMaxWidthByTruncating()
    {
        var longCell = new string('x', 200);
        var table = new ReportTable("Wide", new[] { "Id", "Name" },
            new List<IReadOnlyList<string>> { new[] { "1", longCell } });

        var text = ReportExporter.ToPlainText(table, 40);

        foreach (var line in text.Split('\n'))
        {
            Assert.True(line.Length <= 40, $"Line exceeded max width: '{line}'");
        }
        Assert.Contains("...", text);
    }

    [Fact]
    public void ToPlainText_NullTable_Throws()
        => Assert.Throws<ArgumentNullException>(() => ReportExporter.ToPlainText(null!));

    [Fact]
    public void ToPdf_NullTable_Throws()
        => Assert.Throws<ArgumentNullException>(() => ReportExporter.ToPdf(null!));

    [Fact]
    public void ToPdf_ProducesWellFormedDocument()
    {
        var bytes = ReportExporter.ToPdf(Table(
            new[] { "1", "Rice", "10" },
            new[] { "2", "Milk (low)", "0.5" }));

        var text = Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", text);
        Assert.Contains("/Type /Catalog", text);
        Assert.Contains("Stock on Hand", text);
        Assert.EndsWith("%%EOF\n", text);

        // startxref must point at the cross-reference table.
        var startXref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        Assert.True(startXref > 0);
        var offset = ParseLeadingInt(text.Substring(startXref + "startxref".Length));
        Assert.Equal("xref", text.Substring(offset, 4));

        // Every cross-reference entry must point at the matching object header.
        var entries = Regex.Matches(text.Substring(offset), @"(?m)^(\d{10}) 00000 n $");
        Assert.True(entries.Count >= 6);
        for (var i = 0; i < entries.Count; i++)
        {
            var objectOffset = int.Parse(entries[i].Groups[1].Value);
            Assert.StartsWith($"{i + 1} 0 obj", text.Substring(objectOffset));
        }
    }

    [Fact]
    public void ToPdf_EmptyTable_StillProducesOnePage()
    {
        var text = Encoding.Latin1.GetString(ReportExporter.ToPdf(new ReportTable("Empty", new[] { "Id" }, Array.Empty<IReadOnlyList<string>>())));
        Assert.Contains("/Count 1", text);
        Assert.Contains("0 rows", text);
    }

    [Fact]
    public void ToPdf_LongReport_Paginates()
    {
        var rows = Enumerable.Range(1, 200)
            .Select(i => (IReadOnlyList<string>)new[] { i.ToString(), $"Item {i}", "1" })
            .ToList();
        var text = Encoding.Latin1.GetString(ReportExporter.ToPdf(new ReportTable("Long", new[] { "Id", "Name", "Quantity" }, rows)));

        var pages = Regex.Matches(text, "/Type /Page /Parent").Count;
        Assert.True(pages > 1, $"Expected multiple pages, got {pages}.");
    }

    [Fact]
    public void ToPdf_EscapesPdfSpecialCharacters_AndNonLatin1()
    {
        var text = Encoding.Latin1.GetString(ReportExporter.ToPdf(Table(new[] { "1", "Na(me)\\with)chars \u20ac", "1" })));

        Assert.Contains("Na\\(me\\)\\\\with\\)chars ?", text);
        Assert.DoesNotContain("Na(me)", text);
    }

    private static int ParseLeadingInt(string value)
    {
        var digits = new string(value.TrimStart().TakeWhile(char.IsDigit).ToArray());
        return int.Parse(digits);
    }
}
