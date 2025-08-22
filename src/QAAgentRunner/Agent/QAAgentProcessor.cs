using System.Text;
using QAAgentRunner.Models;
using QAAgentRunner.Services;

namespace QAAgentRunner.Agent;

public class QAAgentProcessor
{
    private readonly FileSystemService _fs;

    public QAAgentProcessor(FileSystemService fs)
    {
        _fs = fs;
    }

    public void GenerateReport(InputSpec spec, string outputPath)
    {
        // Validate inputs
        _fs.EnsureFileExists(spec.ArchitecturePath);
        _fs.EnsureFileExists(spec.ProductManagerPath);
        _fs.EnsureFileExists(spec.FeaturesPath);
        _fs.EnsureFileExists(spec.FeaturesV1Path);

        string architecture = _fs.ReadAllText(spec.ArchitecturePath);
        string pm = _fs.ReadAllText(spec.ProductManagerPath);
        string features = _fs.ReadAllText(spec.FeaturesPath);
        string featuresV1 = _fs.ReadAllText(spec.FeaturesV1Path);

        // Extract high-value signals
        bool hasLowStock5 = pm.Contains("LowStockThreshold = 5") || pm.Contains("LowStockThreshold=5") || pm.Contains("LowStockThreshold defaults to 5");
        bool hasExpiring7 = pm.Contains("ExpiringSoonDays = 7") || pm.Contains("ExpiringSoonDays=7") || pm.Contains("ExpiringSoonDays default is 7");
        bool windowsFirst = pm.Contains("Windows first") || architecture.Contains("Windows rollout first");

        var archHeadings = ExtractHeadings(architecture);
        var pmHeadings = ExtractHeadings(pm);

        var sb = new StringBuilder();
        sb.AppendLine("# QA Agent Output — TDD Run");
        sb.AppendLine();
        sb.AppendLine("## Inputs Processed");
        sb.AppendLine($"- Architecture: {spec.ArchitecturePath}");
        sb.AppendLine($"- Product Manager: {spec.ProductManagerPath}");
        sb.AppendLine($"- Features: {spec.FeaturesPath}");
        sb.AppendLine($"- Features v1: {spec.FeaturesV1Path}");
        sb.AppendLine();
        sb.AppendLine("## Extracted Decisions & Signals");
        sb.AppendLine($"- LowStockThreshold default to 5: {(hasLowStock5 ? "FOUND" : "NOT FOUND")}");
        sb.AppendLine($"- ExpiringSoonDays = 7: {(hasExpiring7 ? "FOUND" : "NOT FOUND")}");
        sb.AppendLine($"- Rollout Order Windows first: {(windowsFirst ? "FOUND" : "NOT FOUND")}");
        sb.AppendLine();
        sb.AppendLine("## Detected Sections (Headings)");
        sb.AppendLine("- Architecture: " + string.Join(", ", archHeadings.Take(5)) + (archHeadings.Count > 5 ? ", ..." : string.Empty));
        sb.AppendLine("- Product Manager: " + string.Join(", ", pmHeadings.Take(5)) + (pmHeadings.Count > 5 ? ", ..." : string.Empty));
        sb.AppendLine();
        sb.AppendLine("## QA Test Plan (Derived)");
        sb.AppendLine("- Backend Context: Validate services (InventoryService, CategoryService, NotificationService, AuthService, SettingsService, BackupService, AuditService) with unit tests; enforce validation rules (non-negative quantities, unique category names, password policy, lockout).\n- Frontend Context: ViewModel tests for Inventory, ItemDetail, NeedsAttention, Users, Categories; search/filter behavior; sorting; visual cues logic.\n- End-to-End Context: Simulate user journeys: login -> inventory -> needs attention -> dismiss -> audit trail; admin user/category management; backup/restore flow.");
        sb.AppendLine();
        sb.AppendLine("## TDD Stages");
        sb.AppendLine("1. Red: Write failing tests that assert outputs include decisions and sections above.\n2. Green: Implement minimal parsing and report generation (this stage).\n3. Refactor: Improve parsers and structure without changing behavior; keep tests passing.");
        sb.AppendLine();
        sb.AppendLine("## CI/CD Enforcement");
        sb.AppendLine("- dotnet test runs on every push and PR via GitHub Actions.\n- Tests must pass for merge; coverage can be added later.");

        _fs.WriteAllText(outputPath, sb.ToString());
    }

    public static List<string> ExtractHeadings(string markdown)
    {
        var lines = markdown.Split(new[] {"\r\n", "\n"}, StringSplitOptions.None);
        var heads = new List<string>();
        foreach (var line in lines)
        {
            if (line.StartsWith("# ") || line.StartsWith("## "))
            {
                var h = line.TrimStart('#', ' ').Trim();
                if (!string.IsNullOrWhiteSpace(h)) heads.Add(h);
            }
        }
        return heads;
    }
}