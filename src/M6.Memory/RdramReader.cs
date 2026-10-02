using System.Buffers.Binary;
using TrackerOfTime.V2.GameCore.Client;

namespace TrackerOfTime.V2.M6.Memory;

public interface IRdramTransport
{
    Task<byte[]> ReadAsync(int offset, int count, TimeSpan timeout, CancellationToken cancellationToken = default);
}

public sealed class GameCoreRdramTransport(GameCoreClient client) : IRdramTransport
{
    public async Task<byte[]> ReadAsync(int offset, int count, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count < 1 || count > 65536) throw new ArgumentOutOfRangeException(nameof(count));
        if (offset > (8 * 1024 * 1024) - count) throw new ArgumentOutOfRangeException(nameof(offset));
        var payload = await client.CallAsync("ReadRdram", new { offset, count }, timeout, cancellationToken);
        int returnedOffset = payload.GetProperty("offset").GetInt32();
        int returnedCount = payload.GetProperty("count").GetInt32();
        if (returnedOffset != offset || returnedCount != count)
            throw new InvalidDataException($"ReadRdram response range mismatch: requested {offset:X}/{count}, returned {returnedOffset:X}/{returnedCount}.");
        string? b64 = payload.GetProperty("dataBase64").GetString();
        if (string.IsNullOrWhiteSpace(b64)) throw new InvalidDataException("ReadRdram response has no dataBase64.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(b64); }
        catch (FormatException ex) { throw new InvalidDataException("ReadRdram response dataBase64 is invalid.", ex); }
        if (bytes.Length != count) throw new InvalidDataException($"ReadRdram payload length mismatch: expected {count}, got {bytes.Length}.");
        return bytes;
    }
}

/// <summary>
/// M6.2 typed view over the exact raw bytes returned by the frozen M5 ReadRdram operation.
/// Numeric reads intentionally use little-endian host interpretation because that is what
/// V1/M10 WindowsProcessMemory + BitConverter used over Mupen's word-swapped RDRAM storage.
/// Byte reads are not address-xor adjusted; V1 offsets already address the raw layout.
/// </summary>
public sealed class GoldenRdramReader(IRdramTransport transport, TimeSpan? defaultTimeout = null)
{
    private readonly TimeSpan timeout = defaultTimeout ?? TimeSpan.FromSeconds(5);

    public Task<byte[]> ReadBytesAsync(int offset, int length, CancellationToken ct = default)
        => transport.ReadAsync(offset, length, timeout, ct);

    public async Task<byte> ReadByteAsync(int offset, CancellationToken ct = default)
        => (await ReadExact(offset, 1, ct))[0];

    public async Task<short> ReadInt16Async(int offset, CancellationToken ct = default)
        => BinaryPrimitives.ReadInt16LittleEndian(await ReadExact(offset, 2, ct));

    public async Task<ushort> ReadUInt16Async(int offset, CancellationToken ct = default)
        => BinaryPrimitives.ReadUInt16LittleEndian(await ReadExact(offset, 2, ct));

    public async Task<int> ReadInt32Async(int offset, CancellationToken ct = default)
        => BinaryPrimitives.ReadInt32LittleEndian(await ReadExact(offset, 4, ct));

    public async Task<uint> ReadUInt32Async(int offset, CancellationToken ct = default)
        => BinaryPrimitives.ReadUInt32LittleEndian(await ReadExact(offset, 4, ct));

    private async Task<byte[]> ReadExact(int offset, int count, CancellationToken ct)
    {
        byte[] bytes = await transport.ReadAsync(offset, count, timeout, ct);
        if (bytes.Length != count) throw new InvalidDataException($"Typed RDRAM read expected {count} bytes, got {bytes.Length}.");
        return bytes;
    }
}
