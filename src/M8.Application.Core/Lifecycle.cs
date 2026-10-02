namespace TrackerOfTime.V2.M8.Application.Core;

public sealed class M8ApplicationLifecycle
{
    private readonly M8ApplicationStateMachine _state;
    private readonly M8UserDataPaths _paths;
    private readonly IM8Diagnostics _diagnostics;
    private readonly IReadOnlyList<IExternalHostOwner> _owners;

    public M8ApplicationLifecycle(M8ApplicationStateMachine state, M8UserDataPaths paths, IM8Diagnostics diagnostics, IEnumerable<IExternalHostOwner>? owners = null)
    { _state = state; _paths = paths; _diagnostics = diagnostics; _owners = (owners ?? []).ToArray(); }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _paths.EnsureCreated();
        _diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Lifecycle, "M8 application core startup.");
        _state.MarkReady();
        return Task.CompletedTask;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        _state.BeginShutdown();
        _diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Lifecycle, "M8 application shutdown requested.");
        foreach (var owner in _owners.Reverse())
            if (owner.Snapshot.OwnedByM8 && !owner.Snapshot.Exited)
                await owner.RequestShutdownAsync(cancellationToken).ConfigureAwait(false);
    }
}
