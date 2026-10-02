using System.Text.Json;

namespace TrackerOfTime.V2.M8.Application.Core;

public sealed record M8StartingItemsCanonicalizationOutcome(
    M8SettingsState State,
    bool Applied,
    long SourceRevision,
    string? RequestId);

/// <summary>
/// Captures Original OoTR's canonical starting_items projection from the existing
/// ConvertSettings response. It does not reproduce StartingItems mappings in C#.
/// The legacy GUI settings remain transport inputs; the canonical artifact is the
/// single normalized semantic view for their combined meaning.
/// </summary>
public sealed class M8StartingItemsCanonicalizer
{
    private static readonly string[] LegacyKeys =
        ["starting_equipment", "starting_inventory", "starting_songs"];

    public M8StartingItemsCanonicalizationOutcome ApplyConvertResponse(
        M8SettingsState state,
        long sourceRevision,
        JsonElement returnedSettings,
        string? requestId = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceRevision);
        if (returnedSettings.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("ConvertSettings response settings must be a JSON object.", nameof(returnedSettings));

        if (sourceRevision != state.Revision)
            return new(state, false, sourceRevision, requestId);

        // Original OoTR 9.1.36 defines starting_items as a shared SettingInfoDict.
        // It is therefore one of the 277 settable definitions, even though it is
        // not a normal GUI control. Do not infer settable-ness from GUI visibility.
        if (!state.Metadata.TryGetValue("starting_items", out var canonicalMetadata)
            || !canonicalMetadata.IsSettable
            || canonicalMetadata.OriginalType != "dict"
            || !canonicalMetadata.Shared)
            throw new InvalidOperationException("Original OoTR starting_items metadata contract is unavailable or changed unexpectedly.");

        foreach (var key in LegacyKeys)
        {
            if (!state.Metadata.TryGetValue(key, out var metadata) || metadata.LegacyCanonicalKey != "starting_items" || !metadata.IsSettable)
                throw new InvalidOperationException($"Original OoTR legacy starting-item alias contract is invalid for '{key}'.");
        }

        if (!returnedSettings.TryGetProperty("starting_items", out var canonical) || canonical.ValueKind != JsonValueKind.Object)
            return new(state, false, sourceRevision, requestId);

        var values = new Dictionary<string, M8SettingEntry>(state.Values, StringComparer.Ordinal);
        foreach (var key in LegacyKeys)
        {
            if (!values.TryGetValue(key, out var previous)) continue;
            var provenance = previous.Provenance.ToList();
            provenance.Add(new M8SettingProvenance(
                M8SettingSourceKind.FrozenM3ConvertResponse,
                state.OoTRVersion,
                key,
                sourceRevision,
                "ConvertSettings",
                "LegacyStartingAliasNormalizedToOriginalOoTRStartingItems",
                "FrozenM3.ConvertSettings.settings.starting_items",
                M8SettingEvidence.OriginalNormalized,
                requestId));
            values[key] = previous with { Provenance = provenance };
        }

        var artifact = new M8CanonicalStartingItemsArtifact(
            new M8SettingValue(canonical.Clone()),
            sourceRevision,
            state.OoTRVersion,
            M8SettingSourceKind.FrozenM3ConvertResponse,
            M8SettingEvidence.OriginalNormalized,
            LegacyKeys,
            requestId);

        var next = new M8SettingsState(
            state.Revision,
            state.OoTRVersion,
            values,
            state.Metadata,
            state.SettingsString,
            state.LastNormalization,
            state.Seed,
            artifact);

        return new(next, true, sourceRevision, requestId);
    }
}
