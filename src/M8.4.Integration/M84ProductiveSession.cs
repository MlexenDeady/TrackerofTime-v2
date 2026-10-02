using TrackerOfTime.V2.M8.Application.Core;
using TrackerOfTime.V2.M8_1.Integration;
using TrackerOfTime.V2.M8_2.Integration;
using TrackerOfTime.V2.M8_3.Integration;

namespace TrackerOfTime.V2.M8_4.Integration;

/// <summary>Productive M8.4 session owner; frozen GameCore/M6 semantics remain untouched.</summary>
public sealed class M84ProductiveSession : IAsyncDisposable
{
    private readonly string repositoryRoot;
    private readonly M8UserDataPaths paths;
    private readonly M8ApplicationStateMachine application;
    private readonly IM8Diagnostics diagnostics;
    private readonly GameRuntimeService game;
    private readonly M84UnifiedApplicationCoordinator coordinator;
    private M83DirectXInputPlugin.InstallResult? directInput;
    private M84InputRuntimeManager.Baseline? inputBaseline;
    private bool started;
    private bool hostLifetime;

    public M84ProductiveSession(string repositoryRoot, M8UserDataPaths paths, M8ApplicationStateMachine application, IM8Diagnostics diagnostics)
    {
        this.repositoryRoot=repositoryRoot; this.paths=paths; this.application=application; this.diagnostics=diagnostics;
        game=new GameRuntimeService(diagnostics); coordinator=new M84UnifiedApplicationCoordinator(application,game,diagnostics);
    }

    public M8ApplicationState State=>application.State;
    public string? Status=>application.Status;
    public int? HostProcessId=>game.HostProcessId;
    public string InputStatus { get; private set; }="Input not prepared";
    public bool UsesKeyboardFallback { get; private set; }
    public M84SessionPlan PlanUserRom(string romPath)=>coordinator.PlanUserRom(romPath);

    public async Task StartAsync(M84SessionPlan plan,string renderer,bool directXInput,CancellationToken ct=default)
    {
        if(started||hostLifetime) throw new InvalidOperationException("A GameCore session is already running.");
        var runtime=await new MupenRuntimeProvisioner(paths.Root,diagnostics).PrepareAsync(ct).ConfigureAwait(false);
        GameCorePreflight.Validate(runtime,renderer);
        try
        {
            // Always begin from the signed/pinned archive baseline. This repairs stale FIX2L state
            // left by an interrupted or older STEP3 session before the host can load a plugin.
            inputBaseline=M84InputRuntimeManager.RestorePinnedInputSdl(runtime,diagnostics);
            var useDirect=false;
            if(directXInput)
            {
                var xinput=M84XInputAvailability.Probe();
                diagnostics.Write(M8DiagnosticLevel.Info,M8DiagnosticCategory.Lifecycle,
                    $"M8.4 Direct-XInput preflight: available={xinput.XInputAvailable}, connected={xinput.ControllerConnected}, index={xinput.ControllerIndex?.ToString()??"none"}; {xinput.Evidence}");
                // M8.5 productive input: install the direct provider even when the pad is currently off.
                // The native provider stays alive, polls for XInput hot-plug in GetKeys(), and provides
                // a direct keyboard fallback while no controller is connected. No SendInput is used.
                if(xinput.XInputAvailable)
                {
                    directInput=M83DirectXInputPlugin.Install(runtime,repositoryRoot); useDirect=true;
                    UsesKeyboardFallback=true;
                    InputStatus=xinput.ControllerConnected
                        ? $"Direct-XInput controller {xinput.ControllerIndex} + keyboard active (hot-plug enabled)"
                        : "Keyboard active; waiting for XInput hot-plug";
                }
                else
                {
                    M83DiagnosticInput.PrepareKeyboardFallback(runtime);
                    UsesKeyboardFallback=true;
                    InputStatus="Input-SDL keyboard fallback (XInput runtime unavailable)";
                    diagnostics.Write(M8DiagnosticLevel.Warning,M8DiagnosticCategory.Lifecycle,
                        "XInput runtime unavailable; pinned Input-SDL keyboard provider selected for this session.");
                }
            }
            else
            {
                M83DiagnosticInput.PrepareKeyboardFallback(runtime);
                UsesKeyboardFallback=true;
                InputStatus="Input-SDL keyboard fallback (Direct-XInput disabled)";
            }
            M84InputRuntimeManager.VerifyProvider(inputBaseline,useDirect,diagnostics);
            hostLifetime=true;
            await coordinator.StartGameAsync(plan,repositoryRoot,runtime,renderer,ct).ConfigureAwait(false);
            started=true;
        }
        catch
        {
            // The input DLL cannot be restored while GameCore has it loaded. Tear down the host first.
            await DisposeHostBestEffortAsync().ConfigureAwait(false);
            RestoreInputBaseline();
            throw;
        }
    }

    public Task<M83TrackerFrame> CaptureTrackerAsync(M84SessionPlan plan,CancellationToken ct=default)=>coordinator.CaptureTrackerAsync(plan,ct);
    public Task<byte[]> ReadRdramAsync(int offset,int count,CancellationToken ct=default)=>game.ReadRdramAsync(offset,count,ct);

    public async Task StopAsync(CancellationToken ct=default)
    {
        if(!started&&!hostLifetime){RestoreInputBaseline();return;}
        try { if(started) await coordinator.StopAsync(ct).ConfigureAwait(false); }
        finally
        {
            started=false;
            // Stop only halts emulation; Dispose shuts down the frozen host and releases the DLL.
            await DisposeHostBestEffortAsync().ConfigureAwait(false);
            RestoreInputBaseline();
        }
    }

    private async Task DisposeHostBestEffortAsync()
    {
        if(!hostLifetime)return;
        try { await game.DisposeAsync().ConfigureAwait(false); }
        finally { hostLifetime=false; }
    }

    private void RestoreInputBaseline()
    {
        if(directInput is not null) M83DirectXInputPlugin.Restore(directInput);
        directInput=null;
        if(inputBaseline is not null)
        {
            var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(inputBaseline.InputPath)));
            if(!hash.Equals(inputBaseline.Sha256,StringComparison.OrdinalIgnoreCase))
                throw new IOException("M8.4 could not restore the pinned Input-SDL provider after GameCore shutdown.");
            diagnostics.Write(M8DiagnosticLevel.Info,M8DiagnosticCategory.Lifecycle,"M8.4 input baseline restored after session shutdown.");
        }
        inputBaseline=null;
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync().ConfigureAwait(false); }
        finally { await DisposeHostBestEffortAsync().ConfigureAwait(false); RestoreInputBaseline(); }
    }
}
