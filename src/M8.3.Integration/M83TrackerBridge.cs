using TrackerOfTime.V2.M6.SnapshotAdapter;
using TrackerOfTime.V2.M8_1.Integration;

namespace TrackerOfTime.V2.M8_3.Integration;

public enum M83SnapshotReadiness : byte { Unavailable, GameNotLoaded, Live }

public sealed record M83TrackerFrame(
    DateTimeOffset CapturedAt,
    M83SnapshotReadiness Readiness,
    GameSnapshotAdapterResult FrozenSnapshot,
    string Source);

/// <summary>
/// M8.3 boundary over the frozen M6 GameSnapshotAdapter. This class deliberately does not
/// reinterpret inventory/check/MQ/ER/map semantics. Frozen evidence and Unknown/Partial states
/// remain authoritative and are passed through unchanged.
/// </summary>
public sealed class M83TrackerBridge(GameRuntimeService game)
{
    public async Task<M83TrackerFrame> CaptureAsync(CancellationToken ct = default)
    {
        var snapshot = await game.CaptureSnapshotAsync(ct).ConfigureAwait(false);
        var readiness = snapshot.Game.Value.IsLoaded ? M83SnapshotReadiness.Live : M83SnapshotReadiness.GameNotLoaded;
        return new M83TrackerFrame(snapshot.CapturedAt, readiness, snapshot, "Frozen M6 GameSnapshotAdapter via M8 GameRuntimeService");
    }
}
