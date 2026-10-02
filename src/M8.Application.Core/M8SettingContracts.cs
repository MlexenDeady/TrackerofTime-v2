using System.Text.Json;

namespace TrackerOfTime.V2.M8.Application.Core;

public enum M8SettingSourceKind
{
    Unknown,
    OriginalMetadata,
    UserInput,
    SettingsStringImport,
    FrozenM3ConvertResponse,
    FrozenM3GenerateResponse,
    OriginalOoTROutput
}

public enum M8SettingEvidence
{
    Unknown,
    UserDefined,
    OriginalNormalized,
    OriginalEffective
}

public enum M8NormalizationCompleteness
{
    Partial
}

public sealed record M8SettingValue(JsonElement Json)
{
    public static M8SettingValue From<T>(T value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return new M8SettingValue(document.RootElement.Clone());
    }

    public override string ToString() => Json.GetRawText();
}

public sealed record M8SettingMetadata(
    string Key,
    string OriginalType,
    M8SettingValue? OriginalDefault,
    bool IsSettable,
    bool Shared,
    bool Cosmetic,
    bool SettingsStringEncoded,
    int BitWidth,
    string Classification,
    string? LegacyCanonicalKey = null);

public sealed record M8SettingProvenance(
    M8SettingSourceKind SourceKind,
    string? SourceVersion,
    string SourceSettingKey,
    long StateRevision,
    string Operation,
    string Transformation,
    string TransportPath,
    M8SettingEvidence Evidence,
    string? RequestOrArtifactId = null);

public sealed record M8SettingTransportState(
    long StateRevision,
    string Operation,
    string? RequestId,
    IReadOnlyCollection<string> SentKeys,
    DateTimeOffset RecordedAtUtc);

public sealed record M8SettingEntry(
    string Key,
    M8SettingValue? UserValue,
    M8SettingValue? NormalizedValue,
    M8SettingValue? EffectiveValue,
    M8SettingEvidence Evidence,
    IReadOnlyList<M8SettingProvenance> Provenance,
    M8SettingTransportState? LastTransport = null);

public sealed record M8SettingsStringArtifact(
    string SettingsString,
    long StateRevision,
    bool IsImported,
    string OoTRVersion,
    M8SettingEvidence Evidence);

public sealed record M8PartialNormalizationResult(
    long StateRevision,
    IReadOnlyCollection<string> ReturnedKeys,
    IReadOnlyCollection<string> MissingKeys,
    M8NormalizationCompleteness Completeness,
    string? RequestId);

public sealed record M8SeedProvenance(
    string? RequestedSeed,
    string? ActualOoTRSeed,
    string? FrozenM3ResponseSeed,
    M8SettingEvidence RequestedEvidence = M8SettingEvidence.Unknown,
    M8SettingEvidence ActualEvidence = M8SettingEvidence.Unknown,
    M8SettingEvidence FrozenM3ResponseEvidence = M8SettingEvidence.Unknown);

public sealed record M8CanonicalStartingItemsArtifact(
    M8SettingValue Value,
    long StateRevision,
    string OoTRVersion,
    M8SettingSourceKind SourceKind,
    M8SettingEvidence Evidence,
    IReadOnlyCollection<string> LegacySourceKeys,
    string? RequestId = null);
