using System.Text.Json;
using TrackerOfTime.V2.M7.Workflow;
using TrackerOfTime.V2.M8.Application.Core;
using TrackerOfTime.V2.Randomizer;

namespace TrackerOfTime.V2.M8_1.Integration;

public sealed record RomValidationResult(bool Valid, string Format, long SizeBytes, string Sha256, string Detail);
public sealed record SettingsConversionResult(string SettingsString, JsonElement Settings);
public sealed record GenerationResult(string Seed, string SettingsString, string RandomizerVersion, string OutputDirectory, IReadOnlyList<string> OutputFiles, string StandardOutput, string StandardError);

public sealed class RandomizerWorkflowService(IM8Diagnostics diagnostics)
{
    private readonly OoTRNamedPipeClient _client = new();
    private string? _activeGenerationRequestId;
    public M7WorkflowSnapshot Snapshot { get; private set; } = new(M7WorkflowState.Idle, null, false, "Select a ROM.", null, false, Array.Empty<string>(), null);

    public void SelectRom(string? path) => Snapshot = M7WorkflowRules.SelectRom(Snapshot, path);

    public async Task<string> QueryMetadataAsync(CancellationToken ct = default)
    {
        var response = await _client.SendAsync("QueryMetadata", null, ct).ConfigureAwait(false);
        EnsureSuccess(response); var p = Payload(response);
        var version = p.GetProperty("randomizerVersion").GetString() ?? "";
        var python = p.GetProperty("pythonVersion").GetString() ?? "";
        diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Randomizer, $"OoTR ready: version={version}, Python={python}.");
        return version;
    }

    public async Task<RomValidationResult> ValidateAsync(CancellationToken ct = default)
    {
        if (!M7WorkflowRules.CanValidate(Snapshot)) throw new InvalidOperationException("ROM validation is not currently allowed.");
        Snapshot = Snapshot with { State = M7WorkflowState.ValidatingRom, Status = "Validating ROM...", Error = null };
        diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Randomizer, $"ValidateRom requested: {Snapshot.RomPath} | exists={File.Exists(Snapshot.RomPath)}");
        try
        {
            var response = await _client.SendAsync("ValidateRom", new ValidateRomRequest(Snapshot.RomPath!), ct).ConfigureAwait(false);
            EnsureSuccess(response);
            var p = Payload(response);
            var result = new RomValidationResult(p.GetProperty("valid").GetBoolean(), p.GetProperty("format").GetString() ?? "", p.GetProperty("sizeBytes").GetInt64(), p.GetProperty("sha256").GetString() ?? "", p.GetProperty("detail").GetString() ?? "");
            Snapshot = result.Valid
                ? Snapshot with { State = M7WorkflowState.RomValid, RomValidated = true, Status = "ROM valid.", Error = null }
                : Snapshot with { State = M7WorkflowState.Error, RomValidated = false, Status = "ROM invalid.", Error = result.Detail };
            diagnostics.Write(result.Valid ? M8DiagnosticLevel.Info : M8DiagnosticLevel.Warning, M8DiagnosticCategory.Randomizer, $"ValidateRom completed: valid={result.Valid}, format={result.Format}, size={result.SizeBytes}, sha256={result.Sha256}, detail='{result.Detail}'.");
            return result;
        }
        catch (Exception ex)
        {
            diagnostics.Write(M8DiagnosticLevel.Error, M8DiagnosticCategory.Randomizer, $"ValidateRom failed for '{Snapshot.RomPath}': {ex}");
            Snapshot = Snapshot with { State = M7WorkflowState.Error, RomValidated = false, Status = "ROM validation failed.", Error = ex.Message };
            throw;
        }
    }

    public async Task<SettingsConversionResult> ConvertSettingsAsync(string? settingsString, IReadOnlyDictionary<string, object?> settings, CancellationToken ct = default)
    {
        diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Randomizer, $"ConvertSettings requested: settingsString={(string.IsNullOrWhiteSpace(settingsString)?"<none>":settingsString)}, fields={settings.Count}.");
        var response = await _client.SendAsync("ConvertSettings", new ConvertSettingsRequest(settingsString, new Dictionary<string, object?>(settings)), ct).ConfigureAwait(false);
        EnsureSuccess(response); var p = Payload(response);
        var result = new SettingsConversionResult(p.GetProperty("settingsString").GetString() ?? "", p.GetProperty("settings").Clone());
        Snapshot = M7WorkflowRules.SettingsChanged(Snapshot, result.SettingsString);
        diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Randomizer, $"ConvertSettings completed: normalizedSettingsString={result.SettingsString}, outputStale={Snapshot.OutputStale}.");
        return result;
    }

    public async Task<GenerationResult> GenerateAsync(M7GenerationInput input, CancellationToken ct = default)
    {
        if (!M7WorkflowRules.CanGenerate(Snapshot, input.Output)) throw new InvalidOperationException("Seed generation is not currently allowed.");
        Snapshot = Snapshot with { State = M7WorkflowState.Generating, Status = "Generating seed...", Error = null, OutputFiles = Array.Empty<string>(), OutputStale = false };
        _activeGenerationRequestId = Guid.NewGuid().ToString("N");
        diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Randomizer, $"GenerateSeed requested: requestId={_activeGenerationRequestId}, rom='{input.RomPath}', seed='{input.Seed ?? "<AUTO>"}', worldCount={input.WorldCount}, output='{input.Output.OutputDirectory}', patch={input.Output.CreatePatchFile}, compressed={input.Output.CreateCompressedRom}, uncompressed={input.Output.CreateUncompressedRom}, spoiler={input.Output.CreateSpoiler}.");
        try
        {
            var response = await _client.SendWithRequestIdAsync(_activeGenerationRequestId, "GenerateSeed", M7WorkflowRules.ToFrozenM3Request(input), ct).ConfigureAwait(false);
            EnsureSuccess(response); var p = Payload(response);
            var files = p.GetProperty("outputFiles").EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().ToArray();
            var result = new GenerationResult(p.GetProperty("seed").GetString() ?? "", p.GetProperty("settingsString").GetString() ?? "", p.GetProperty("randomizerVersion").GetString() ?? "", p.GetProperty("outputDirectory").GetString() ?? input.Output.OutputDirectory, files, p.GetProperty("standardOutput").GetString() ?? "", p.GetProperty("standardError").GetString() ?? "");
            Snapshot = Snapshot with { State = M7WorkflowState.OutputReady, Status = "Generation complete.", OutputFiles = files, SettingsString = result.SettingsString, OutputStale = false, Error = null };
            diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Randomizer, $"Generation complete: seed={result.Seed}, version={result.RandomizerVersion}, output='{result.OutputDirectory}', files={files.Length}.");
            foreach (var file in files) diagnostics.Write(M8DiagnosticLevel.Info, M8DiagnosticCategory.Randomizer, $"Generated output: {file}");
            if (!string.IsNullOrWhiteSpace(result.StandardError)) diagnostics.Write(M8DiagnosticLevel.Warning, M8DiagnosticCategory.Randomizer, "OoTR stderr: " + result.StandardError.Trim());
            return result;
        }
        catch (Exception ex)
        {
            diagnostics.Write(M8DiagnosticLevel.Error, M8DiagnosticCategory.Randomizer, $"GenerateSeed failed: requestId={_activeGenerationRequestId}, {ex}");
            Snapshot = Snapshot with { State = M7WorkflowState.Error, Status = "Generation failed.", Error = ex.Message };
            throw;
        }
        finally { _activeGenerationRequestId = null; }
    }

    public async Task CancelGenerationAsync(CancellationToken ct = default)
    {
        var id = _activeGenerationRequestId;
        if (id is null) return;
        Snapshot = Snapshot with { State = M7WorkflowState.Cancelling, Status = "Cancelling generation..." };
        var response = await _client.CancelAsync(id, ct).ConfigureAwait(false);
        EnsureSuccess(response);
        Snapshot = Snapshot with { State = M7WorkflowState.Cancelled, Status = "Generation cancelled.", Error = null };
    }

    public void MarkGameStartRequested()
    {
        // M7's frozen transition is intentionally strict. M8.1 may restart the same verified output
        // with another renderer, so an already-requested game start is idempotent here.
        if (Snapshot.State is M7WorkflowState.GameStartRequested or M7WorkflowState.TrackerStartRequested) return;
        Snapshot = M7WorkflowRules.RequestGameStart(Snapshot);
    }
    public void MarkTrackerStartRequested() => Snapshot = M7WorkflowRules.RequestTrackerStart(Snapshot);
    public static string? FindLaunchableRom(IEnumerable<string> outputFiles) => outputFiles.FirstOrDefault(x => Path.GetExtension(x).Equals(".z64", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(x).Equals(".n64", StringComparison.OrdinalIgnoreCase));
    private static JsonElement Payload(HostResponse r) => r.Payload is JsonElement e ? e : JsonSerializer.SerializeToElement(r.Payload);
    private static void EnsureSuccess(HostResponse r) { if (!r.Success) throw new InvalidOperationException($"{r.Error?.Code}: {r.Error?.Message}\n{r.Error?.Detail}".Trim()); }
}
