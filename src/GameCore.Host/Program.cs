using System.Buffers.Binary;using System.IO.Pipes;using System.Text.Json;using TrackerOfTime.V2.GameCore.Contracts;
namespace TrackerOfTime.V2.GameCore.Host;
internal static class Program {
 static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
 static async Task Main(){
  string pipe=Environment.GetEnvironmentVariable("TOT_GAMECORE_PIPE")??Protocol.DefaultPipe;
  string rt=Environment.GetEnvironmentVariable("TOT_GAMECORE_RUNTIME")??throw new InvalidOperationException("TOT_GAMECORE_RUNTIME required.");
  string bundle=Path.Combine(rt,"bundle");if(!Directory.Exists(bundle))throw new DirectoryNotFoundException(bundle);
  var files=Directory.GetFiles(bundle,"*",SearchOption.AllDirectories);
  string One(Func<string,bool> pred,string label){var m=files.Where(x=>pred(Path.GetFileName(x))).ToArray();if(m.Length!=1)throw new InvalidOperationException($"{label}: expected exactly one matching DLL, found {m.Length}.");return m[0];}
  string core=One(n=>n.Equals("mupen64plus.dll",StringComparison.OrdinalIgnoreCase),"Core");
  string renderer=(Environment.GetEnvironmentVariable("TOT_GAMECORE_RENDERER")??"Rice").Trim();
  string gfx;
  if(renderer.Equals("Rice",StringComparison.OrdinalIgnoreCase)){renderer="Rice";gfx=One(n=>n.Contains("rice",StringComparison.OrdinalIgnoreCase)&&n.EndsWith(".dll",StringComparison.OrdinalIgnoreCase),"Rice");}
  else if(renderer.Equals("Glide64mk2",StringComparison.OrdinalIgnoreCase)){renderer="Glide64mk2";gfx=One(n=>n.Contains("glide64mk2",StringComparison.OrdinalIgnoreCase)&&n.EndsWith(".dll",StringComparison.OrdinalIgnoreCase),"Glide64mk2");}
  else if(renderer.Equals("GLideN64",StringComparison.OrdinalIgnoreCase)){renderer="GLideN64";gfx=One(n=>n.Contains("gliden64",StringComparison.OrdinalIgnoreCase)&&!n.Contains("glide64mk2",StringComparison.OrdinalIgnoreCase)&&n.EndsWith(".dll",StringComparison.OrdinalIgnoreCase),"GLideN64");}
  else if(renderer.Equals("Angrylion",StringComparison.OrdinalIgnoreCase)){renderer="Angrylion";gfx=One(n=>n.Contains("angrylion",StringComparison.OrdinalIgnoreCase)&&n.EndsWith(".dll",StringComparison.OrdinalIgnoreCase),"Angrylion");}
  else throw new InvalidOperationException($"Unsupported TOT_GAMECORE_RENDERER '{renderer}'. Expected Rice, Glide64mk2, GLideN64 or Angrylion.");
  string audio=One(n=>n.Contains("audio",StringComparison.OrdinalIgnoreCase)&&n.Contains("sdl",StringComparison.OrdinalIgnoreCase)&&n.EndsWith(".dll",StringComparison.OrdinalIgnoreCase),"AudioSDL");
  string input=One(n=>n.Contains("input",StringComparison.OrdinalIgnoreCase)&&n.Contains("sdl",StringComparison.OrdinalIgnoreCase)&&n.EndsWith(".dll",StringComparison.OrdinalIgnoreCase),"InputSDL");
  string rsp=One(n=>n.Contains("rsp",StringComparison.OrdinalIgnoreCase)&&n.Contains("hle",StringComparison.OrdinalIgnoreCase)&&n.EndsWith(".dll",StringComparison.OrdinalIgnoreCase),"RspHLE");
  using var nc=new NativeCore();nc.Log+=x=>Console.WriteLine($"{DateTime.UtcNow:O} {x}");nc.Initialize(core,Path.Combine(rt,"config"),Path.Combine(rt,"shared"));
  Console.WriteLine($"GameCore.Host READY pipe={pipe}");bool quit=false;
  while(!quit){
   using var server=new NamedPipeServerStream(pipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);
   await server.WaitForConnectionAsync();
   try{
    while(server.IsConnected&&!quit){
     Request? req;try{req=await Read<Request>(server);}catch(EndOfStreamException){break;}
     if(req is null)break;Response response;
     try{
      if(req.ProtocolVersion!=Protocol.Version)throw new InvalidOperationException($"ProtocolVersion {req.ProtocolVersion} unsupported; expected {Protocol.Version}.");
      if(req.PayloadVersion!=Protocol.PayloadVersion)throw new InvalidOperationException($"PayloadVersion {req.PayloadVersion} unsupported; expected {Protocol.PayloadVersion}.");
      if(string.IsNullOrWhiteSpace(req.RequestId)||req.RequestId.Length>128)throw new InvalidOperationException("Invalid RequestId.");
      object? payload=req.MessageType switch{
       "Ping"=>new{pong=true},"QueryCapabilities"=>Capabilities(renderer),"Status"=>Status(nc,renderer),
       "LoadRom"=>Load(nc,req.Payload,gfx,audio,input,rsp,renderer),"Start"=>Act(nc.Start,nc,renderer),"Pause"=>Act(nc.Pause,nc,renderer),"Resume"=>Act(nc.Resume,nc,renderer),
       "Reset"=>Reset(nc,req.Payload,renderer),"Stop"=>Act(nc.Stop,nc,renderer),"ReadRdram"=>ReadMem(nc,req.Payload),"CloseRom"=>Act(nc.CloseRom,nc,renderer),
       "Shutdown"=>Shutdown(nc,out quit),_=>throw new InvalidOperationException($"Unknown MessageType: {req.MessageType}")};
      response=new(Protocol.Version,req.RequestId,req.MessageType+"Result",Protocol.PayloadVersion,true,payload,null);
     }catch(Exception ex){response=new(Protocol.Version,req.RequestId,req.MessageType+"Result",Protocol.PayloadVersion,false,null,ex.ToString());}
     await Write(server,response);
    }
   }catch(EndOfStreamException ex){Console.Error.WriteLine($"Client disconnected: {ex.Message}");}
    catch(IOException ex){Console.Error.WriteLine($"Client I/O error: {ex.Message}");}
    catch(InvalidDataException ex){Console.Error.WriteLine($"Rejected malformed client frame: {ex.Message}");}
    catch(JsonException ex){Console.Error.WriteLine($"Rejected malformed JSON: {ex.Message}");}
  }
 }
 static object Capabilities(string renderer)=>new Capabilities("Mupen64Plus","2.6.0","2.1.6","2.3.2","2.0.1","3.3.0",renderer,["Ping","QueryCapabilities","Status","LoadRom","Start","Pause","Resume","Reset","Stop","ReadRdram","CloseRom","Shutdown"]);
 static HostStatus Status(NativeCore n,string renderer){var s=n.QueryState();string state=s==CoreState.Stopped?(n.RomLoaded?"RomLoaded":"Ready"):s.ToString();return new(state,n.RomLoaded,s!=CoreState.Stopped,renderer,n.RdramAvailable,Protocol.RdramBytes);}
 static object Load(NativeCore n,JsonElement p,string g,string a,string i,string r,string renderer){string path=p.GetProperty("romPath").GetString()??throw new InvalidOperationException("romPath required");n.LoadRom(path);n.Attach(g,a,i,r,renderer);return Status(n,renderer);}
 static object Act(Action a,NativeCore n,string renderer){a();return Status(n,renderer);}
 static object Reset(NativeCore n,JsonElement p,string renderer){bool hard=p.TryGetProperty("hard",out var h)&&h.ValueKind==JsonValueKind.True;n.Reset(hard);return Status(n,renderer);}
 static object ReadMem(NativeCore n,JsonElement p){int o=p.GetProperty("offset").GetInt32(),c=p.GetProperty("count").GetInt32();byte[] b=n.ReadRdram(o,c);return new{offset=o,count=b.Length,dataBase64=Convert.ToBase64String(b)};}
 static object Shutdown(NativeCore n,out bool quit){if(n.QueryState()!=CoreState.Stopped)n.Stop();if(n.RomLoaded)n.CloseRom();quit=true;return new{shuttingDown=true};}
 static async Task<T?> Read<T>(Stream s){byte[] h=new byte[4];await Exact(s,h);int n=BinaryPrimitives.ReadInt32LittleEndian(h);if(n<=0||n>Protocol.MaxFrameBytes)throw new InvalidDataException($"Invalid request frame length {n}.");byte[] b=new byte[n];await Exact(s,b);return JsonSerializer.Deserialize<T>(b,Json);}
 static async Task Exact(Stream s,byte[] b){int o=0;while(o<b.Length){int n=await s.ReadAsync(b.AsMemory(o));if(n==0)throw new EndOfStreamException();o+=n;}}
 static async Task Write<T>(Stream s,T value){byte[] b=JsonSerializer.SerializeToUtf8Bytes(value,Json);if(b.Length>Protocol.MaxFrameBytes)throw new InvalidDataException($"Response frame exceeds {Protocol.MaxFrameBytes} bytes.");byte[] h=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(h,b.Length);await s.WriteAsync(h);await s.WriteAsync(b);await s.FlushAsync();}
}
