using System.Diagnostics;
using TrackerOfTime.V2.M8.Application.Core;
using TrackerOfTime.V2.Randomizer;
using TrackerOfTime.V2.GameCore.Client;

namespace TrackerOfTime.V2.M8_1.Integration;

public abstract class ExternalProcessOwner : IExternalHostOwner, IAsyncDisposable
{
    private Process? _process;
    protected readonly IM8Diagnostics Diagnostics;
    protected ExternalProcessOwner(ExternalHostKind host, IM8Diagnostics diagnostics) { Host = host; Diagnostics = diagnostics; _snapshot = new(host, false, false, false, false, null); }
    private void RefreshSnapshot() { if (_process is { HasExited: true } && !_snapshot.Exited) _snapshot = _snapshot with { Exited = true, ExitCode = _process.ExitCode }; }
    public ExternalHostKind Host { get; }
    public int? ProcessId => _process is null ? null : _process.Id;
    private ProcessOwnershipSnapshot _snapshot;
    public ProcessOwnershipSnapshot Snapshot { get { RefreshSnapshot(); return _snapshot; } private set => _snapshot = value; }
    protected abstract ProcessStartInfo CreateStartInfo();
    protected abstract Task RequestProtocolShutdownAsync(CancellationToken ct);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_process is { HasExited: false }) return;
        var psi = CreateStartInfo();
        _process = Process.Start(psi) ?? throw new InvalidOperationException($"{Host} host failed to start.");
        Snapshot = new(Host, true, true, false, false, null);
        Diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Process, $"{Host} host started PID={_process.Id}.");
        if (psi.RedirectStandardOutput) _ = PumpAsync(_process.StandardOutput, M8DiagnosticLevel.Info, $"{Host}/stdout");
        if (psi.RedirectStandardError) _ = PumpAsync(_process.StandardError, M8DiagnosticLevel.Warning, $"{Host}/stderr");
        await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        if (_process.HasExited)
        {
            Snapshot = Snapshot with { Exited = true, ExitCode = _process.ExitCode };
            throw new InvalidOperationException($"{Host} host exited during startup with ExitCode={_process.ExitCode}.");
        }
    }

    private async Task PumpAsync(StreamReader reader, M8DiagnosticLevel level, string prefix)
    {
        try { while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line) if (!string.IsNullOrWhiteSpace(line)) Diagnostics.Write(level, M8DiagnosticCategory.GameCore, $"{prefix}: {line}"); }
        catch (Exception ex) { Diagnostics.Write(M8DiagnosticLevel.Warning, M8DiagnosticCategory.Process, $"{prefix} capture ended: {ex.Message}"); }
    }

    public async Task RequestShutdownAsync(CancellationToken cancellationToken = default)
    {
        if (_process is null || _process.HasExited) return;
        Snapshot = Snapshot with { ShutdownRequested = true };
        try { await RequestProtocolShutdownAsync(cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) { Diagnostics.Write(M8DiagnosticLevel.Warning, M8DiagnosticCategory.Process, $"{Host} protocol shutdown failed: {ex.Message}"); }
        var code = await WaitForExitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        if (code is null && _process is { HasExited: false })
        {
            _process.Kill(true);
            await _process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            code = _process.ExitCode;
        }
        Snapshot = Snapshot with { Exited = true, ExitCode = code };
    }

    public async Task<int?> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (_process is null) return null;
        if (_process.HasExited) return _process.ExitCode;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try { await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false); return _process.ExitCode; }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
    }

    public async ValueTask DisposeAsync()
    {
        try { await RequestShutdownAsync(); } catch { }
        _process?.Dispose();
    }
}

public sealed class OoTRHostOwner(string runtimeRoot, IM8Diagnostics diagnostics) : ExternalProcessOwner(ExternalHostKind.OoTR, diagnostics)
{
    protected override ProcessStartInfo CreateStartInfo()
    {
        var script = Path.Combine(runtimeRoot, "hosts", "OoTR.Host", "ootr_named_pipe_host.py");
        var ootrRoot = Path.Combine(runtimeRoot, "ThirdParty", "OoTR");
        if (!File.Exists(script)) throw new FileNotFoundException("Isolated OoTR named-pipe host not found.", script);
        foreach (var relative in OoTRRuntimeWorkspace.RequiredHelpers)
        {
            var helper = Path.Combine(ootrRoot, relative);
            if (!File.Exists(helper)) throw new FileNotFoundException($"OoTR runtime helper not found: {relative}", helper);
        }
        var psi = new ProcessStartInfo("py") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = ootrRoot };
        psi.ArgumentList.Add("-3.13"); psi.ArgumentList.Add(script);
        return psi;
    }
    protected override async Task RequestProtocolShutdownAsync(CancellationToken ct)
    {
        await using var client = new OoTRNamedPipeClient();
        var r = await client.SendAsync("Shutdown", null, ct).ConfigureAwait(false);
        if (!r.Success) throw new InvalidOperationException(r.Error?.Message ?? "OoTR shutdown failed.");
    }
}

public sealed class GameCoreHostOwner(string repositoryRoot, string runtimePath, string renderer, IM8Diagnostics diagnostics) : ExternalProcessOwner(ExternalHostKind.GameCore, diagnostics)
{
    protected override ProcessStartInfo CreateStartInfo()
    {
        if (string.IsNullOrWhiteSpace(runtimePath) || !Directory.Exists(runtimePath)) throw new DirectoryNotFoundException($"GameCore runtime not found: {runtimePath}");
        // Golden M5 launches the Windows apphost EXE directly. Keep that process identity/loader path
        // intact here instead of hosting GameCore under dotnet.exe; this is especially important for
        // native SDL/OpenGL driver discovery on legacy Windows graphics stacks.
        var portableExe = Path.Combine(repositoryRoot, "Runtime", "GameCore", "TrackerOfTime.V2.GameCore.Host.exe");
        var exe = File.Exists(portableExe)
            ? portableExe
            : Path.Combine(repositoryRoot, "src", "GameCore.Host", "bin", "Release", "net8.0-windows", "TrackerOfTime.V2.GameCore.Host.exe");
        if (!File.Exists(exe)) throw new FileNotFoundException("GameCore.Host Release apphost not found. Build the solution first.", exe);
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(exe)! };
        psi.Environment["TOT_GAMECORE_RUNTIME"] = runtimePath; psi.Environment["TOT_GAMECORE_RENDERER"] = renderer;
        return psi;
    }
    protected override async Task RequestProtocolShutdownAsync(CancellationToken ct)
    {
        await using var client = new GameCoreClient();
        await client.ConnectAsync(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
        await client.CallAsync("Shutdown", new { }, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
    }
}
