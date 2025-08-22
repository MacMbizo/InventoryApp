using System;
using QAAgentRunner.Agent;
using QAAgentRunner.Models;
using QAAgentRunner.Services;

namespace QAAgentRunner;

public class Program
{
    public static int Main(string[] args)
    {
        try
        {
            // Default inputs to repository-standard paths if not provided
            string repoRoot = AppContext.BaseDirectory;
            // Attempt to locate the repo root by walking up until project-documentation exists
            string? current = repoRoot;
            for (int i = 0; i < 6 && current != null; i++)
            {
                if (Directory.Exists(Path.Combine(current, "project-documentation")))
                {
                    repoRoot = current;
                    break;
                }
                current = Directory.GetParent(current)?.FullName;
            }

            var spec = args.Length >= 4
                ? new InputSpec(args[0], args[1], args[2], args[3])
                : new InputSpec(
                    Path.Combine(repoRoot, "project-documentation", "architecture-output.md"),
                    Path.Combine(repoRoot, "project-documentation", "product-manager-output.md"),
                    Path.Combine(repoRoot, "Features.md"),
                    Path.Combine(repoRoot, "Featuresv1.md"));

            string outputPath = args.Length >= 5
                ? args[4]
                : Path.Combine(repoRoot, "project-documentation", "qa-output.md");

            var processor = new QAAgentProcessor(new FileSystemService());
            processor.GenerateReport(spec, outputPath);

            Console.WriteLine($"QA Agent processing complete. Output: {outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}