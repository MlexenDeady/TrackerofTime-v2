using TrackerOfTime.V2.GameCore.Contracts;
using System.Runtime.InteropServices;
namespace TrackerOfTime.V2.GameCore.Host;

internal enum CoreState { Stopped=1, Running=2, Paused=3 }

internal sealed class NativeCore : IDisposable {
 // EXACT Mupen64Plus 2.6.0 m64p_types.h enum values. Do not renumber.
 const int API=0x020106;
 const int ROM_OPEN=1, ROM_CLOSE=2, EXECUTE=5, STOP=6, PAUSE=7, RESUME=8, CORE_STATE_QUERY=9, SET_FRAME_CALLBACK=15, RESET=19;
 const int RSP=1,GFX=2,AUDIO=3,INPUT=4,RDRAM=1,M64TYPE_INT=1,M64TYPE_BOOL=3,M64CORE_EMU_STATE=1;
 const int M64ERR_SUCCESS=0;
 const int MAX_ROM_BYTES=64*1024*1024;

 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DebugCb(IntPtr context,int level,IntPtr message);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void StateCb(IntPtr context,int param,int value);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void FrameCb(uint frameIndex);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CoreStartup(int apiVersion,IntPtr configPath,IntPtr dataPath,IntPtr debugContext,DebugCb debugCallback,IntPtr stateContext,StateCb stateCallback);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CoreShutdown();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CoreDoCommand(int command,int paramInt,IntPtr paramPtr);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CoreAttachPlugin(int type,IntPtr handle);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CoreDetachPlugin(int type);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr DebugMemGetPointer(int type);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int PluginStartup(IntPtr coreHandle,IntPtr context,DebugCb callback);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int PluginShutdown();
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int PluginGetVersion(out int type,out int version,out int apiVersion,out IntPtr name,out int capabilities);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ConfigOpenSection(IntPtr name,out IntPtr section);
 [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int ConfigSetParameter(IntPtr section,IntPtr name,int type,IntPtr value);

 sealed class Plug { public string Name=""; public int Type; public IntPtr Handle; public PluginShutdown? Shutdown; public bool Attached,Started; }
 readonly object gate=new(); readonly List<Plug> plugins=[]; readonly DebugCb debugCallback; readonly StateCb stateCallback; readonly FrameCb frameCallback;
 GCHandle debugRoot,stateRoot,frameRoot; IntPtr core; CoreDoCommand? command; CoreShutdown? shutdown; CoreDetachPlugin? detach; DebugMemGetPointer? mem;
 Thread? emuThread; volatile bool executeActive; long frameEpoch; bool initialized,romOpen,disposed; Exception? executeException;
 public event Action<string>? Log;
 public bool RomLoaded { get { lock(gate) return romOpen; } }
 public bool ExecuteActive=>executeActive;
 public bool RdramAvailable { get { lock(gate) return initialized && romOpen && QueryState()!=CoreState.Stopped && mem?.Invoke(RDRAM)!=IntPtr.Zero; } }

 public NativeCore(){
  debugCallback=(c,l,m)=>Log?.Invoke($"CORE[{l}] {Marshal.PtrToStringAnsi(m)??""}");
  stateCallback=(c,p,v)=>Log?.Invoke($"STATE {p}={v}");
  frameCallback=f=>Interlocked.Increment(ref frameEpoch);
  debugRoot=GCHandle.Alloc(debugCallback); stateRoot=GCHandle.Alloc(stateCallback); frameRoot=GCHandle.Alloc(frameCallback);
 }
 static T Export<T>(IntPtr h,string name) where T:Delegate {
  if(!NativeLibrary.TryGetExport(h,name,out var p)||p==IntPtr.Zero) throw new MissingMethodException($"Required native export missing: {name}");
  return Marshal.GetDelegateForFunctionPointer<T>(p);
 }
 static IntPtr Ansi(string s)=>Marshal.StringToHGlobalAnsi(s);
 static string ErrorName(int e)=>e switch{0=>"SUCCESS",1=>"NOT_INIT",2=>"ALREADY_INIT",3=>"INCOMPATIBLE",4=>"INPUT_ASSERT",5=>"INPUT_INVALID",6=>"INPUT_NOT_FOUND",7=>"NO_MEMORY",8=>"FILES",9=>"INTERNAL",10=>"INVALID_STATE",11=>"PLUGIN_FAIL",12=>"SYSTEM_FAIL",13=>"UNSUPPORTED",14=>"WRONG_TYPE",_=>"UNKNOWN"};
 static void Check(int e,string op){if(e!=M64ERR_SUCCESS)throw new InvalidOperationException($"{op} failed: m64p_error={e} ({ErrorName(e)})");}
 void Need(){if(!initialized)throw new InvalidOperationException("Core not initialized.");}

 public void Initialize(string coreDll,string configDir,string sharedDir){
  lock(gate){
   if(initialized)return;
   if(!Environment.Is64BitProcess)throw new PlatformNotSupportedException("GameCore.Host must run x64.");
   core=NativeLibrary.Load(Path.GetFullPath(coreDll));
   try{
    var startup=Export<CoreStartup>(core,"CoreStartup"); shutdown=Export<CoreShutdown>(core,"CoreShutdown");
    command=Export<CoreDoCommand>(core,"CoreDoCommand"); detach=Export<CoreDetachPlugin>(core,"CoreDetachPlugin");
    mem=Export<DebugMemGetPointer>(core,"DebugMemGetPointer");
    var open=Export<ConfigOpenSection>(core,"ConfigOpenSection"); var set=Export<ConfigSetParameter>(core,"ConfigSetParameter");
    Directory.CreateDirectory(configDir);Directory.CreateDirectory(sharedDir);
    IntPtr c=Ansi(Path.GetFullPath(configDir)),d=Ansi(Path.GetFullPath(sharedDir));
    try{Check(startup(API,c,d,IntPtr.Zero,debugCallback,IntPtr.Zero,stateCallback),"CoreStartup");initialized=true;}
    finally{Marshal.FreeHGlobal(c);Marshal.FreeHGlobal(d);}
    IntPtr sectionName=Ansi("Video-General");
    try{
     Check(open(sectionName,out var section),"ConfigOpenSection(Video-General)");
     Set(section,set,"ScreenWidth",640,M64TYPE_INT);Set(section,set,"ScreenHeight",480,M64TYPE_INT);
     Set(section,set,"Fullscreen",0,M64TYPE_BOOL);Set(section,set,"VerticalSync",0,M64TYPE_BOOL);
    }finally{Marshal.FreeHGlobal(sectionName);}
   }catch{if(initialized){try{shutdown?.Invoke();}catch{} initialized=false;} if(core!=IntPtr.Zero){NativeLibrary.Free(core);core=IntPtr.Zero;}throw;}
  }
 }
 static void Set(IntPtr section,ConfigSetParameter set,string name,int value,int type){
  IntPtr n=Ansi(name),v=Marshal.AllocHGlobal(sizeof(int));
  try{Marshal.WriteInt32(v,value);Check(set(section,n,type,v),$"ConfigSetParameter({name})");}
  finally{Marshal.FreeHGlobal(n);Marshal.FreeHGlobal(v);}
 }

 public CoreState QueryState(){
  lock(gate){
   Need();IntPtr p=Marshal.AllocHGlobal(sizeof(int));
   try{
    Marshal.WriteInt32(p,0);Check(command!(CORE_STATE_QUERY,M64CORE_EMU_STATE,p),"CORE_STATE_QUERY(M64CORE_EMU_STATE)");
    int v=Marshal.ReadInt32(p);if(v is <1 or >3)throw new InvalidOperationException($"Core returned invalid emulation state {v}.");
    return (CoreState)v;
   }finally{Marshal.FreeHGlobal(p);}
  }
 }
 void InstallFrameCallback(){IntPtr fp=Marshal.GetFunctionPointerForDelegate(frameCallback);Check(command!(SET_FRAME_CALLBACK,0,fp),"SET_FRAME_CALLBACK");}
 void WaitForFirstFrame(long baseline,int timeoutMs=15000){
  long until=Environment.TickCount64+timeoutMs;
  while(Environment.TickCount64<until){CheckExecuteThread("Start/FrameBarrier");if(Interlocked.Read(ref frameEpoch)>baseline)return;Thread.Sleep(10);}
  throw new TimeoutException($"Start: core reported Running but no emulated frame completed within {timeoutMs} ms.");
 }
 public CoreState WaitForState(CoreState wanted,string op,int timeoutMs=10000){
  long until=Environment.TickCount64+timeoutMs;CoreState last=CoreState.Stopped;
  while(Environment.TickCount64<until){last=QueryState();if(last==wanted)return last;CheckExecuteThread(op);Thread.Sleep(20);}
  throw new TimeoutException($"{op}: core did not reach {wanted} within {timeoutMs} ms; actual={last}, executeActive={executeActive}.");
 }
 void CheckExecuteThread(string op){
  var ex=executeException;if(ex!=null)throw new InvalidOperationException($"{op}: EXECUTE thread failed.",ex);
  if(emuThread!=null && !emuThread.IsAlive && !executeActive && QueryState()!=CoreState.Stopped)throw new InvalidOperationException($"{op}: EXECUTE thread ended unexpectedly.");
 }

 public unsafe void LoadRom(string path){
  lock(gate){
   Need();if(QueryState()!=CoreState.Stopped||executeActive)throw new InvalidOperationException("LoadRom requires Stopped core.");
   if(romOpen)CloseRom();
   var fi=new FileInfo(path);if(!fi.Exists)throw new FileNotFoundException("ROM not found.",path);
   if(fi.Length<4096||fi.Length>MAX_ROM_BYTES||fi.Length%4!=0)throw new InvalidDataException($"ROM size invalid for Mupen64Plus 2.6.0: {fi.Length} bytes.");
   string ext=fi.Extension.ToLowerInvariant();if(ext is not ".n64" and not ".z64" and not ".v64")throw new InvalidDataException($"Unsupported ROM extension: {ext}");
   byte[] bytes=File.ReadAllBytes(path);
   uint magic=((uint)bytes[0]<<24)|((uint)bytes[1]<<16)|((uint)bytes[2]<<8)|bytes[3];
   if(magic!=0x80371240 && magic!=0x40123780 && magic!=0x37804012)throw new InvalidDataException($"Unrecognized N64 ROM byte order/header: 0x{magic:X8}");
   fixed(byte* p=bytes)Check(command!(ROM_OPEN,bytes.Length,(IntPtr)p),"ROM_OPEN");
   romOpen=true;
  }
 }

 public void Attach(string gfx,string audio,string input,string rsp,string videoName="Video"){
  lock(gate){
   Need();if(!romOpen)throw new InvalidOperationException("Attach requires an open ROM.");if(QueryState()!=CoreState.Stopped)throw new InvalidOperationException("Attach requires Stopped core.");
   if(plugins.Count!=0)throw new InvalidOperationException("Plugins already attached.");
   var attach=Export<CoreAttachPlugin>(core,"CoreAttachPlugin");
   var specs=new[]{(videoName,GFX,gfx),("AudioSDL",AUDIO,audio),("InputSDL",INPUT,input),("RspHLE",RSP,rsp)};
   try{
    foreach(var spec in specs){
     var x=new Plug{Name=spec.Item1,Type=spec.Item2};
     try{
      x.Handle=NativeLibrary.Load(Path.GetFullPath(spec.Item3));
      var version=Export<PluginGetVersion>(x.Handle,"PluginGetVersion");
      Check(version(out int actual,out _,out int api,out IntPtr name,out _),$"{x.Name}.PluginGetVersion");
      if(actual!=x.Type)throw new InvalidOperationException($"{x.Name}: plugin type {actual}, expected {x.Type}.");
      if((api&0xffff0000)!=0x00020000)throw new InvalidOperationException($"{x.Name}: incompatible plugin API 0x{api:X8}.");
      string nativeName=Marshal.PtrToStringAnsi(name)??x.Name;Log?.Invoke($"PLUGIN {nativeName} type={actual} api=0x{api:X8}");
      var start=Export<PluginStartup>(x.Handle,"PluginStartup");x.Shutdown=Export<PluginShutdown>(x.Handle,"PluginShutdown");
      Check(start(core,IntPtr.Zero,debugCallback),$"{x.Name}.PluginStartup");x.Started=true;
      Check(attach(x.Type,x.Handle),$"{x.Name}.Attach");x.Attached=true;plugins.Add(x);
     }catch{CleanupPlugin(x);throw;}
    }
   }catch{DetachAllBestEffort();throw;}
  }
 }

 public void Start(){
  long startFrame;
  lock(gate){
   Need();if(!romOpen||plugins.Count!=4)throw new InvalidOperationException("Start requires ROM plus all four plugins.");
   if(QueryState()!=CoreState.Stopped||executeActive)throw new InvalidOperationException("Start requires Stopped core.");
   executeException=null;
   InstallFrameCallback();
   startFrame=Interlocked.Read(ref frameEpoch);
   executeActive=true;
   emuThread=new Thread(()=>{
    try{int e=command!(EXECUTE,0,IntPtr.Zero);if(e!=0)executeException=new InvalidOperationException($"EXECUTE failed: m64p_error={e} ({ErrorName(e)})");Log?.Invoke($"EXECUTE returned {e} ({ErrorName(e)})");}
    catch(Exception ex){executeException=ex;Log?.Invoke($"EXECUTE exception: {ex}");}
    finally{executeActive=false;}
   }){IsBackground=true,Name="TrackerOfTime.GameCore.EXECUTE"};
   try{emuThread.Start();}catch{executeActive=false;emuThread=null;throw;}
  }
  try{
   WaitForState(CoreState.Running,"Start/State",15000);
   WaitForFirstFrame(startFrame,15000);
   if(!RdramAvailable)throw new InvalidOperationException("Start reached real frame execution but RDRAM pointer is unavailable.");
  }catch{
   // A failed Start must not leave an unowned emulation session running.
   try{if(QueryState()!=CoreState.Stopped)command!(STOP,0,IntPtr.Zero);}catch{}
   try{emuThread?.Join(5000);}catch{}
   throw;
  }
 }

 public void Pause(){var st=QueryState();if(st==CoreState.Paused)return;if(st!=CoreState.Running)throw new InvalidOperationException($"Pause requires Running; actual={st}.");Check(command!(PAUSE,0,IntPtr.Zero),"PAUSE");WaitForState(CoreState.Paused,"Pause");}
 public void Resume(){var st=QueryState();if(st==CoreState.Running)return;if(st!=CoreState.Paused)throw new InvalidOperationException($"Resume requires Paused; actual={st}.");Check(command!(RESUME,0,IntPtr.Zero),"RESUME");WaitForState(CoreState.Running,"Resume");}
 public void Reset(bool hard=false){var st=QueryState();if(st is not CoreState.Running and not CoreState.Paused)throw new InvalidOperationException($"Reset requires Running or Paused; actual={st}.");Check(command!(RESET,hard?1:0,IntPtr.Zero),hard?"RESET(hard)":"RESET(soft)");}
 public void Stop(){
  if(!initialized)return;CoreState st=QueryState();if(st!=CoreState.Stopped)Check(command!(STOP,0,IntPtr.Zero),"STOP");
  Thread? t=emuThread;if(t!=null&&!t.Join(15000))throw new TimeoutException("STOP: EXECUTE thread did not return within 15 seconds.");
  emuThread=null;
  if(executeException!=null)throw new InvalidOperationException("EXECUTE ended with an error.",executeException);
  WaitForState(CoreState.Stopped,"Stop",5000);
 }
 public byte[] ReadRdram(int offset,int count){
  if(QueryState()==CoreState.Stopped)throw new InvalidOperationException("ReadRdram requires Running or Paused core.");
  if(offset<0||count<1||count>Protocol.MaxRdramRead||offset>Protocol.RdramBytes-count)throw new ArgumentOutOfRangeException($"ReadRdram range invalid: offset={offset}, count={count}.");
  IntPtr p=mem?.Invoke(RDRAM)??IntPtr.Zero;if(p==IntPtr.Zero)throw new InvalidOperationException("RDRAM unavailable.");
  byte[] bytes=new byte[count];Marshal.Copy(IntPtr.Add(p,offset),bytes,0,count);return bytes;
 }
 public void CloseRom(){
  lock(gate){
   if(!initialized||!romOpen)return;if(QueryState()!=CoreState.Stopped||executeActive)throw new InvalidOperationException("CloseRom requires Stopped core.");
   DetachAll();Check(command!(ROM_CLOSE,0,IntPtr.Zero),"ROM_CLOSE");romOpen=false;
  }
 }
 void CleanupPlugin(Plug x){
  if(x.Attached){try{detach?.Invoke(x.Type);}catch{}x.Attached=false;}
  if(x.Started){try{x.Shutdown?.Invoke();}catch{}x.Started=false;}
  if(x.Handle!=IntPtr.Zero){try{NativeLibrary.Free(x.Handle);}catch{}x.Handle=IntPtr.Zero;}
 }
 void DetachAll(){
  for(int i=plugins.Count-1;i>=0;i--){var x=plugins[i];if(x.Attached){Check(detach!(x.Type),$"{x.Name}.Detach");x.Attached=false;}}
  foreach(var x in plugins){if(x.Started){Check(x.Shutdown!(),$"{x.Name}.Shutdown");x.Started=false;}if(x.Handle!=IntPtr.Zero){NativeLibrary.Free(x.Handle);x.Handle=IntPtr.Zero;}}
  plugins.Clear();
 }
 void DetachAllBestEffort(){for(int i=plugins.Count-1;i>=0;i--)CleanupPlugin(plugins[i]);plugins.Clear();}
 public void Dispose(){
  if(disposed)return;disposed=true;
  try{if(initialized&&QueryState()!=CoreState.Stopped)Stop();}catch(Exception ex){Log?.Invoke($"Dispose Stop warning: {ex.Message}");}
  try{if(initialized&&romOpen&&QueryState()==CoreState.Stopped)CloseRom();}catch(Exception ex){Log?.Invoke($"Dispose CloseRom warning: {ex.Message}");DetachAllBestEffort();}
  try{if(initialized){Check(shutdown!(),"CoreShutdown");initialized=false;}}catch(Exception ex){Log?.Invoke($"Dispose CoreShutdown warning: {ex.Message}");}
  if(core!=IntPtr.Zero){try{NativeLibrary.Free(core);}catch{}core=IntPtr.Zero;}
  if(debugRoot.IsAllocated)debugRoot.Free();if(stateRoot.IsAllocated)stateRoot.Free();if(frameRoot.IsAllocated)frameRoot.Free();GC.KeepAlive(debugCallback);GC.KeepAlive(stateCallback);GC.KeepAlive(frameCallback);
 }
}
