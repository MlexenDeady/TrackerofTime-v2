namespace TrackerOfTime.V2.M8.Application.Core;

public sealed class M8SettingsStateEditor
{
    public M8SettingsState SetUserValue(M8SettingsState state, string key, M8SettingValue value, string operation = "UserEdit")
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        if (!state.Metadata.TryGetValue(key, out var metadata) || !metadata.IsSettable)
            throw new ArgumentException($"Unknown or non-settable OoTR setting '{key}'.", nameof(key));

        var nextRevision = checked(state.Revision + 1);
        var values = new Dictionary<string, M8SettingEntry>(state.Values, StringComparer.Ordinal);
        values.TryGetValue(key, out var previous);
        var provenance = previous?.Provenance.ToList() ?? new List<M8SettingProvenance>();
        provenance.Add(new M8SettingProvenance(
            M8SettingSourceKind.UserInput,
            state.OoTRVersion,
            key,
            nextRevision,
            operation,
            "SetUserValue",
            "M8SettingsStateEditor",
            M8SettingEvidence.UserDefined));

        values[key] = new M8SettingEntry(
            key,
            value,
            null,
            null,
            M8SettingEvidence.UserDefined,
            provenance);

        // A user edit invalidates revision-bound normalized/effective views. The old
        // settings-string artifact may remain for provenance, but HasCurrentSettingsString
        // becomes false because its revision no longer matches.
        return new M8SettingsState(
            nextRevision,
            state.OoTRVersion,
            values,
            state.Metadata,
            state.SettingsString,
            null,
            state.Seed,
            state.CanonicalStartingItems);
    }
}
