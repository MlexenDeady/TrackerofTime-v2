namespace TrackerOfTime.V2.M8.Application.Core;

public enum M8ApplicationState
{
    Starting,
    Ready,
    RandomizerActive,
    GameCoreStarting,
    GameLoading,
    GameRunning,
    TrackerStarting,
    TrackerActive,
    StoppingGame,
    Recovering,
    ApplicationError,
    ShuttingDown
}

public sealed class M8ApplicationStateMachine
{
    public M8ApplicationState State { get; private set; } = M8ApplicationState.Starting;
    public string? Status { get; private set; } = "Starting";
    public string? Error { get; private set; }

    public void MarkReady() => Transition(M8ApplicationState.Ready, "Ready");
    public void BeginShutdown() => Transition(M8ApplicationState.ShuttingDown, "Shutting down");
    public void Fail(string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(error);
        State = M8ApplicationState.ApplicationError;
        Status = "Application error";
        Error = error;
    }

    public void Transition(M8ApplicationState state, string? status = null)
    {
        State = state;
        Status = status;
        Error = null;
    }
}
