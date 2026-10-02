namespace TrackerOfTime.V2.M8.Application.Core;

public sealed class M8SeedProvenanceUpdater
{
    public M8SettingsState RecordGeneration(
        M8SettingsState state,
        string? requestedSeed,
        string? actualOoTRSeed,
        string? frozenM3ResponseSeed)
    {
        ArgumentNullException.ThrowIfNull(state);
        var requested = string.IsNullOrWhiteSpace(requestedSeed) ? null : requestedSeed.Trim();
        var actual = string.IsNullOrWhiteSpace(actualOoTRSeed) ? null : actualOoTRSeed.Trim();
        var frozen = string.IsNullOrWhiteSpace(frozenM3ResponseSeed) ? null : frozenM3ResponseSeed.Trim();
        var seed = new M8SeedProvenance(
            requested,
            actual,
            frozen,
            requested is null ? M8SettingEvidence.Unknown : M8SettingEvidence.UserDefined,
            actual is null ? M8SettingEvidence.Unknown : M8SettingEvidence.OriginalEffective,
            frozen is null ? M8SettingEvidence.Unknown : M8SettingEvidence.OriginalNormalized);
        return new M8SettingsState(state.Revision,state.OoTRVersion,state.Values,state.Metadata,state.SettingsString,state.LastNormalization,seed,state.CanonicalStartingItems);
    }
}
