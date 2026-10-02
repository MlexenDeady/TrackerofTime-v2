namespace TrackerOfTime.V2.M8.Application.Core;

public sealed class M8SettingsState
{
    public long Revision { get; }
    public string OoTRVersion { get; }
    public IReadOnlyDictionary<string, M8SettingEntry> Values { get; }
    public IReadOnlyDictionary<string, M8SettingMetadata> Metadata { get; }
    public M8SettingsStringArtifact? SettingsString { get; }
    public M8PartialNormalizationResult? LastNormalization { get; }
    public M8SeedProvenance Seed { get; }
    public M8CanonicalStartingItemsArtifact? CanonicalStartingItems { get; }

    public M8SettingsState(
        long revision,
        string ooTRVersion,
        IReadOnlyDictionary<string, M8SettingEntry> values,
        IReadOnlyDictionary<string, M8SettingMetadata> metadata,
        M8SettingsStringArtifact? settingsString = null,
        M8PartialNormalizationResult? lastNormalization = null,
        M8SeedProvenance? seed = null,
        M8CanonicalStartingItemsArtifact? canonicalStartingItems = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        ArgumentException.ThrowIfNullOrWhiteSpace(ooTRVersion);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(metadata);

        foreach (var key in values.Keys)
        {
            if (!metadata.TryGetValue(key, out var definition) || !definition.IsSettable)
                throw new ArgumentException($"Value key '{key}' has no settable metadata definition.", nameof(values));
        }

        Revision = revision;
        OoTRVersion = ooTRVersion;
        Values = new Dictionary<string, M8SettingEntry>(values, StringComparer.Ordinal);
        Metadata = new Dictionary<string, M8SettingMetadata>(metadata, StringComparer.Ordinal);
        SettingsString = settingsString;
        LastNormalization = lastNormalization;
        Seed = seed ?? new M8SeedProvenance(null, null, null);
        CanonicalStartingItems = canonicalStartingItems;
    }

    public bool HasCurrentSettingsString => SettingsString is not null && SettingsString.StateRevision == Revision;
    public bool HasCurrentCanonicalStartingItems => CanonicalStartingItems is not null && CanonicalStartingItems.StateRevision == Revision;
}
