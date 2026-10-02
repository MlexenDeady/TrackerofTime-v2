using TrackerOfTime.V2.M8.Application.Core;

namespace TrackerOfTime.V2.M8_1.Integration;

/// <summary>
/// Owns the canonical M8 settings snapshot for the M8.1 workflow. All requests
/// are projected through M8SettingsTransportAdapter; ConvertSettings responses
/// are merged only into the exact revision that produced the request.
/// </summary>
public sealed class M8SettingsWorkflowCoordinator
{
    private readonly RandomizerWorkflowService _workflow;
    private readonly M8SettingsTransportAdapter _transport = new();
    private readonly M8SettingsNormalizationMerger _merger = new();
    private readonly M8StartingItemsCanonicalizer _startingItems = new();
    private readonly M8SettingsStateEditor _editor = new();
    private readonly M8SeedProvenanceUpdater _seed = new();

    public M8SettingsState State { get; private set; }

    public M8SettingsWorkflowCoordinator(RandomizerWorkflowService workflow, M8SettingsState initialState)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(initialState);
        _workflow = workflow;
        State = initialState;
    }

    public bool SetUserValueIfChanged(string key, M8SettingValue value, string operation = "UserEdit")
    {
        if (State.Values.TryGetValue(key, out var current) && current.UserValue is not null &&
            current.UserValue.Json.GetRawText() == value.Json.GetRawText()) return false;
        State = _editor.SetUserValue(State, key, value, operation);
        return true;
    }

    public M8SettingsTransportSnapshot CreateTransportSnapshot() => _transport.CreateSnapshot(State);

    public async Task<GenerationResult> GenerateAsync(string romPath, string? requestedSeed, int worldCount, TrackerOfTime.V2.M7.Workflow.M7OutputOptions output, CancellationToken ct = default)
    {
        var request = _transport.CreateSnapshot(State);
        var input = new TrackerOfTime.V2.M7.Workflow.M7GenerationInput(romPath, requestedSeed, request.SettingsString, request.Settings, worldCount, output);
        var result = await _workflow.GenerateAsync(input, ct).ConfigureAwait(false);
        var evidence = OoTRGenerationEvidenceReader.Read(result);
        State = _seed.RecordGeneration(State, requestedSeed, evidence.ActualSeed, result.Seed);
        return result;
    }

    public async Task<SettingsConversionResult> ConvertAsync(CancellationToken ct = default)
    {
        var request = _transport.CreateSnapshot(State);
        var requestId = Guid.NewGuid().ToString("N");
        var result = await _workflow.ConvertSettingsAsync(request.SettingsString, request.Settings, ct).ConfigureAwait(false);

        // Apply both views against the same source revision. A concurrent user edit
        // causes both adapters to reject the response rather than overwrite intent.
        var merge = _merger.Apply(State, request.StateRevision, result.Settings, result.SettingsString, requestId, request.Settings.Keys.ToArray());
        State = merge.State;
        var canonical = _startingItems.ApplyConvertResponse(State, request.StateRevision, result.Settings, requestId);
        State = canonical.State;
        return result;
    }
}
