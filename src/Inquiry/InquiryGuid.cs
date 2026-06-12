namespace Inquiry;

/// <summary>
/// Sequential (UUID version 7) GUID generation for index-friendly client-supplied keys. A v7 GUID
/// leads with a 48-bit Unix-millisecond timestamp, so values generated over time sort roughly
/// ascending — avoiding the page-split churn random v4 keys cause in clustered B-tree indexes.
/// </summary>
public static class InquiryGuid
{
    /// <summary>
    /// Creates a version-7 (time-ordered) GUID. On .NET 9+ this is <c>Guid.CreateVersion7()</c>;
    /// on .NET 8 an RFC 9562-conformant polyfill (48-bit big-endian Unix-millisecond timestamp,
    /// cryptographically random tail, version/variant bits stamped).
    /// </summary>
    public static Guid NewVersion7()
    {
#if NET9_0_OR_GREATER
        return Guid.CreateVersion7();
#else
        Span<byte> bytes = stackalloc byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);

        var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bytes[0] = (byte)(unixMs >> 40);
        bytes[1] = (byte)(unixMs >> 32);
        bytes[2] = (byte)(unixMs >> 24);
        bytes[3] = (byte)(unixMs >> 16);
        bytes[4] = (byte)(unixMs >> 8);
        bytes[5] = (byte)unixMs;

        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x70); // version 7
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC variant

        return new Guid(bytes, bigEndian: true);
#endif
    }
}
