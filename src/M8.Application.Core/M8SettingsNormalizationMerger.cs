using System.Text.Json;

namespace TrackerOfTime.V2.M8.Application.Core;

public sealed record M8NormalizationMergeOutcome(
    M8SettingsState State,
    bool Applied,
    long SourceRevision,
    IReadOnlyCollection<string> AppliedKeys,
    IReadOnlyCollection<string> MissingKeys,
    IReadOnlyCollection<string> UnknownKeys,
    string? RequestId);

public sealed class M8SettingsNormalizationMerger
{
    public M8NormalizationMergeOutcome Apply(
        M8SettingsState state,
        long sourceRevision,
        JsonElement returnedSettings,
        string normalizedSettingsString,
        string? requestId = null,
        IReadOnlyCollection<string>? sentKeys = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceRevision);
        if (returnedSettings.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("ConvertSettings response settings must be a JSON object.", nameof(returnedSettings));

        // A response belongs only to the exact state revision that produced the request.
        // Never allow an older async response to normalize newer user intent.
        if (sourceRevision != state.Revision)
        {
            return new M8NormalizationMergeOutcome(
                state, false, sourceRevision,
                Array.Empty<string>(), state.Values.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                Array.Empty<string>(), requestId);
        }

        var values = new Dictionary<string, M8SettingEntry>(state.Values, StringComparer.Ordinal);
        var applied = new List<string>();
        var unknown = new List<string>();
        var transportKeys = sentKeys?.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray()
            ?? Array.Empty<string>();
        var transport = new M8SettingTransportState(sourceRevision, "ConvertSettings", requestId, transportKeys, DateTimeOffset.UtcNow);

        foreach (var property in returnedSettings.EnumerateObject())
        {
            if (!state.Metadata.TryGetValue(property.Name, out var metadata) || !metadata.IsSettable || !values.TryGetValue(property.Name, out var previous))
            {
                unknown.Add(property.Name);
                continue;
            }

            var normalized = new M8SettingValue(property.Value.Clone());
            var provenance = previous.Provenance.ToList();
            provenance.Add(new M8SettingProvenance(
                M8SettingSourceKind.FrozenM3ConvertResponse,
                state.OoTRVersion,
                property.Name,
                sourceRevision,
                "ConvertSettings",
                "PartialNormalizationMerge",
                "FrozenM3.ConvertSettings.settings",
                M8SettingEvidence.OriginalNormalized,
                requestId));

            // UserValue is canonical user intent and is deliberately not replaced.
            values[property.Name] = previous with
            {
                NormalizedValue = normalized,
                Evidence = M8SettingEvidence.OriginalNormalized,
                Provenance = provenance,
                LastTransport = transport
            };
            applied.Add(property.Name);
        }

        var appliedSet = applied.ToHashSet(StringComparer.Ordinal);
        var missing = state.Values.Keys.Where(k => !appliedSet.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var result = new M8PartialNormalizationResult(
            sourceRevision,
            applied.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            missing,
            M8NormalizationCompleteness.Partial,
            requestId);

        var artifact = new M8SettingsStringArtifact(
            normalizedSettingsString ?? string.Empty,
            sourceRevision,
            false,
            state.OoTRVersion,
            M8SettingEvidence.OriginalNormalized);

        var next = new M8SettingsState(
            state.Revision,
            state.OoTRVersion,
            values,
            state.Metadata,
            artifact,
            result,
            state.Seed,
            state.CanonicalStartingItems);

        return new M8NormalizationMergeOutcome(
            next, true, sourceRevision,
            result.ReturnedKeys, result.MissingKeys,
            unknown.OrderBy(x => x, StringComparer.Ordinal).ToArray(), requestId);
    }
}
