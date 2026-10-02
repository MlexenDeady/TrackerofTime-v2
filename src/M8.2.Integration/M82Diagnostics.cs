using System.Text;
using TrackerOfTime.V2.M8.Application.Core;

namespace TrackerOfTime.V2.M8_2.Integration;

public sealed class M82SessionDiagnostics : IM8Diagnostics
{
    private readonly IM8Diagnostics inner;
    private readonly object gate = new();
    public string LogFile { get; }

    public M82SessionDiagnostics(IM8Diagnostics inner, string logDirectory, string renderer)
    {
        this.inner = inner;
        Directory.CreateDirectory(logDirectory);
        var safeRenderer = string.Concat(renderer.Where(char.IsLetterOrDigit));
        LogFile = Path.Combine(logDirectory, $"M8.2-GameCore-{safeRenderer}-{DateTime.Now:yyyyMMdd-HHmmss}-{Environment.ProcessId}.log");
        File.AppendAllText(LogFile, $"# Tracker of Time V2 M8.2 GameCore diagnostic {DateTimeOffset.Now:O}{Environment.NewLine}", Encoding.UTF8);
    }

    public void Write(M8DiagnosticLevel level, M8DiagnosticCategory category, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        var line = $"{DateTimeOffset.Now:O} [{level}] [{category}] {message}";
        lock (gate) File.AppendAllText(LogFile, line + Environment.NewLine, Encoding.UTF8);
        inner.Write(level, category, message);
        Console.WriteLine(line);
    }

    public IReadOnlyList<M8DiagnosticEntry> Snapshot() => inner.Snapshot();
}
