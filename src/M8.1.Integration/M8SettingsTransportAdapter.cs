using System.Text.Json;
using TrackerOfTime.V2.M8.Application.Core;

namespace TrackerOfTime.V2.M8_1.Integration;

public sealed record M8SettingsTransportSnapshot(
    long StateRevision,
    IReadOnlyDictionary<string, object?> Settings,
    string? SettingsString,
    bool UsesRevisionBoundSettingsString);

/// <summary>
/// Projects an immutable M8 settings-state snapshot onto the existing M7/M3
/// transport shape. This class contains no OoTR rules and opens no transport
/// channel of its own.
/// </summary>
public sealed class M8SettingsTransportAdapter
{
    public M8SettingsTransportSnapshot CreateSnapshot(M8SettingsState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var settings = new Dictionary<string, object?>(state.Values.Count, StringComparer.Ordinal);
        foreach (var pair in state.Values.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            // UserValue is the canonical M8 intent. Normalized/Effective views must
            // never silently replace it when constructing a transport request.
            if (pair.Value.UserValue is null)
                continue;

            settings[pair.Key] = ToTransportValue(pair.Value.UserValue.Json);
        }

        // Original OoTR's transport gives a settings string precedence. Therefore
        // only a string derived from this exact revision may accompany the state.
        // A stale artifact is retained in M8SettingsState for provenance but is
        // deliberately omitted from transport.
        var currentString = state.HasCurrentSettingsString
            ? state.SettingsString!.SettingsString
            : null;

        // Original OoTR 9.1.36 folds the legacy Electron starting_* controls into the
        // canonical starting_items dictionary. After ConvertSettings produced a settings
        // string for this exact revision, passing those three legacy aliases again in the
        // JSON settings file applies the starting inventory twice (notably #AdultTrade).
        // Keep all non-shared/runtime settings in the JSON payload, but let the current
        // Original-OoTR settings string be authoritative for the three legacy aliases.
        if (currentString is not null)
        {
            settings.Remove("starting_equipment");
            settings.Remove("starting_inventory");
            settings.Remove("starting_songs");
            settings.Remove("starting_items");
        }

        return new M8SettingsTransportSnapshot(
            state.Revision,
            settings,
            currentString,
            currentString is not null);
    }

    private static object? ToTransportValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number when value.TryGetInt64(out var integer) => integer,
        JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
        _ => value.Clone()
    };
}
