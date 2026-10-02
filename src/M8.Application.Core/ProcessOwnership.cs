namespace TrackerOfTime.V2.M8.Application.Core;

public enum ExternalHostKind { OoTR, GameCore }

public sealed record ProcessOwnershipSnapshot(
    ExternalHostKind Host,
    bool StartedByM8,
    bool OwnedByM8,
    bool ShutdownRequested,
    bool Exited,
    int? ExitCode);

public interface IExternalHostOwner
{
    ExternalHostKind Host { get; }
    ProcessOwnershipSnapshot Snapshot { get; }
    Task StartAsync(CancellationToken cancellationToken = default);
    Task RequestShutdownAsync(CancellationToken cancellationToken = default);
    Task<int?> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
