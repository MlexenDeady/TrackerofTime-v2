using System.IO.Compression;
using System.Security.Cryptography;
using TrackerOfTime.V2.M8.Application.Core;
using TrackerOfTime.V2.M8_3.Integration;

namespace TrackerOfTime.V2.M8_4.Integration;

/// <summary>
/// M8.4-owned input-provider lifecycle. The pinned Mupen archive is the authoritative
/// Input-SDL baseline; Direct-XInput is installed only for the lifetime of one session.
/// Frozen GameCore/M5/M6 and the proven M8.3 plugin are not modified.
/// </summary>
internal static class M84InputRuntimeManager
{
    internal sealed record Baseline(string InputPath, string BackupPath, string Sha256);

    internal static Baseline RestorePinnedInputSdl(string runtimeRoot, IM8Diagnostics diagnostics)
    {
        var bundle = Path.Combine(runtimeRoot, "bundle");
        var input = Directory.EnumerateFiles(bundle, "*.dll", SearchOption.AllDirectories)
            .Single(x => Path.GetFileName(x).Contains("input", StringComparison.OrdinalIgnoreCase)
                      && Path.GetFileName(x).Contains("sdl", StringComparison.OrdinalIgnoreCase));
        var zipPath = Path.Combine(runtimeRoot, "mupen64plus-bundle-win64-2.6.0.zip");
        if (!File.Exists(zipPath)) throw new FileNotFoundException("Pinned Mupen64Plus archive required to restore Input-SDL baseline.", zipPath);

        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries.Where(e =>
            Path.GetFileName(e.FullName).Contains("input", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(e.FullName).Contains("sdl", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(e.FullName).EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (entries.Length != 1) throw new InvalidDataException($"Pinned Input-SDL baseline: expected exactly one DLL in archive, found {entries.Length}.");

        var temp = Path.Combine(Path.GetTempPath(), "tot-m84-input-" + Guid.NewGuid().ToString("N") + ".dll");
        try
        {
            entries[0].ExtractToFile(temp, true);
            var baselineHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(temp)));
            var currentHash = File.Exists(input) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))) : "MISSING";
            if (!currentHash.Equals(baselineHash, StringComparison.OrdinalIgnoreCase))
                File.Copy(temp, input, true);

            // M83DirectXInputPlugin.Restore uses this backup. Refresh it from the authoritative
            // pinned archive every session so an old/contaminated FIX2L backup cannot survive.
            var backup = input + ".fix2l-original";
            File.Copy(temp, backup, true);
            diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Lifecycle,
                $"M8.4 input baseline ready: provider=Input-SDL; restored={(currentHash.Equals(baselineHash, StringComparison.OrdinalIgnoreCase) ? "no" : "yes")}; sha256={baselineHash}.");
            return new Baseline(input, backup, baselineHash);
        }
        finally { try { File.Delete(temp); } catch { } }
    }

    internal static void VerifyProvider(Baseline baseline, bool directXInput, IM8Diagnostics diagnostics)
    {
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(baseline.InputPath)));
        var isBaseline = hash.Equals(baseline.Sha256, StringComparison.OrdinalIgnoreCase);
        if (directXInput && isBaseline) throw new InvalidOperationException("Direct-XInput was selected but the runtime still contains the pinned Input-SDL provider.");
        if (!directXInput && !isBaseline) throw new InvalidOperationException("Input-SDL was selected but the runtime input DLL does not match the pinned baseline.");
        diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Lifecycle,
            $"M8.4 input provider verified: {(directXInput ? "Direct-XInput" : "Input-SDL")}; sha256={hash}.");
    }
}
