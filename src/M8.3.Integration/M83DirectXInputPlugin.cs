using System.Diagnostics;
using System.Text;

namespace TrackerOfTime.V2.M8_3.Integration;

/// <summary>
/// FIX2L: M8-only runtime adapter. Builds a tiny Mupen64Plus input plugin that reads XInput
/// directly in GetKeys(). Frozen M5/M6 sources and contracts are not modified.
/// </summary>
public static class M83DirectXInputPlugin
{
    public sealed record InstallResult(string PluginPath, string OriginalPath, string BackupPath, string BuildEvidence);

    public static InstallResult Install(string runtimeRoot, string repositoryRoot)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("FIX2L direct XInput plugin is Windows-only.");
        var bundle = Path.Combine(runtimeRoot, "bundle");
        var input = Directory.EnumerateFiles(bundle, "*.dll", SearchOption.AllDirectories)
            .Single(x => Path.GetFileName(x).Contains("input", StringComparison.OrdinalIgnoreCase)
                      && Path.GetFileName(x).Contains("sdl", StringComparison.OrdinalIgnoreCase));
        var dir = Path.GetDirectoryName(input)!;
        var backup = input + ".fix2l-original";
        if (!File.Exists(backup)) File.Copy(input, backup, true);

        var nativeDir = Path.Combine(repositoryRoot, "src", "M8.3.DirectInput.Native");
        var built = Path.Combine(nativeDir, "bin", "mupen64plus-input-sdl-fix2l.dll");
        string evidence;

        // Portable releases ship the already-built x64 provider. End users must not
        // need Visual Studio/Build Tools just to start a game. Development checkouts
        // still rebuild from the canonical C source when no packaged DLL is present.
        if (File.Exists(built))
        {
            evidence = "Using packaged x64 Direct-XInput provider.";
        }
        else
        {
            var source = Path.Combine(nativeDir, "m83_input_xinput.c");
            if (!File.Exists(source)) throw new FileNotFoundException("FIX2L native input source missing.", source);
            Directory.CreateDirectory(Path.GetDirectoryName(built)!);
            var buildScript = Path.Combine(nativeDir, "build-fix2l-native.cmd");
            if (!File.Exists(buildScript)) throw new FileNotFoundException("FIX2L native build script missing.", buildScript);
            evidence = RunBuildScript(buildScript, source, built);
            if (!File.Exists(built)) throw new InvalidOperationException("FIX2L native plugin build completed without producing DLL. Output: " + evidence);
        }

        File.Copy(built, input, true);
        return new(input, input, backup, evidence.Trim());
    }

    public static void Restore(InstallResult? install)
    {
        if (install is null) return;
        try { if (File.Exists(install.BackupPath)) File.Copy(install.BackupPath, install.OriginalPath, true); } catch { }
    }

    static string RunBuildScript(string buildScript, string source, string built)
    {
        // cmd.exe /S /C requires an additional outer quote pair when the command itself
        // starts with a quoted path. This keeps repository paths such as "tracker v2" intact.
        var comSpec = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(comSpec))
            comSpec = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        var command = $"\"{buildScript}\" \"{source}\" \"{built}\"";
        var p = new ProcessStartInfo
        {
            FileName = comSpec,
            Arguments = $"/d /s /c \"{command}\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(buildScript)!
        };
        using var proc = Process.Start(p) ?? throw new InvalidOperationException("Failed to start " + comSpec);
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        if (!proc.WaitForExit(120000))
        {
            try { proc.Kill(true); } catch { }
            throw new TimeoutException(buildScript + " timed out.");
        }
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"{buildScript} exited {proc.ExitCode}.\n{stdout}\n{stderr}");
        return stdout + stderr;
    }
}
