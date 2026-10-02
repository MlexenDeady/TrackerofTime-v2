using System.Text;

namespace TrackerOfTime.V2.M8_3.Integration;

/// <summary>
/// M8.3-only diagnostic input preparation/inspection. It never changes Frozen M5/M6 sources.
/// </summary>
public static class M83DiagnosticInput
{
    public static string PrepareKeyboardFallback(string runtimeRoot)
    {
        // FIX2E: make Controller 1 deterministic. Input-SDL mode 0 means fully manual;
        // device -1 means keyboard/mouse only. This avoids Full-Auto selecting a detected
        // joystick and bypassing the keyboard fallback. M5/M6 frozen code is untouched.
        var configDir = Path.Combine(runtimeRoot, "config");
        Directory.CreateDirectory(configDir);
        var cfg = Path.Combine(configDir, "mupen64plus.cfg");

        var text = """
[Input-SDL-Control1]
version = 2.000000
mode = 0
device = -1
name = Keyboard
plugged = True
plugin = 2
mouse = False
MouseSensitivity = "2.00,2.00"
DPad R = key(104)
DPad L = key(102)
DPad D = key(103)
DPad U = key(116)
Start = key(13)
Z Trig = key(113)
B Button = key(107)
A Button = key(106)
C Button R = key(275)
C Button L = key(276)
C Button D = key(274)
C Button U = key(273)
R Trig = key(101)
L Trig = key(304)
Mempak switch = key(44)
Rumblepak switch = key(46)
X Axis = key(97,100)
Y Axis = key(119,115)
AnalogDeadzone = "4096,4096"
AnalogPeak = "32768,32768"

[Input-SDL-Control2]
version = 2.000000
mode = 2

[Input-SDL-Control3]
version = 2.000000
mode = 2

[Input-SDL-Control4]
version = 2.000000
mode = 2
""";
        File.WriteAllText(cfg, text, new UTF8Encoding(false));
        return cfg;
    }


    public sealed record ControllerDiscovery(bool Found, string Name, string PnpDeviceId, string Transport, string Evidence);
    public sealed record SdlDevice(int Index, string Name, string Guid, int Axes, int Buttons, int Hats, bool IsGameController);
    public sealed record SdlDiscovery(bool Available, string Library, string Error, IReadOnlyList<SdlDevice> Devices)
    {
        public SdlDevice? Preferred => Devices.FirstOrDefault(d => d.IsGameController) ?? Devices.FirstOrDefault();
    }

    public static SdlDiscovery DiscoverSdlControllers(string runtimeRoot)
    {
        if (!OperatingSystem.IsWindows()) return new(false, "", "SDL discovery is Windows-only in FIX2H.", Array.Empty<SdlDevice>());
        var candidates = Directory.Exists(runtimeRoot)
            ? Directory.EnumerateFiles(runtimeRoot, "*.dll", SearchOption.AllDirectories)
                .Where(x => Path.GetFileName(x).Equals("SDL2.dll", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(x).Equals("SDL.dll", StringComparison.OrdinalIgnoreCase))
                .ToArray()
            : Array.Empty<string>();
        foreach (var dll in candidates)
        {
            if (!System.Runtime.InteropServices.NativeLibrary.TryLoad(dll, out var lib)) continue;
            try
            {
                T Get<T>(string n) where T : Delegate => System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<T>(System.Runtime.InteropServices.NativeLibrary.GetExport(lib,n));
                var init=Get<SDL_Init>("SDL_Init"); var quit=Get<SDL_Quit>("SDL_Quit"); var num=Get<SDL_NumJoysticks>("SDL_NumJoysticks");
                var name=Get<SDL_JoystickNameForIndex>("SDL_JoystickNameForIndex"); var open=Get<SDL_JoystickOpen>("SDL_JoystickOpen"); var close=Get<SDL_JoystickClose>("SDL_JoystickClose");
                var axes=Get<SDL_JoystickNumAxes>("SDL_JoystickNumAxes"); var buttons=Get<SDL_JoystickNumButtons>("SDL_JoystickNumButtons"); var hats=Get<SDL_JoystickNumHats>("SDL_JoystickNumHats");
                var guid=Get<SDL_JoystickGetDeviceGUID>("SDL_JoystickGetDeviceGUID"); var guidStr=Get<SDL_JoystickGetGUIDString>("SDL_JoystickGetGUIDString");
                SDL_IsGameController? isgc=null; try { isgc=Get<SDL_IsGameController>("SDL_IsGameController"); } catch { }
                const uint SDL_INIT_JOYSTICK=0x00000200, SDL_INIT_GAMECONTROLLER=0x00002000;
                var rc=init(SDL_INIT_JOYSTICK|SDL_INIT_GAMECONTROLLER); if(rc!=0) return new(false,dll,$"SDL_Init returned {rc}",Array.Empty<SdlDevice>());
                try {
                    var list=new List<SdlDevice>(); var n=num();
                    for(int i=0;i<n;i++) {
                        var np=name(i); var nm=np==IntPtr.Zero?$"SDL joystick {i}":System.Runtime.InteropServices.Marshal.PtrToStringUTF8(np)??$"SDL joystick {i}";
                        var j=open(i); int a=0,b=0,h=0; if(j!=IntPtr.Zero){a=axes(j);b=buttons(j);h=hats(j);close(j);}
                        var g=guid(i); var sb=new StringBuilder(64); guidStr(g,sb,64);
                        list.Add(new(i,nm,sb.ToString(),a,b,h,isgc?.Invoke(i)!=0));
                    }
                    return new(true,dll,"",list);
                } finally { quit(); }
            } catch(Exception ex) { return new(false,dll,ex.Message,Array.Empty<SdlDevice>()); }
            finally { System.Runtime.InteropServices.NativeLibrary.Free(lib); }
        }
        return new(false,"","No SDL2.dll/SDL.dll found under provisioned Mupen runtime.",Array.Empty<SdlDevice>());
    }

    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)] delegate int SDL_Init(uint flags);
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)] delegate void SDL_Quit();
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)] delegate int SDL_NumJoysticks();
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)] delegate IntPtr SDL_JoystickNameForIndex(int index);
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)] delegate IntPtr SDL_JoystickOpen(int index);
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)] delegate void SDL_JoystickClose(IntPtr joystick);
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)] delegate int SDL_JoystickNumAxes(IntPtr joystick);
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)] delegate int SDL_JoystickNumButtons(IntPtr joystick);
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)] delegate int SDL_JoystickNumHats(IntPtr joystick);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] struct SDL_JoystickGUID { public ulong a; public ulong b; }
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)] delegate SDL_JoystickGUID SDL_JoystickGetDeviceGUID(int index);
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)] delegate void SDL_JoystickGetGUIDString(SDL_JoystickGUID guid, StringBuilder pszGUID, int cbGUID);
    [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.Cdecl)] delegate int SDL_IsGameController(int index);

    public static ControllerDiscovery DiscoverController()
    {
        if (!OperatingSystem.IsWindows()) return new(false, "", "", "", "Windows controller discovery is only available on Windows.");
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            // XUSB/XInput devices use the Microsoft XNA composite class. Also accept common HID game-controller names
            // as discovery evidence; no vendor/product ID is hard-coded.
            psi.ArgumentList.Add("-NoProfile");
            psi.ArgumentList.Add("-ExecutionPolicy"); psi.ArgumentList.Add("Bypass");
            psi.ArgumentList.Add("-Command");
            psi.ArgumentList.Add("$d=Get-CimInstance Win32_PnPEntity | Where-Object { $_.Status -eq 'OK' -and ( $_.ClassGuid -eq '{D61CA365-5AF4-4486-998B-9DB4734C6CA3}' -or $_.PNPClass -eq 'XnaComposite' -or ( $_.PNPClass -eq 'HIDClass' -and $_.Name -match 'controller|gamepad|xbox' ) ) } | Select-Object -First 1 Name,PNPDeviceID,ClassGuid; if($d){ ($d.Name -replace '\\|','/')+'|'+($d.PNPDeviceID -replace '\\|','/')+'|'+$d.ClassGuid }");
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var output = proc.StandardOutput.ReadToEnd().Trim();
            var error = proc.StandardError.ReadToEnd().Trim();
            proc.WaitForExit(10000);
            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
                return new(false, "", "", "", string.IsNullOrWhiteSpace(error) ? "No active Windows game controller discovered." : error);
            var parts = output.Split('|');
            var name = parts.ElementAtOrDefault(0)?.Trim() ?? "Windows Game Controller";
            var id = parts.ElementAtOrDefault(1)?.Trim() ?? "";
            var transport = id.StartsWith("USB", StringComparison.OrdinalIgnoreCase) ? "USB/XInput (wireless dongle or cable)" : "Windows game-controller device";
            return new(true, name, id, transport, "Windows PnP/XInput discovery");
        }
        catch (Exception ex) { return new(false, "", "", "", ex.Message); }
    }

    public static string PrepareControllerAuto(string runtimeRoot, ControllerDiscovery discovery, int sdlDeviceIndex = 0, string? sdlName = null)
    {
        // FIX2G: deterministic XInput-family mapping after Windows auto-discovery.
        // The device is still SDL joystick 0 for this diagnostic, but unlike FIX2F we provide the complete
        // mapping instead of relying on Input-SDL Full Auto to synthesize it. No M5/M6 source is changed.
        var configDir = Path.Combine(runtimeRoot, "config");
        Directory.CreateDirectory(configDir);
        var cfg = Path.Combine(configDir, "mupen64plus.cfg");
        if (!discovery.Found) return PrepareKeyboardFallback(runtimeRoot);
        var safeName = (sdlName ?? discovery.Name).Replace("\"", "'");
        var text = $"""
[Input-SDL-Control1]
version = 2.000000
mode = 0
device = {sdlDeviceIndex}
name = "{safeName}"
plugged = True
plugin = 2
mouse = False
MouseSensitivity = "2.00,2.00"
DPad R = hat(0 Right)
DPad L = hat(0 Left)
DPad D = hat(0 Down)
DPad U = hat(0 Up)
Start = button(7)
Z Trig = axis(2+)
B Button = button(2)
A Button = button(0)
C Button R = axis(3+)
C Button L = axis(3-)
C Button D = axis(4+)
C Button U = axis(4-)
R Trig = button(5)
L Trig = button(4)
Mempak switch = button(6)
Rumblepak switch = button(8)
X Axis = axis(0-,0+)
Y Axis = axis(1-,1+)
AnalogDeadzone = "4096,4096"
AnalogPeak = "32768,32768"

[Input-SDL-Control2]
version = 2.000000
mode = 2
device = -1
plugged = False

[Input-SDL-Control3]
version = 2.000000
mode = 2
device = -1
plugged = False

[Input-SDL-Control4]
version = 2.000000
mode = 2
device = -1
plugged = False
""";
        File.WriteAllText(cfg, text, new UTF8Encoding(false));
        return cfg;
    }

    public static IReadOnlyList<string> InspectActiveControl1(string runtimeRoot)
    {
        var lines = new List<string>();
        var cfg = Path.Combine(runtimeRoot, "config", "mupen64plus.cfg");
        var shared = Path.Combine(runtimeRoot, "shared", "InputAutoCfg.ini");
        lines.Add($"runtime={runtimeRoot}");
        lines.Add($"ConfigDir={Path.Combine(runtimeRoot, "config")}");
        lines.Add($"SharedDataDir={Path.Combine(runtimeRoot, "shared")}");
        lines.Add($"InputAutoCfg={shared}; exists={File.Exists(shared)}");
        lines.Add($"mupen64plus.cfg={cfg}; exists={File.Exists(cfg)}");
        if (!File.Exists(cfg))
        {
            lines.Add("ACTIVE CONTROL1: unavailable because mupen64plus.cfg was not created.");
            return lines;
        }

        var section = ReadSection(File.ReadAllLines(cfg), "Input-SDL-Control1");
        if (section.Count == 0)
        {
            lines.Add("ACTIVE CONTROL1: [Input-SDL-Control1] section NOT FOUND.");
            return lines;
        }

        lines.Add("ACTIVE CONTROL1 BEGIN");
        lines.AddRange(section);
        lines.Add("ACTIVE CONTROL1 END");
        return lines;
    }

    private static List<string> ReadSection(string[] source, string sectionName)
    {
        var result = new List<string>();
        bool active = false;
        foreach (var raw in source)
        {
            var trimmed = raw.Trim();
            if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
            {
                if (active) break;
                active = trimmed.Equals($"[{sectionName}]", StringComparison.OrdinalIgnoreCase);
                if (active) result.Add(trimmed);
                continue;
            }
            if (active && trimmed.Length > 0 && !trimmed.StartsWith('#') && !trimmed.StartsWith(';')) result.Add(trimmed);
        }
        return result;
    }

    public const string KeyboardHelp = "WASD=Stick, J=A, K=B, Enter=Start, Arrows=C-buttons, Q=Z, E=R, LeftShift=L, T/F/G/H=D-pad";
}
