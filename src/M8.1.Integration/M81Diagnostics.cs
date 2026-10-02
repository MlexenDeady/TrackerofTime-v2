using System.Text;
using TrackerOfTime.V2.M8.Application.Core;

namespace TrackerOfTime.V2.M8_1.Integration;

/// <summary>M8.1-only diagnostic decorator: full-fidelity session log on disk, compact UI snapshot.</summary>
public sealed class M81SessionDiagnostics : IM8Diagnostics
{
    private readonly IM8Diagnostics inner;
    private readonly object gate = new();
    private readonly string logFile;
    public string LogFile => logFile;

    public M81SessionDiagnostics(IM8Diagnostics inner, string logDirectory)
    {
        this.inner = inner;
        Directory.CreateDirectory(logDirectory);
        logFile = Path.Combine(logDirectory, $"M8.1-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
        File.AppendAllText(logFile, $"# Tracker of Time V2 M8.1 session {DateTimeOffset.Now:O}{Environment.NewLine}", Encoding.UTF8);
    }

    public void Write(M8DiagnosticLevel level, M8DiagnosticCategory category, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        var now = DateTimeOffset.Now;
        lock (gate)
        {
            File.AppendAllText(logFile, $"{now:O} [{level}] [{category}] {message}{Environment.NewLine}", Encoding.UTF8);
        }
        // Keep the UI useful: multiline native/OoTR dumps stay complete on disk, but get one summary line in-memory.
        var compact = message.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        string ui = compact.Length <= 1 ? message : $"{compact[0]}  [full detail: {compact.Length} lines -> {Path.GetFileName(logFile)}]";
        if (ui.Length > 900) ui = ui[..900] + "…";
        inner.Write(level, category, ui);
    }

    public IReadOnlyList<M8DiagnosticEntry> Snapshot() => inner.Snapshot();

    public void Operation(string name, string? detail = null) =>
        Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Lifecycle, $"=== {name}{(string.IsNullOrWhiteSpace(detail) ? "" : " | " + detail)} ===");
}
