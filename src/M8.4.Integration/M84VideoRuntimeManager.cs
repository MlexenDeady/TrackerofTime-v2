using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TrackerOfTime.V2.M8.Application.Core;

namespace TrackerOfTime.V2.M8_4.Integration;

/// <summary>Installs optional x64 Mupen64Plus video plugins into the verified runtime on demand.</summary>
internal static class M84VideoRuntimeManager
{
    private const string GlideN64Url = "https://nightly.link/gonetz/GLideN64/workflows/build/master/GLideN64-41c7ba2-Windows-Mupen64Plus-CLI-x64.zip";
    private const string GlideN64Sha256 = "6A01E4EFFC82B511EEBB80108DDC1E713AB354E4CB7C5A886918CA0285393387";
    private const string AngrylionReleaseApi = "https://api.github.com/repos/ata4/angrylion-rdp-plus/releases/tags/v1.6";
    private const string AngrylionAssetName = "mupen64plus-video-angrylion-plus.dll";

    internal static async Task PrepareSelectedRendererAsync(string runtimeRoot, string renderer, IM8Diagnostics diagnostics, CancellationToken ct=default)
    {
        if (renderer.Equals("GLideN64", StringComparison.OrdinalIgnoreCase))
            await EnsureGlideN64Async(runtimeRoot, diagnostics, ct).ConfigureAwait(false);
        else if (renderer.Equals("Angrylion", StringComparison.OrdinalIgnoreCase))
            await EnsureAngrylionAsync(runtimeRoot, diagnostics, ct).ConfigureAwait(false);
    }

    private static async Task EnsureGlideN64Async(string runtimeRoot, IM8Diagnostics diagnostics, CancellationToken ct)
    {
        var bundle = Path.Combine(runtimeRoot, "bundle");
        if (FindExactlyOne(bundle, n => n.Contains("gliden64", StringComparison.OrdinalIgnoreCase) && !n.Contains("glide64mk2", StringComparison.OrdinalIgnoreCase)) is not null) return;
        var tempZip = Path.Combine(Path.GetTempPath(), "tot-gliden64-" + Guid.NewGuid().ToString("N") + ".zip");
        var tempDir = Path.Combine(Path.GetTempPath(), "tot-gliden64-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var http = NewHttp();
            await DownloadAsync(http, GlideN64Url, tempZip, ct).ConfigureAwait(false);
            VerifySha256(tempZip, GlideN64Sha256, "GLideN64 x64 artifact");
            ZipFile.ExtractToDirectory(tempZip, tempDir);
            var dlls = Directory.GetFiles(tempDir, "*.dll", SearchOption.AllDirectories)
                .Where(x => Path.GetFileName(x).Contains("gliden64", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (dlls.Length != 1) throw new InvalidDataException($"GLideN64 artifact: expected exactly one Mupen64Plus video DLL, found {dlls.Length}.");
            File.Copy(dlls[0], Path.Combine(bundle, Path.GetFileName(dlls[0])), true);
            diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.GameCore, "Optional renderer installed: GLideN64 x64 (verified artifact).");
        }
        finally { TryDelete(tempZip); TryDeleteDirectory(tempDir); }
    }

    private static async Task EnsureAngrylionAsync(string runtimeRoot, IM8Diagnostics diagnostics, CancellationToken ct)
    {
        var bundle = Path.Combine(runtimeRoot, "bundle");
        if (FindExactlyOne(bundle, n => n.Contains("angrylion", StringComparison.OrdinalIgnoreCase)) is not null) return;
        using var http = NewHttp();
        using var response = await http.GetAsync(AngrylionReleaseApi, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false));
        var assets = doc.RootElement.GetProperty("assets").EnumerateArray().ToArray();
        var matches = assets.Where(a => a.GetProperty("name").GetString()?.Equals(AngrylionAssetName, StringComparison.OrdinalIgnoreCase) == true).ToArray();
        if (matches.Length != 1) throw new InvalidDataException($"Angrylion v1.6: expected exactly one Windows Mupen64Plus asset, found {matches.Length}.");
        var asset = matches[0];
        var url = asset.GetProperty("browser_download_url").GetString() ?? throw new InvalidDataException("Angrylion asset has no download URL.");
        var digest = asset.TryGetProperty("digest", out var d) ? d.GetString() : null;
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Angrylion v1.6 release asset does not expose a SHA-256 digest; refusing an unverified plugin download.");
        var expected = digest[7..].ToUpperInvariant();
        var temp = Path.Combine(Path.GetTempPath(), "tot-angrylion-" + Guid.NewGuid().ToString("N") + ".dll");
        try
        {
            await DownloadAsync(http, url, temp, ct).ConfigureAwait(false);
            VerifySha256(temp, expected, "Angrylion RDP Plus v1.6");
            File.Copy(temp, Path.Combine(bundle, AngrylionAssetName), true);
            diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.GameCore, "Optional renderer installed: Angrylion RDP Plus v1.6 (GitHub SHA-256 verified).");
        }
        finally { TryDelete(temp); }
    }

    private static HttpClient NewHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("TrackerOfTime-V2/2.0");
        return http;
    }
    private static async Task DownloadAsync(HttpClient http, string url, string path, CancellationToken ct)
    {
        await using var input = await http.GetStreamAsync(url, ct).ConfigureAwait(false);
        await using var output = File.Create(path);
        await input.CopyToAsync(output, ct).ConfigureAwait(false);
    }
    private static void VerifySha256(string path, string expected, string label)
    {
        var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"{label} SHA-256 mismatch. Expected={expected} Actual={actual}");
    }
    private static string? FindExactlyOne(string root, Func<string,bool> namePredicate)
    {
        var matches = Directory.GetFiles(root, "*.dll", SearchOption.AllDirectories).Where(x => namePredicate(Path.GetFileName(x))).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }
    private static void TryDelete(string path){try{if(File.Exists(path))File.Delete(path);}catch{}}
    private static void TryDeleteDirectory(string path){try{if(Directory.Exists(path))Directory.Delete(path,true);}catch{}}
}
