using System.IO.Compression;
using System.Security.Cryptography;
using TrackerOfTime.V2.M8.Application.Core;

namespace TrackerOfTime.V2.M8_1.Integration;

public sealed class OoTRRuntimeWorkspace(string repositoryRoot, string temporaryRoot, IM8Diagnostics diagnostics)
{
    private readonly string _sessionId = $"Session-{Environment.ProcessId}-{Guid.NewGuid():N}";
    public string RuntimeRoot => Path.Combine(temporaryRoot, "OoTR-Runtime", _sessionId);
    public string HostScript => Path.Combine(RuntimeRoot, "hosts", "OoTR.Host", "ootr_named_pipe_host.py");
    public string OoTRRoot => Path.Combine(RuntimeRoot, "ThirdParty", "OoTR");

    public string Prepare()
    {
        var sourceHost = Path.Combine(repositoryRoot, "src", "OoTR.Host");
        var sourceOoTR = Path.Combine(repositoryRoot, "third_party", "OoTR");
        if (!Directory.Exists(sourceHost) || !Directory.Exists(sourceOoTR)) throw new DirectoryNotFoundException("OoTR source is incomplete.");
        // Never reuse/delete a fixed runtime directory: a previous crashed host may still hold it.
        // Each V2 process gets its own isolated OoTR workspace.
        Directory.CreateDirectory(RuntimeRoot);
        CopyTree(sourceHost, Path.Combine(RuntimeRoot, "hosts", "OoTR.Host"));
        CopyTree(sourceOoTR, OoTRRoot);
        EnsureRuntimeHelpers();
        foreach (var relative in RequiredHelpers)
        {
            var p = Path.Combine(OoTRRoot, relative);
            if (!File.Exists(p)) throw new FileNotFoundException($"OoTR runtime helper missing after workspace preparation: {relative}", p);
        }
        diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Randomizer, $"Isolated OoTR runtime prepared: {RuntimeRoot}");
        return RuntimeRoot;
    }

    public void ClearRomCache()
    {
        var cache = Path.Combine(OoTRRoot, "ZOOTDEC.z64");
        try
        {
            if (File.Exists(cache))
            {
                File.Delete(cache);
                diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Randomizer, $"Removed OoTR base-ROM cache before ROM operation: {cache}");
            }
        }
        catch (Exception ex)
        {
            throw new IOException($"Unable to clear OoTR base-ROM cache '{cache}': {ex.Message}", ex);
        }
    }

    public void Cleanup()
    {
        try { if (Directory.Exists(RuntimeRoot)) Directory.Delete(RuntimeRoot, true); }
        catch (Exception ex) { diagnostics.Write(M8DiagnosticLevel.Warning, M8DiagnosticCategory.Randomizer, $"OoTR runtime cleanup warning: {ex.Message}"); }
    }

    public static readonly string[] RequiredHelpers =
    [
        Path.Combine("bin", "Decompress", "Decompress.exe"),
        Path.Combine("bin", "Compress", "Compress.exe"),
        Path.Combine("bin", "gzinject", "gzinject.exe"),
        Path.Combine("bin", "minibsdiff", "minibsdiff.exe")
    ];

    // OoTR keeps required native helper tools below its own bin/ directory. Tracker's
    // repository intentionally ignores generic build bin/ directories, so a source-only
    // checkout may not contain those upstream runtime tools. Restore them from the pinned
    // official OoTR release archive into this isolated runtime workspace only.
    private const string HelperArchiveUrl = "https://github.com/OoTRandomizer/OoT-Randomizer/archive/refs/tags/v9.1.zip";

    private void EnsureRuntimeHelpers()
    {
        if (RequiredHelpers.All(relative => File.Exists(Path.Combine(OoTRRoot, relative)))) return;

        var cacheRoot = Path.Combine(temporaryRoot, "OoTR-Dependencies");
        Directory.CreateDirectory(cacheRoot);
        var archive = Path.Combine(cacheRoot, "OoT-Randomizer-v9.1.zip");
        if (!File.Exists(archive))
        {
            diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Randomizer, "Downloading official OoTR v9.1 runtime helpers...");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            var bytes = http.GetByteArrayAsync(HelperArchiveUrl).GetAwaiter().GetResult();
            var stagingArchive = archive + ".download";
            File.WriteAllBytes(stagingArchive, bytes);
            File.Move(stagingArchive, archive, true);
        }

        using var zip = ZipFile.OpenRead(archive);
        foreach (var relative in RequiredHelpers)
        {
            var target = Path.Combine(OoTRRoot, relative);
            if (File.Exists(target)) continue;
            var normalized = relative.Replace('\\', '/');
            var entry = zip.Entries.SingleOrDefault(e =>
                e.FullName.EndsWith("/" + normalized, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
                throw new FileNotFoundException($"Official OoTR helper archive does not contain required runtime helper: {relative}");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, true);
        }

        diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Randomizer, "Official OoTR runtime helpers restored in isolated workspace.");
    }

    private static void CopyTree(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, true);
        }
    }
}


public static class OoTRStaleHostCleanup
{
    public static async Task<int> StopAllTrackerOoTRHostsAsync(IM8Diagnostics diagnostics, CancellationToken ct = default)
    {
        // Frozen M3 uses one fixed named-pipe name. Multiple surviving Python hosts can therefore
        // accept different requests. Remove only Tracker-of-Time OoTR named-pipe hosts before
        // creating the single owner for this V2 session.
        var command = "$ErrorActionPreference='SilentlyContinue'; " +
            "$ps=Get-CimInstance Win32_Process | Where-Object { $_.CommandLine -like '*ootr_named_pipe_host.py*' -and $_.CommandLine -like '*TrackerOfTime*OoTR-Runtime*' }; " +
            "$ids=@($ps | ForEach-Object { $_.ProcessId }); " +
            "$ps | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }; " +
            "[Console]::Out.Write(($ids -join ','))";
        var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-NonInteractive"); psi.ArgumentList.Add("-Command"); psi.ArgumentList.Add(command);
        using var process = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Unable to start stale OoTR host cleanup.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdout = (await stdoutTask.ConfigureAwait(false)).Trim();
        var stderr = (await stderrTask.ConfigureAwait(false)).Trim();
        var count = string.IsNullOrWhiteSpace(stdout) ? 0 : stdout.Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
        if (count > 0) diagnostics.Write(M8DiagnosticLevel.Warning, M8DiagnosticCategory.Process, $"Terminated {count} stale Tracker-of-Time OoTR host(s), PID(s)={stdout}.");
        if (!string.IsNullOrWhiteSpace(stderr)) diagnostics.Write(M8DiagnosticLevel.Warning, M8DiagnosticCategory.Process, "OoTR stale-host cleanup diagnostic: " + stderr);
        await Task.Delay(300, ct).ConfigureAwait(false);
        return count;
    }
}

public sealed class MupenRuntimeProvisioner(string userRoot, IM8Diagnostics diagnostics)
{
    public const string ExpectedSha256 = "8292C68D6FFC3428D4181AC09B1368EF2ADB2CC568325545AAC78CB6C1E66E21";
    public const string BundleUrl = "https://github.com/mupen64plus/mupen64plus-core/releases/download/2.6.0/mupen64plus-bundle-win64-2.6.0.zip";
    public string RuntimeRoot => Path.Combine(userRoot, "Runtime", "Mupen64Plus-2.6.0");
    private string BundleZip => Path.Combine(RuntimeRoot, "mupen64plus-bundle-win64-2.6.0.zip");

    public async Task<string> PrepareAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(RuntimeRoot);
        if (!File.Exists(BundleZip))
        {
            diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.GameCore, "Downloading pinned Mupen64Plus 2.6.0 runtime...");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            await using var input = await http.GetStreamAsync(BundleUrl, ct).ConfigureAwait(false);
            await using var output = File.Create(BundleZip);
            await input.CopyToAsync(output, ct).ConfigureAwait(false);
        }
        var sha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(BundleZip, ct))).ToUpperInvariant();
        if (!sha.Equals(ExpectedSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Pinned Mupen64Plus bundle SHA mismatch. Expected={ExpectedSha256} Actual={sha}");

        var bundle = Path.Combine(RuntimeRoot, "bundle");
        string[] files;
        if (IsPreparedRuntimeValid(bundle, out files))
        {
            diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.GameCore, $"Reusing verified Mupen64Plus runtime without rewriting loaded DLLs: {RuntimeRoot}");
        }
        else
        {
            var staging = Path.Combine(RuntimeRoot, "bundle-staging-" + Guid.NewGuid().ToString("N"));
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            ZipFile.ExtractToDirectory(BundleZip, staging, true);
            var stagedFiles = Directory.GetFiles(staging, "*", SearchOption.AllDirectories);
            ValidateRuntimeFiles(stagedFiles);
            if (Directory.Exists(bundle))
            {
                try { Directory.Delete(bundle, true); }
                catch (UnauthorizedAccessException ex) { throw new IOException("The existing emulator runtime is still in use. Stop GameCore before repairing/replacing it.", ex); }
                catch (IOException ex) { throw new IOException("The existing emulator runtime is still in use. Stop GameCore before repairing/replacing it.", ex); }
            }
            Directory.Move(staging, bundle);
            files = Directory.GetFiles(bundle, "*", SearchOption.AllDirectories);
        }
        ValidateRuntimeFiles(files);
        RequireExactlyOne(files, n => n.Equals("mupen64plus.dll", StringComparison.OrdinalIgnoreCase), "Core");
        RequireExactlyOne(files, n => n.Contains("rice", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), "Rice");
        RequireExactlyOne(files, n => n.Contains("glide64", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), "Glide64mk2");
        RequireExactlyOne(files, n => n.Contains("audio", StringComparison.OrdinalIgnoreCase) && n.Contains("sdl", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), "AudioSDL");
        RequireExactlyOne(files, n => n.Contains("input", StringComparison.OrdinalIgnoreCase) && n.Contains("sdl", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), "InputSDL");
        RequireExactlyOne(files, n => n.Contains("rsp", StringComparison.OrdinalIgnoreCase) && n.Contains("hle", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), "RspHLE");

        var shared = Path.Combine(RuntimeRoot, "shared"); Directory.CreateDirectory(shared);
        foreach (var name in new[] { "mupen64plus.ini", "RiceVideoLinux.ini", "Glide64mk2.ini", "InputAutoCfg.ini" })
        {
            var matches = files.Where(x => Path.GetFileName(x).Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length < 1) throw new FileNotFoundException($"Pinned Mupen64Plus shared data missing: {name}");
            File.Copy(matches[0], Path.Combine(shared, name), true);
        }
        Directory.CreateDirectory(Path.Combine(RuntimeRoot, "config"));
        diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.GameCore, $"Pinned Mupen64Plus runtime ready: {RuntimeRoot}");
        return RuntimeRoot;
    }

    private static bool IsPreparedRuntimeValid(string bundle, out string[] files)
    {
        files = Array.Empty<string>();
        if (!Directory.Exists(bundle)) return false;
        try
        {
            files = Directory.GetFiles(bundle, "*", SearchOption.AllDirectories);
            ValidateRuntimeFiles(files);
            return true;
        }
        catch { files = Array.Empty<string>(); return false; }
    }

    private static void ValidateRuntimeFiles(IEnumerable<string> files)
    {
        var a = files.ToArray();
        RequireExactlyOne(a, n => n.Equals("mupen64plus.dll", StringComparison.OrdinalIgnoreCase), "Core");
        RequireExactlyOne(a, n => n.Contains("rice", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), "Rice");
        RequireExactlyOne(a, n => n.Contains("glide64", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), "Glide64mk2");
        RequireExactlyOne(a, n => n.Contains("audio", StringComparison.OrdinalIgnoreCase) && n.Contains("sdl", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), "AudioSDL");
        RequireExactlyOne(a, n => n.Contains("input", StringComparison.OrdinalIgnoreCase) && n.Contains("sdl", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), "InputSDL");
        RequireExactlyOne(a, n => n.Contains("rsp", StringComparison.OrdinalIgnoreCase) && n.Contains("hle", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".dll", StringComparison.OrdinalIgnoreCase), "RspHLE");
    }

    private static string RequireExactlyOne(IEnumerable<string> files, Func<string, bool> predicate, string label)
    {
        var matches = files.Where(x => predicate(Path.GetFileName(x))).ToArray();
        if (matches.Length != 1) throw new InvalidDataException($"{label}: expected exactly one runtime DLL, found {matches.Length}.");
        return matches[0];
    }
}
