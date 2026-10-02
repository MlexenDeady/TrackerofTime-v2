using System.Diagnostics;
using System.Text.Json;
using TrackerOfTime.V2.M8.Application.Core;

namespace TrackerOfTime.V2.M8_1.Integration;

/// <summary>
/// Projects the setting schema directly from the pinned Original OoTR SettingsList.py.
/// This deliberately does not duplicate OoTR rules or maintain a hand-written 284-setting table.
/// </summary>
public sealed class OoTRSettingsCatalog
{
    private sealed record RawSetting(string Key, string OriginalType, JsonElement Default, bool IsSettable, bool Shared, bool Cosmetic, int BitWidth);

    private const string ExtractScript = """
import json
from SettingsList import SettingInfos
out=[]
for key, info in SettingInfos.setting_infos.items():
    out.append({
        'key': key,
        'originalType': info.type.__name__,
        'default': info.default,
        'isSettable': info.type is not type(None),
        'shared': bool(info.shared),
        'cosmetic': bool(info.cosmetic),
        'bitWidth': int(info.bitwidth),
    })
print(json.dumps(out, separators=(',', ':')))
""";

    public async Task<M8SettingsState> LoadInitialStateAsync(string repositoryRoot, string ooTRVersion, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(ooTRVersion);
        var ootrRoot = Path.Combine(repositoryRoot, "third_party", "OoTR");
        var settingsList = Path.Combine(ootrRoot, "SettingsList.py");
        if (!File.Exists(settingsList)) throw new FileNotFoundException("Frozen Original OoTR SettingsList.py not found.", settingsList);

        var raw = await ExtractAsync(ootrRoot, ct).ConfigureAwait(false);
        if (raw.Count != 284) throw new InvalidDataException($"Original OoTR catalog definition count changed. Expected=284 Actual={raw.Count}.");
        if (raw.Count(x => x.IsSettable) != 277) throw new InvalidDataException($"Original OoTR settable count changed. Expected=277 Actual={raw.Count(x => x.IsSettable)}.");
        if (raw.Count(x => x.OriginalType == "NoneType") != 7) throw new InvalidDataException("Original OoTR NoneType metadata count changed; expected 7.");

        var metadata = new Dictionary<string, M8SettingMetadata>(StringComparer.Ordinal);
        var values = new Dictionary<string, M8SettingEntry>(StringComparer.Ordinal);
        foreach (var item in raw)
        {
            if (!metadata.TryAdd(item.Key, ToMetadata(item))) throw new InvalidDataException($"Duplicate Original OoTR setting key '{item.Key}'.");
            if (!item.IsSettable) continue;

            var defaultValue = new M8SettingValue(item.Default.Clone());
            var provenance = new M8SettingProvenance(
                M8SettingSourceKind.OriginalMetadata, ooTRVersion, item.Key, 0,
                "CatalogInitialize", "Original OoTR SettingInfo.default projected without rule reimplementation",
                "third_party/OoTR/SettingsList.py -> SettingInfos.setting_infos", M8SettingEvidence.Unknown);
            values.Add(item.Key, new M8SettingEntry(item.Key, defaultValue, null, null, M8SettingEvidence.Unknown, new[] { provenance }));
        }

        return new M8SettingsState(0, ooTRVersion, values, metadata);
    }

    private static M8SettingMetadata ToMetadata(RawSetting item)
    {
        var legacyCanonical = item.Key is "starting_equipment" or "starting_inventory" or "starting_songs" ? "starting_items" : null;
        var classification = !item.IsSettable ? "GUI_METADATA" : item.Cosmetic ? "COSMETIC_SFX" : item.Shared ? "SHARED" : "NON_SHARED";
        return new M8SettingMetadata(item.Key, item.OriginalType, new M8SettingValue(item.Default.Clone()), item.IsSettable,
            item.Shared, item.Cosmetic, item.BitWidth > 0, item.BitWidth, classification, legacyCanonical);
    }

    private static async Task<IReadOnlyList<RawSetting>> ExtractAsync(string ootrRoot, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("py")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = ootrRoot
        };
        psi.ArgumentList.Add("-3.13");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(ExtractScript);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Unable to start Python for Original OoTR settings catalog extraction.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdout = await stdoutTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        if (process.ExitCode != 0) throw new InvalidOperationException($"Original OoTR settings catalog extraction failed (ExitCode={process.ExitCode}): {stderr.Trim()}");
        var result = JsonSerializer.Deserialize<List<RawSetting>>(stdout, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return result ?? throw new InvalidDataException("Original OoTR settings catalog returned no JSON data.");
    }
}
