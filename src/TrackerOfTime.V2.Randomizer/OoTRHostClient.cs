using System.Diagnostics; using System.Text.Json;
namespace TrackerOfTime.V2.Randomizer;
public sealed class OoTRHostClient : IAsyncDisposable {
 readonly string python,host; Process? p; readonly SemaphoreSlim gate=new(1,1); static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){PropertyNameCaseInsensitive=true};
 public OoTRHostClient(string pythonExe,string hostScript){python=pythonExe;host=hostScript;}
 async Task Start(CancellationToken ct){if(p is {HasExited:false})return;var i=new ProcessStartInfo(python){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};i.ArgumentList.Add("-u");i.ArgumentList.Add(host);p=Process.Start(i)??throw new InvalidOperationException("OoTR.Host start failed");await Task.Yield();ct.ThrowIfCancellationRequested();}
 public async Task<HostResponse> SendAsync(string type,object? payload=null,CancellationToken ct=default){await gate.WaitAsync(ct);try{await Start(ct);var r=new HostRequest(Guid.NewGuid().ToString("N"),type,payload);await p!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(r,Json).AsMemory(),ct);await p.StandardInput.FlushAsync();var line=await p.StandardOutput.ReadLineAsync(ct)??throw new IOException("OoTR.Host disconnected");return JsonSerializer.Deserialize<HostResponse>(line,Json)??throw new InvalidDataException("Invalid host response");}finally{gate.Release();}}
 public Task<HostResponse> QueryMetadataAsync(CancellationToken ct=default)=>SendAsync("QueryMetadata",null,ct);
 public Task<HostResponse> ValidateRomAsync(ValidateRomRequest x,CancellationToken ct=default)=>SendAsync("ValidateRom",x,ct);
 public Task<HostResponse> ConvertSettingsAsync(ConvertSettingsRequest x,CancellationToken ct=default)=>SendAsync("ConvertSettings",x,ct);
 public Task<HostResponse> GenerateSeedAsync(GenerateSeedRequest x,CancellationToken ct=default)=>SendAsync("GenerateSeed",x,ct);
 public async ValueTask DisposeAsync(){if(p is {HasExited:false}){try{await SendAsync("Shutdown");if(!p.WaitForExit(1500))p.Kill(true);}catch{try{p?.Kill(true);}catch{}}}p?.Dispose();gate.Dispose();}
}