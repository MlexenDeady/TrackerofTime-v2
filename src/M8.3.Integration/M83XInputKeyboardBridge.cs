using System.Runtime.InteropServices;

namespace TrackerOfTime.V2.M8_3.Integration;

/// <summary>M8.3-only XInput -> proven keyboard-path bridge. Frozen M5/M6 remain untouched.</summary>
public sealed class M83XInputKeyboardBridge : IAsyncDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct XINPUT_GAMEPAD { public ushort wButtons; public byte bLeftTrigger,bRightTrigger; public short sThumbLX,sThumbLY,sThumbRX,sThumbRY; }
    [StructLayout(LayoutKind.Sequential)] struct XINPUT_STATE { public uint dwPacketNumber; public XINPUT_GAMEPAD Gamepad; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] delegate uint XInputGetState(uint index, out XINPUT_STATE state);

    // Win32 INPUT is 40 bytes on x64. FIX2J's union only contained KEYBDINPUT, making INPUT 32 bytes;
    // SendInput therefore received the wrong cbSize and could reject every synthesized key event.
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk,wScan; public uint dwFlags,time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct HARDWAREINPUT { public uint uMsg; public ushort wParamL,wParamH; }
    [DllImport("user32.dll", SetLastError=true)] static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    const uint KEYEVENTF_KEYUP=0x0002;
    const ushort DPAD_UP=0x0001,DPAD_DOWN=0x0002,DPAD_LEFT=0x0004,DPAD_RIGHT=0x0008,START=0x0010,LB=0x0100,RB=0x0200,A=0x1000,X=0x4000;
    readonly IntPtr lib; readonly XInputGetState getState; readonly uint index; readonly CancellationTokenSource cts=new(); readonly Task loop; readonly HashSet<ushort> down=new();
    int loggedTransitions;
    public uint ControllerIndex=>index;
    public static int NativeInputSize=>Marshal.SizeOf<INPUT>();

    M83XInputKeyboardBridge(IntPtr l,XInputGetState g,uint i){lib=l;getState=g;index=i;loop=Task.Run(Run);}
    public static bool TryStart(out M83XInputKeyboardBridge? bridge,out string evidence){
        bridge=null; evidence=""; if(!OperatingSystem.IsWindows()){evidence="Windows only";return false;}
        foreach(var dll in new[]{"xinput1_4.dll","xinput1_3.dll","xinput9_1_0.dll"}){
            if(!NativeLibrary.TryLoad(dll,out var l)) continue;
            if(!NativeLibrary.TryGetExport(l,"XInputGetState",out var p)){NativeLibrary.Free(l);continue;}
            var g=Marshal.GetDelegateForFunctionPointer<XInputGetState>(p);
            for(uint i=0;i<4;i++) if(g(i,out var s)==0){bridge=new(l,g,i);evidence=$"{dll}; userIndex={i}; packet={s.dwPacketNumber}; Win32INPUTSize={NativeInputSize}";return true;}
            NativeLibrary.Free(l);
        }
        evidence="No connected XInput controller on user indices 0..3"; return false;
    }
    async Task Run(){
        try{while(!cts.IsCancellationRequested){if(getState(index,out var s)==0) Apply(s.Gamepad); else ReleaseAll(); await Task.Delay(8,cts.Token);}}
        catch(OperationCanceledException){} finally{ReleaseAll();}
    }
    void Apply(XINPUT_GAMEPAD g){
        Set(0x4A,(g.wButtons&A)!=0,"A -> J/N64-A"); Set(0x4B,(g.wButtons&X)!=0,"X -> K/N64-B"); Set(0x0D,(g.wButtons&START)!=0,"Start -> Enter");
        Set(0x51,g.bLeftTrigger>40,"LT -> Q/Z"); Set(0x45,(g.wButtons&RB)!=0,"RB -> E/R"); Set(0xA0,(g.wButtons&LB)!=0,"LB -> LShift/L");
        Set(0x54,(g.wButtons&DPAD_UP)!=0,"DPadUp -> T"); Set(0x47,(g.wButtons&DPAD_DOWN)!=0,"DPadDown -> G"); Set(0x46,(g.wButtons&DPAD_LEFT)!=0,"DPadLeft -> F"); Set(0x48,(g.wButtons&DPAD_RIGHT)!=0,"DPadRight -> H");
        const int dz=9000; Set(0x41,g.sThumbLX < -dz,"LX- -> A"); Set(0x44,g.sThumbLX > dz,"LX+ -> D"); Set(0x57,g.sThumbLY > dz,"LY+ -> W"); Set(0x53,g.sThumbLY < -dz,"LY- -> S");
        Set(0x25,g.sThumbRX < -dz,"RX- -> Left/C"); Set(0x27,g.sThumbRX > dz,"RX+ -> Right/C"); Set(0x26,g.sThumbRY > dz,"RY+ -> Up/C"); Set(0x28,g.sThumbRY < -dz,"RY- -> Down/C");
    }
    void Set(ushort vk,bool pressed,string label){
        if(pressed){if(down.Add(vk)) Key(vk,false,label);} else if(down.Remove(vk)) Key(vk,true,label);
    }
    void Key(ushort vk,bool up,string label){
        var a=new[]{new INPUT{type=1,U=new InputUnion{ki=new KEYBDINPUT{wVk=vk,dwFlags=up?KEYEVENTF_KEYUP:0}}}};
        Marshal.SetLastPInvokeError(0);
        var sent=SendInput(1,a,NativeInputSize);
        var err=sent==1?0:Marshal.GetLastPInvokeError();
        if(Interlocked.Increment(ref loggedTransitions)<=40 || sent!=1)
            Console.WriteLine($"M8.3 INPUT FIX2K EVENT: {label} {(up?"UP":"DOWN")}; vk=0x{vk:X2}; SendInput={sent}; error={err}; cbSize={NativeInputSize}");
    }
    void ReleaseAll(){foreach(var k in down.ToArray())Key(k,true,$"release 0x{k:X2}");down.Clear();}
    public async ValueTask DisposeAsync(){cts.Cancel();try{await loop;}catch{}cts.Dispose();NativeLibrary.Free(lib);}
}
