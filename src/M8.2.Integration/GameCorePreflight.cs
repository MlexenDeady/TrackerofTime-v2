namespace TrackerOfTime.V2.M8_2.Integration;

public sealed record GameCorePreflightResult(
    string Renderer,
    string RuntimeRoot,
    string BundleRoot,
    string CorePath,
    string VideoPluginPath,
    string AudioPluginPath,
    string InputPluginPath,
    string RspPluginPath,
    IReadOnlyList<string> RequiredConfigFiles);

public static class GameCorePreflight
{
    public static readonly string[] SupportedRenderers = ["Rice", "Glide64mk2"];

    public static GameCorePreflightResult Validate(string runtimeRoot, string renderer)
    {
        if (string.IsNullOrWhiteSpace(runtimeRoot) || !Directory.Exists(runtimeRoot))
            throw new DirectoryNotFoundException($"GameCore runtime not found: {runtimeRoot}");
        if (!SupportedRenderers.Contains(renderer, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported renderer '{renderer}'. Expected Rice or Glide64mk2.");

        renderer = renderer.Equals("Rice", StringComparison.OrdinalIgnoreCase) ? "Rice" : "Glide64mk2";
        var bundle = Path.Combine(runtimeRoot, "bundle");
        if (!Directory.Exists(bundle)) throw new DirectoryNotFoundException($"Mupen bundle not found: {bundle}");
        var files = Directory.GetFiles(bundle, "*", SearchOption.AllDirectories);
        string One(Func<string, bool> predicate, string label)
        {
            var matches = files.Where(x => predicate(Path.GetFileName(x))).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException($"{label}: expected exactly one matching DLL, found {matches.Length}.");
            return matches[0];
        }

        var core = One(n => n.Equals("mupen64plus.dll", StringComparison.OrdinalIgnoreCase), "Core");
        var video = renderer == "Rice"
            ? One(n => n.Contains("rice", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), "Rice")
            : One(n => n.Contains("glide64", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), "Glide64mk2");
        var audio = One(n => n.Contains("audio", StringComparison.OrdinalIgnoreCase) && n.Contains("sdl", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), "AudioSDL");
        var input = One(n => n.Contains("input", StringComparison.OrdinalIgnoreCase) && n.Contains("sdl", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), "InputSDL");
        var rsp = One(n => n.Contains("rsp", StringComparison.OrdinalIgnoreCase) && n.Contains("hle", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), "RspHLE");

        var required = new[] { "mupen64plus.ini", "InputAutoCfg.ini", renderer == "Rice" ? "RiceVideoLinux.ini" : "Glide64mk2.ini" };
        var roots = new[] { Path.Combine(runtimeRoot, "shared"), Path.Combine(runtimeRoot, "config"), bundle };
        foreach (var name in required)
            if (!roots.Any(root => Directory.Exists(root) && Directory.EnumerateFiles(root, name, SearchOption.AllDirectories).Any()))
                throw new FileNotFoundException($"Renderer/runtime preflight missing required data file: {name}");

        return new(renderer, runtimeRoot, bundle, core, video, audio, input, rsp, required);
    }
}

public sealed record RendererAttempt(string Renderer, bool Started, string? Failure);

public static class RendererFallbackPolicy
{
    // M8.2 policy only: deterministic order. It does not hide failures and does not mutate Frozen GameCore.
    public static IReadOnlyList<string> Order(string requested)
    {
        if (requested.Equals("Rice", StringComparison.OrdinalIgnoreCase)) return ["Rice", "Glide64mk2"];
        if (requested.Equals("Glide64mk2", StringComparison.OrdinalIgnoreCase)) return ["Glide64mk2", "Rice"];
        throw new InvalidOperationException($"Unsupported renderer '{requested}'.");
    }

    public static bool ShouldTryFallback(RendererAttempt first)
        => !first.Started && Supported(first.Renderer);

    private static bool Supported(string renderer)
        => GameCorePreflight.SupportedRenderers.Contains(renderer, StringComparer.OrdinalIgnoreCase);
}
