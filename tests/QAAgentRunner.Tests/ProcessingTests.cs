using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using QAAgentRunner.Agent;
using QAAgentRunner.Models;
using QAAgentRunner.Services;
using Xunit;

namespace QAAgentRunner.Tests;

public class ProcessingTests
{
    private static string FindRepoRoot()
    {
        var current = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && current != null; i++)
        {
            if (Directory.Exists(Path.Combine(current, "project-documentation")))
                return current;
            current = Directory.GetParent(current)?.FullName!;
        }
        throw new DirectoryNotFoundException("Could not locate repository root containing project-documentation folder.");
    }

    [Fact]
    public void SuccessfulProcessing_GeneratesQAOutput()
    {
        var root = FindRepoRoot();
        var spec = new InputSpec(
            Path.Combine(root, "project-documentation", "architecture-output.md"),
            Path.Combine(root, "project-documentation", "product-manager-output.md"),
            Path.Combine(root, "Features.md"),
            Path.Combine(root, "Featuresv1.md")
        );
        var output = Path.Combine(root, "project-documentation", "qa-output.md");

        var processor = new QAAgentProcessor(new FileSystemService());
        processor.GenerateReport(spec, output);

        File.Exists(output).Should().BeTrue("the QA agent should generate an output report");
        var text = File.ReadAllText(output);
        text.Should().Contain("QA Agent Output");
        text.Should().ContainAny(new[] {"ExpiringSoonDays = 7", "ExpiringSoonDays=7"});
        text.Should().Contain("LowStockThreshold");
        text.Should().Contain("Windows");
    }

    [Fact]
    public void InputValidation_MissingFile_Throws()
    {
        var root = FindRepoRoot();
        var spec = new InputSpec(
            Path.Combine(root, "project-documentation", "architecture-output.md"),
            Path.Combine(root, "project-documentation", "product-manager-output.md"),
            Path.Combine(root, "Features.md"),
            Path.Combine(root, "DOES_NOT_EXIST_Featuresv1.md")
        );
        var processor = new QAAgentProcessor(new FileSystemService());
        Action act = () => processor.GenerateReport(spec, Path.Combine(root, "project-documentation", "qa-output.md"));
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void ContentParsing_HeadingsDetected()
    {
        var root = FindRepoRoot();
        var archPath = Path.Combine(root, "project-documentation", "architecture-output.md");
        var pmPath = Path.Combine(root, "project-documentation", "product-manager-output.md");
        var arch = File.ReadAllText(archPath);
        var pm = File.ReadAllText(pmPath);

        var archHeads = QAAgentProcessor.ExtractHeadings(arch);
        var pmHeads = QAAgentProcessor.ExtractHeadings(pm);

        archHeads.Should().Contain(h => h.StartsWith("Executive Summary"));
        pmHeads.Should().Contain(h => h.StartsWith("Executive Summary"));
    }

    [Fact]
    public void Idempotent_WritesOutputSafely()
    {
        var root = FindRepoRoot();
        var spec = new InputSpec(
            Path.Combine(root, "project-documentation", "architecture-output.md"),
            Path.Combine(root, "project-documentation", "product-manager-output.md"),
            Path.Combine(root, "Features.md"),
            Path.Combine(root, "Featuresv1.md")
        );
        var output = Path.Combine(root, "project-documentation", "qa-output.md");
        var processor = new QAAgentProcessor(new FileSystemService());
        processor.GenerateReport(spec, output);
        processor.GenerateReport(spec, output); // run twice

        var text = File.ReadAllText(output);
        text.Split(Environment.NewLine).Count(l => l.StartsWith("# QA Agent Output")).Should().Be(1);
    }

    [Fact]
    public void Integration_CISectionPresent()
    {
        var root = FindRepoRoot();
        var spec = new InputSpec(
            Path.Combine(root, "project-documentation", "architecture-output.md"),
            Path.Combine(root, "project-documentation", "product-manager-output.md"),
            Path.Combine(root, "Features.md"),
            Path.Combine(root, "Featuresv1.md")
        );
        var output = Path.Combine(root, "project-documentation", "qa-output.md");
        var processor = new QAAgentProcessor(new FileSystemService());
        processor.GenerateReport(spec, output);
        var text = File.ReadAllText(output);
        text.Should().Contain("CI/CD").And.Contain("GitHub Actions");
    }
}