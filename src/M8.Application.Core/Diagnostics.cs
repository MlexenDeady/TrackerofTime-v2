namespace TrackerOfTime.V2.M8.Application.Core;

public enum M8DiagnosticLevel { Info, Warning, Error }
public enum M8DiagnosticCategory { Lifecycle, Process, Randomizer, GameCore, Tracker }
public sealed record M8DiagnosticEntry(DateTimeOffset Timestamp, M8DiagnosticLevel Level, M8DiagnosticCategory Category, string Message);

public interface IM8Diagnostics
{
    void Write(M8DiagnosticLevel level, M8DiagnosticCategory category, string message);
    IReadOnlyList<M8DiagnosticEntry> Snapshot();
}

public sealed class InMemoryM8Diagnostics : IM8Diagnostics
{
    private readonly object _sync = new();
    private readonly List<M8DiagnosticEntry> _entries = new();
    public void Write(M8DiagnosticLevel level, M8DiagnosticCategory category, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        lock (_sync) _entries.Add(new(DateTimeOffset.UtcNow, level, category, message));
    }
    public IReadOnlyList<M8DiagnosticEntry> Snapshot()
    {
        lock (_sync) return _entries.ToArray();
    }
}
