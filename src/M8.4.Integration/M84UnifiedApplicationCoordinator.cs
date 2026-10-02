using TrackerOfTime.V2.M8.Application.Core;
using TrackerOfTime.V2.M8_1.Integration;
using TrackerOfTime.V2.M8_3.Integration;

namespace TrackerOfTime.V2.M8_4.Integration;

public enum M84RomSource { UserSelected, RandomizerOutput }
public sealed record M84SessionPlan(string RomPath, M84RomSource Source, M84RomInspection Inspection, bool StartTrackerWhenLive);

/// <summary>
/// First M8.4 unified-application boundary. It composes existing M8 services; it does not
/// duplicate Randomizer, GameCore, Frozen M6 or tracker semantics.
/// </summary>
public sealed class M84UnifiedApplicationCoordinator(
    M8ApplicationStateMachine application,
    GameRuntimeService game,
    IM8Diagnostics diagnostics)
{
    public M84SessionPlan PlanUserRom(string romPath)
    {
        var inspection = M84RomProfiles.Inspect(romPath);
        if (!inspection.IsN64Rom) throw new InvalidDataException($"'{romPath}' is not a recognized N64 ROM image.");
        var plan = new M84SessionPlan(romPath, M84RomSource.UserSelected, inspection, inspection.TrackerCapability == M84TrackerCapability.OoTTrackerCandidate);
        diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Lifecycle, $"M8.4 session planned: source={plan.Source}, profile={inspection.GameProfile}, tracker={plan.StartTrackerWhenLive}, rom='{romPath}'.");
        return plan;
    }

    public M84SessionPlan PlanRandomizerOutput(IEnumerable<string> outputFiles)
    {
        var rom = RandomizerWorkflowService.FindLaunchableRom(outputFiles)
            ?? throw new InvalidOperationException("Randomizer output does not contain a launchable N64 ROM.");
        var inspection = M84RomProfiles.Inspect(rom);
        if (!inspection.IsN64Rom) throw new InvalidDataException("Randomizer output is not a recognized N64 ROM image.");
        var plan = new M84SessionPlan(rom, M84RomSource.RandomizerOutput, inspection, true);
        diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Lifecycle, $"M8.4 randomizer session planned: profile={inspection.GameProfile}, rom='{rom}'. Runtime M8.3 readiness remains authoritative.");
        return plan;
    }

    public async Task StartGameAsync(M84SessionPlan plan, string repositoryRoot, string runtimePath, string renderer, CancellationToken ct = default)
    {
        application.Transition(M8ApplicationState.GameCoreStarting, "Starting GameCore");
        await game.StartHostAsync(repositoryRoot, runtimePath, renderer, ct).ConfigureAwait(false);
        application.Transition(M8ApplicationState.GameLoading, "Loading ROM");
        await game.LoadAndStartAsync(plan.RomPath, ct).ConfigureAwait(false);
        application.Transition(M8ApplicationState.GameRunning, plan.StartTrackerWhenLive ? "Game running; tracker candidate" : "Game running; emulator-only");
    }

    public async Task<M83TrackerFrame> CaptureTrackerAsync(M84SessionPlan plan, CancellationToken ct = default)
    {
        if (!plan.StartTrackerWhenLive) throw new InvalidOperationException("This ROM is running in emulator-only mode; no tracker profile is available.");
        application.Transition(M8ApplicationState.TrackerStarting, "Starting tracker");
        var frame = await new M83TrackerBridge(game).CaptureAsync(ct).ConfigureAwait(false);
        application.Transition(frame.Readiness == M83SnapshotReadiness.Live ? M8ApplicationState.TrackerActive : M8ApplicationState.GameRunning,
            frame.Readiness == M83SnapshotReadiness.Live ? "Tracker active" : "Waiting for playable save");
        return frame;
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        application.Transition(M8ApplicationState.StoppingGame, "Stopping game");
        await game.StopAsync(ct).ConfigureAwait(false);
        application.MarkReady();
    }
}
