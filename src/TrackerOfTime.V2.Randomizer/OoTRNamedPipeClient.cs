using System.IO.Pipes;
using System.Text.Json;
namespace TrackerOfTime.V2.Randomizer;

public sealed class OoTRNamedPipeClient : IAsyncDisposable {
 const string PipeName="TrackerOfTime.V2.OoTR.v1";
 const int MaxFrame=16*1024*1024;
 static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){PropertyNameCaseInsensitive=true};

 public Task<HostResponse> SendAsync(string type,object? payload=null,CancellationToken ct=default)
  =>SendWithRequestIdAsync(Guid.NewGuid().ToString("N"),type,payload,ct);

 public async Task<HostResponse> SendWithRequestIdAsync(string requestId,string type,object? payload=null,CancellationToken ct=default){
  if(string.IsNullOrWhiteSpace(requestId)) throw new ArgumentException("requestId is required",nameof(requestId));
  using var pipe=new NamedPipeClientStream(".",PipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
  await pipe.ConnectAsync(ct);
  var req=new HostRequest(requestId,type,payload);
  byte[] bytes=JsonSerializer.SerializeToUtf8Bytes(req,Json), len=BitConverter.GetBytes(bytes.Length);
  await pipe.WriteAsync(len,ct); await pipe.WriteAsync(bytes,ct); await pipe.FlushAsync(ct);
  byte[] l=new byte[4]; await ReadExact(pipe,l,ct); int n=BitConverter.ToInt32(l);
  if(n<=0||n>MaxFrame) throw new InvalidDataException("Invalid pipe payload length");
  byte[] data=new byte[n]; await ReadExact(pipe,data,ct);
  return JsonSerializer.Deserialize<HostResponse>(data,Json)
      ??throw new InvalidDataException("Invalid response");
 }

 public Task<HostResponse> CancelAsync(string targetRequestId,CancellationToken ct=default)
  =>SendAsync("Cancel",new CancelRequest(targetRequestId),ct);

 static async Task ReadExact(Stream pipe,byte[] b,CancellationToken ct){
  int o=0; while(o<b.Length){int n=await pipe.ReadAsync(b.AsMemory(o),ct);if(n==0)throw new EndOfStreamException();o+=n;}
 }
 public ValueTask DisposeAsync()=>ValueTask.CompletedTask;
}