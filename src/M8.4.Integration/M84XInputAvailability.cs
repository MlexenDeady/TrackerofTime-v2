using System.Runtime.InteropServices;

namespace TrackerOfTime.V2.M8_4.Integration;

/// <summary>
/// M8.4 productive preflight for the already-proven M8.3 Direct-XInput adapter.
/// It does not replace or reinterpret the M8.3 input path; it only prevents the
/// productive lifecycle from installing that adapter when Windows has no XInput
/// controller available at launch time.
/// </summary>
public static class M84XInputAvailability
{
    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short ThumbLX;
        public short ThumbLY;
        public short ThumbRX;
        public short ThumbRY;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public XInputGamepad Gamepad;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate uint XInputGetState(uint userIndex, out XInputState state);

    public sealed record ProbeResult(bool XInputAvailable, bool ControllerConnected, int? ControllerIndex, string Evidence);

    public static ProbeResult Probe()
    {
        if (!OperatingSystem.IsWindows())
            return new(false, false, null, "Direct-XInput productive preflight is Windows-only.");

        foreach (var name in new[] { "xinput1_4.dll", "xinput1_3.dll", "xinput9_1_0.dll" })
        {
            if (!NativeLibrary.TryLoad(name, out var library))
                continue;
            try
            {
                if (!NativeLibrary.TryGetExport(library, "XInputGetState", out var export))
                    continue;
                var getState = Marshal.GetDelegateForFunctionPointer<XInputGetState>(export);
                for (uint i = 0; i < 4; i++)
                {
                    if (getState(i, out _) == 0)
                        return new(true, true, (int)i, $"{name}: XInputGetState controller {i} connected.");
                }
                return new(true, false, null, $"{name}: XInputGetState available; no controller 0..3 connected.");
            }
            finally { NativeLibrary.Free(library); }
        }

        return new(false, false, null, "No supported Windows XInput DLL exported XInputGetState.");
    }
}
