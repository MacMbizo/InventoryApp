using System.Threading;
using System.Threading.Tasks;

namespace KitchenInventory.Desktop.Services;

public interface IDiagnosticsExporter
{
    /// <summary>
    /// Creates a self-contained diagnostics bundle (.zip) at the specified path.
    /// The bundle includes environment/runtime information, database metadata (redacted),
    /// selected log files, and configuration snippets helpful for support.
    /// </summary>
    /// <param name="outputZipPath">Full path to the output zip file. Parent directory will be created if missing.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ExportAsync(string outputZipPath, CancellationToken ct = default);
}