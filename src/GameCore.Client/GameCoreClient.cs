using System.Buffers.Binary;using System.IO.Pipes;using System.Text.Json;using TrackerOfTime.V2.GameCore.Contracts;
namespace TrackerOfTime.V2.GameCore.Client;
public sealed class GameCoreClient:IAsyncDisposable{
 readonly NamedPipeClientStream pipe;readonly JsonSerializerOptions json=new(JsonSerializerDefaults.Web);int seq;
 public GameCoreClient(string pipeName=Protocol.DefaultPipe)=>pipe=new(".",
  pipeName,PipeDirection.InOut,PipeOptions.Asynchronous);
 public async Task ConnectAsync(TimeSpan timeout,CancellationToken ct=default){using var cts=CancellationTokenSource.CreateLinkedTokenSource(ct);cts.CancelAfter(timeout);await pipe.ConnectAsync(cts.Token);}
 public async Task<JsonElement> CallAsync(string type,object payload,TimeSpan timeout,CancellationToken ct=default){
  using var cts=CancellationTokenSource.CreateLinkedTokenSource(ct);cts.CancelAfter(timeout);string id=Interlocked.Increment(ref seq).ToString();
  byte[] body=JsonSerializer.SerializeToUtf8Bytes(new{protocolVersion=Protocol.Version,requestId=id,messageType=type,payloadVersion=Protocol.PayloadVersion,payload},json);
  if(body.Length>Protocol.MaxFrameBytes)throw new InvalidDataException("Request frame too large.");
  byte[] h=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(h,body.Length);await pipe.WriteAsync(h,cts.Token);await pipe.WriteAsync(body,cts.Token);await pipe.FlushAsync(cts.Token);
  await Exact(h,cts.Token);int n=BinaryPrimitives.ReadInt32LittleEndian(h);if(n<=0||n>Protocol.MaxFrameBytes)throw new InvalidDataException($"Invalid response frame length {n}.");
  byte[] rb=new byte[n];await Exact(rb,cts.Token);using var doc=JsonDocument.Parse(rb);var root=doc.RootElement.Clone();
  if(root.GetProperty("protocolVersion").GetInt32()!=Protocol.Version||root.GetProperty("payloadVersion").GetInt32()!=Protocol.PayloadVersion)throw new InvalidDataException("Response protocol version mismatch.");
  if(root.GetProperty("requestId").GetString()!=id)throw new InvalidDataException("Response RequestId mismatch.");
  if(!root.GetProperty("success").GetBoolean())throw new InvalidOperationException(root.GetProperty("error").GetString()??"GameCore operation failed.");
  return root.GetProperty("payload").Clone();
 }
 async Task Exact(byte[] b,CancellationToken ct){int o=0;while(o<b.Length){int n=await pipe.ReadAsync(b.AsMemory(o),ct);if(n==0)throw new EndOfStreamException();o+=n;}}
 public void Disconnect(){pipe.Close();}
 public ValueTask DisposeAsync()=>pipe.DisposeAsync();
}