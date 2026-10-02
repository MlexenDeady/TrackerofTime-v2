using System.Text.Json;
using TrackerOfTime.V2.GameCore.Client;
using TrackerOfTime.V2.GameCore.Contracts;
using TrackerOfTime.V2.M6.CheckState;
using TrackerOfTime.V2.M6.Memory;
using TrackerOfTime.V2.M6.SnapshotAdapter;
using TrackerOfTime.V2.M8.Application.Core;

namespace TrackerOfTime.V2.M8_1.Integration;

public sealed class GameRuntimeService(IM8Diagnostics diagnostics) : IAsyncDisposable
{
    private GameCoreClient? _client;
    private GameCoreHostOwner? _owner;
    private GameSnapshotAdapter? _snapshot;
    public HostStatus? Status { get; private set; }
    public int? HostProcessId => _owner?.ProcessId;

    public async Task StartHostAsync(string repositoryRoot, string runtimePath, string renderer, CancellationToken ct = default)
    {
        _owner = new GameCoreHostOwner(repositoryRoot, runtimePath, renderer, diagnostics);
        await _owner.StartAsync(ct).ConfigureAwait(false);
        _client = new GameCoreClient();
        await _client.ConnectAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        var payload = await _client.CallAsync("QueryCapabilities", new { }, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
        diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.GameCore, $"GameCore ready: {payload.GetProperty("core").GetString()} {payload.GetProperty("coreVersion").GetString()}.");
        _snapshot = new GameSnapshotAdapter(new GoldenRdramReader(new GameCoreRdramTransport(_client)));
        Status = await ReadStatusAsync(ct).ConfigureAwait(false);
        diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.GameCore, $"GameCore host status: {Status.State}, renderer={Status.Renderer}.");
    }

    public async Task<HostStatus> LoadAndStartAsync(string romPath, CancellationToken ct = default)
    {
        NeedClient();
        try
        {
            diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.GameCore, $"LoadRom requested: '{romPath}'.");
            await _client!.CallAsync("LoadRom", new { romPath }, TimeSpan.FromSeconds(15), ct).ConfigureAwait(false);
            diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.GameCore, "LoadRom completed; Start requested.");
            var p = await _client.CallAsync("Start", new { }, TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
            Status = ParseStatus(p);
        }
        catch (Exception ex)
        {
            await Task.Delay(250, CancellationToken.None).ConfigureAwait(false);
            var ps = _owner?.Snapshot;
            diagnostics.Write(M8DiagnosticLevel.Error, M8DiagnosticCategory.GameCore, $"Game launch failed: {ex.GetType().Name}: {ex.Message}; hostExited={ps?.Exited}, exitCode={ps?.ExitCode?.ToString() ?? "<running/unknown>"}. Full exception: {ex}");
            throw new InvalidOperationException($"GameCore launch failed ({ex.GetType().Name}). Host exited={ps?.Exited}, ExitCode={ps?.ExitCode?.ToString() ?? "unknown"}. See session log for native/plugin diagnostics.", ex);
        }
        diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.GameCore, $"ROM started: '{romPath}', state={Status.State}, renderer={Status.Renderer}, RDRAM={Status.RdramAvailable}/{Status.RdramBytes}.");
        return Status;
    }

    public async Task<HostStatus> ReadStatusAsync(CancellationToken ct = default) { NeedClient(); Status = ParseStatus(await _client!.CallAsync("Status", new { }, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false)); return Status; }
    public async Task PauseAsync(CancellationToken ct = default)
    {
        NeedClient(); var current = await ReadStatusAsync(ct).ConfigureAwait(false);
        if (!current.Executing || current.State.Equals("Stopped", StringComparison.OrdinalIgnoreCase) || current.State.Equals("Ready", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("The game is stopped; Pause is only available while the game is running.");
        if (current.State.Equals("Paused", StringComparison.OrdinalIgnoreCase)) return;
        Status = ParseStatus(await _client!.CallAsync("Pause", new { }, TimeSpan.FromSeconds(5), ct));
    }
    public async Task ResumeAsync(CancellationToken ct = default)
    {
        NeedClient(); var current = await ReadStatusAsync(ct).ConfigureAwait(false);
        if (current.State.Equals("Running", StringComparison.OrdinalIgnoreCase)) return;
        if (!current.State.Equals("Paused", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"The game cannot be resumed because GameCore state is '{current.State}'. Launch/start the generated game again.");
        Status = ParseStatus(await _client!.CallAsync("Resume", new { }, TimeSpan.FromSeconds(5), ct));
    }
    public async Task StopAsync(CancellationToken ct = default) { if (_client is null) return; try { Status = ParseStatus(await _client.CallAsync("Stop", new { }, TimeSpan.FromSeconds(5), ct)); } catch { } }
    public async Task<byte[]> ReadRdramAsync(int offset,int count,CancellationToken ct=default)
    {
        NeedClient();
        return await new GameCoreRdramTransport(_client!).ReadAsync(offset,count,TimeSpan.FromSeconds(5),ct).ConfigureAwait(false);
    }

    public async Task<GameSnapshotAdapterResult> CaptureSnapshotAsync(CancellationToken ct = default)
    {
        if (_snapshot is null) throw new InvalidOperationException("GameCore is not initialized.");
        return await _snapshot.CaptureAsync("GameCore/Mupen64Plus", new CheckScannerSettings(null, 0, null, null), ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(); } catch { }
        if (_client is not null) await _client.DisposeAsync();
        if (_owner is not null) await _owner.DisposeAsync();
    }
    private void NeedClient() { if (_client is null) throw new InvalidOperationException("GameCore host is not connected."); }
    private static HostStatus ParseStatus(JsonElement p) => new(p.GetProperty("state").GetString() ?? "Unknown", p.GetProperty("romLoaded").GetBoolean(), p.GetProperty("executing").GetBoolean(), p.GetProperty("renderer").GetString() ?? "", p.GetProperty("rdramAvailable").GetBoolean(), p.GetProperty("rdramBytes").GetInt32());
}
