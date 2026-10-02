using System.Text.RegularExpressions;

namespace TrackerOfTime.V2.M8_1.Integration;

public sealed record OoTRGenerationEvidence(string? ActualSeed, string Source, bool OutputFilesAgree);

public static partial class OoTRGenerationEvidenceReader
{
    [GeneratedRegex(@"(?im)^.*OoT Randomizer Version .*? - Seed:\s*([A-Za-z0-9]+)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex SeedLine();

    public static OoTRGenerationEvidence Read(GenerationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var match = SeedLine().Match(result.StandardError ?? string.Empty);
        if (!match.Success) return new OoTRGenerationEvidence(null,"Unavailable",false);
        var seed = match.Groups[1].Value;
        var namedOutputs = result.OutputFiles.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        var agree = namedOutputs.Length > 0 && namedOutputs.All(x => Path.GetFileName(x).Contains(seed,StringComparison.OrdinalIgnoreCase));
        return new OoTRGenerationEvidence(seed,"OriginalOoTRStderr",agree);
    }
}
