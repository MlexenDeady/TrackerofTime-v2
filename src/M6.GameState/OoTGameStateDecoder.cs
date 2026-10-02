using TrackerOfTime.V2.M6.Memory;

namespace TrackerOfTime.V2.M6.GameState;

public enum ZeldaSignatureState { Missing, Partial, Full }
public enum OoTAge { Unknown, Adult, Young }

public sealed record PlayerPosition(float X, float Y, float Z);

public sealed record OoTGameStateSnapshot(
    ZeldaSignatureState Signature,
    byte GameState,
    bool IsLoaded,
    OoTAge Age,
    short LocationCode,
    byte MagicRaw,
    bool CanUseMagic,
    ushort MaxLifeRaw,
    int MaxLifeHearts,
    byte GoldSkulltulaRaw,
    PlayerPosition? PlayerPosition,
    short? WastelandRotation);

/// <summary>
/// M6.3 read-only decoder for confirmed vanilla OoT game/player state from the M6.1 Golden Reference.
/// It intentionally does not decode inventory, quest, checks, MQ, ER, randomizer settings, or CurrentRoom.
/// </summary>
public sealed class OoTGameStateDecoder(GoldenRdramReader reader)
{
    internal const int Signature1Offset = 0x11A5EC;
    internal const int Signature2Offset = 0x11A5F2;
    internal const int GameStateOffset = 0x11B92C;
    internal const int AgeOffset = 0x11A5D4;
    internal const int MagicOffset = 0x11A601;
    internal const int MaxLifeOffset = 0x11A5FC;
    internal const int GoldSkulltulaOffset = 0x11A6A2;
    internal const int LocationOffset = 0x1C8546;
    internal const int PlayerXOffset = 0x1DAA54;
    internal const int PlayerYOffset = 0x1DAA58;
    internal const int PlayerZOffset = 0x1DAA5C;
    internal const int WastelandRotationOffset = 0x1DAA74;

    public async Task<OoTGameStateSnapshot> CaptureAsync(CancellationToken ct = default)
    {
        int sig1 = await reader.ReadInt32Async(Signature1Offset, ct);
        short sig2 = await reader.ReadInt16Async(Signature2Offset, ct);
        var signature = DecodeSignature(sig1, sig2);

        byte gameState = await reader.ReadByteAsync(GameStateOffset, ct);
        byte ageRaw = await reader.ReadByteAsync(AgeOffset, ct);
        short location = await reader.ReadInt16Async(LocationOffset, ct);
        byte magic = await reader.ReadByteAsync(MagicOffset, ct);
        ushort maxLife = await reader.ReadUInt16Async(MaxLifeOffset, ct);
        byte gs = await reader.ReadByteAsync(GoldSkulltulaOffset, ct);

        int xBits = await reader.ReadInt32Async(PlayerXOffset, ct);
        int yBits = await reader.ReadInt32Async(PlayerYOffset, ct);
        int zBits = await reader.ReadInt32Async(PlayerZOffset, ct);
        PlayerPosition? position = DecodePosition(xBits, yBits, zBits);

        short? rotation = location == 94
            ? await reader.ReadInt16Async(WastelandRotationOffset, ct)
            : null;

        return new OoTGameStateSnapshot(
            signature,
            gameState,
            signature == ZeldaSignatureState.Full && gameState == 0,
            DecodeAge(ageRaw),
            location,
            magic,
            magic is 1 or 2,
            maxLife,
            maxLife / 16,
            gs,
            position,
            rotation);
    }

    public static ZeldaSignatureState DecodeSignature(int first, short second)
    {
        bool a = first == 0x5A454C44;
        bool b = (ushort)second == 0x415A;
        if (a && b) return ZeldaSignatureState.Full;
        if (a || b) return ZeldaSignatureState.Partial;
        return ZeldaSignatureState.Missing;
    }

    public static OoTAge DecodeAge(byte raw) => raw switch
    {
        0 => OoTAge.Adult,
        1 => OoTAge.Young,
        _ => OoTAge.Unknown
    };

    public static PlayerPosition? DecodePosition(int xBits, int yBits, int zBits)
    {
        float x = BitConverter.Int32BitsToSingle(xBits);
        float y = BitConverter.Int32BitsToSingle(yBits);
        float z = BitConverter.Int32BitsToSingle(zBits);
        if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) return null;
        return new PlayerPosition(x, y, z);
    }
}
