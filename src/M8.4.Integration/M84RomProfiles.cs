using System.Text;

namespace TrackerOfTime.V2.M8_4.Integration;

public enum M84N64ByteOrder { Unknown, BigEndianZ64, ByteSwappedV64, LittleEndianN64 }
public enum M84GameProfile { UnknownN64, OcarinaOfTimeFamily }
public enum M84TrackerCapability { EmulatorOnly, OoTTrackerCandidate }

public sealed record M84RomInspection(
    string Path,
    bool IsN64Rom,
    M84N64ByteOrder ByteOrder,
    string InternalName,
    M84GameProfile GameProfile,
    M84TrackerCapability TrackerCapability,
    string Evidence);

/// <summary>
/// M8.4 application-level ROM routing only. It does not decode game memory and it does not
/// claim tracker readiness. Frozen M6 + M8.3 remain authoritative once an OoT-family game runs.
/// </summary>
public static class M84RomProfiles
{
    public static M84RomInspection Inspect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path)) throw new FileNotFoundException("ROM file was not found.", path);

        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[0x40];
        var read = stream.Read(header);
        if (read < 4)
            return new(path, false, M84N64ByteOrder.Unknown, "", M84GameProfile.UnknownN64, M84TrackerCapability.EmulatorOnly, "File is too small for an N64 header.");

        var order = DetectByteOrder(header);
        if (order == M84N64ByteOrder.Unknown)
            return new(path, false, order, "", M84GameProfile.UnknownN64, M84TrackerCapability.EmulatorOnly, "N64 header magic is unknown.");

        var normalized = header.ToArray();
        NormalizeHeaderInPlace(normalized, order);
        var name = read >= 0x34 ? Encoding.ASCII.GetString(normalized, 0x20, 20).Trim('\0', ' ') : "";
        var oot = name.Contains("THE LEGEND OF ZELDA", StringComparison.OrdinalIgnoreCase);
        return new(path, true, order, name,
            oot ? M84GameProfile.OcarinaOfTimeFamily : M84GameProfile.UnknownN64,
            oot ? M84TrackerCapability.OoTTrackerCandidate : M84TrackerCapability.EmulatorOnly,
            oot
                ? "OoT-family N64 header candidate. Runtime tracker readiness remains authoritative in Frozen M6/M8.3."
                : "Valid N64 ROM; no supported tracker profile identified, so M8.4 routes it to emulator-only mode.");
    }

    private static M84N64ByteOrder DetectByteOrder(ReadOnlySpan<byte> h) =>
        h[0] == 0x80 && h[1] == 0x37 && h[2] == 0x12 && h[3] == 0x40 ? M84N64ByteOrder.BigEndianZ64 :
        h[0] == 0x37 && h[1] == 0x80 && h[2] == 0x40 && h[3] == 0x12 ? M84N64ByteOrder.ByteSwappedV64 :
        h[0] == 0x40 && h[1] == 0x12 && h[2] == 0x37 && h[3] == 0x80 ? M84N64ByteOrder.LittleEndianN64 :
        M84N64ByteOrder.Unknown;

    private static void NormalizeHeaderInPlace(byte[] h, M84N64ByteOrder order)
    {
        if (order == M84N64ByteOrder.ByteSwappedV64)
            for (var i = 0; i + 1 < h.Length; i += 2) (h[i], h[i + 1]) = (h[i + 1], h[i]);
        else if (order == M84N64ByteOrder.LittleEndianN64)
            for (var i = 0; i + 3 < h.Length; i += 4) (h[i], h[i + 1], h[i + 2], h[i + 3]) = (h[i + 3], h[i + 2], h[i + 1], h[i]);
    }
}
