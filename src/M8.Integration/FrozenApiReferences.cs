using TrackerOfTime.V2.GameCore.Client;
using TrackerOfTime.V2.M6.SnapshotAdapter;
using TrackerOfTime.V2.M7.Workflow;

namespace TrackerOfTime.V2.M8.Integration;

public static class FrozenApiReferences
{
    public static Type M7WorkflowType => typeof(M7WorkflowRules);
    public static Type GameCoreClientType => typeof(GameCoreClient);
    public static Type SnapshotAdapterType => typeof(GameSnapshotAdapter);
}
