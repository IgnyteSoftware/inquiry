namespace Inquiry;

/// <summary>
/// Sequential GUID generation for index-friendly client-supplied keys. The default
/// (<see cref="NewVersion7"/>) uses UUIDv7; SQL Server gets a custom layout
/// (<see cref="NewSqlServerSequential"/>) that places the timestamp where
/// <c>uniqueidentifier</c> compares first, avoiding the page-split churn random keys cause.
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

    /// <summary>
    /// Creates a sequential GUID optimized for SQL Server <c>uniqueidentifier</c> clustered-index
    /// ordering. SQL Server compares bytes [10..15] first (Data4[2..7]), so the 48-bit Unix-ms
    /// timestamp is placed there instead of the leading position used by UUIDv7. The version nibble
    /// is set to 8 (RFC 9562 custom/vendor format) to avoid misrepresenting the non-standard layout.
    /// </summary>
    public static Guid NewSqlServerSequential()
    {
        Span<byte> bytes = stackalloc byte[16];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);

        var unixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bytes[10] = (byte)(unixMs >> 40);
        bytes[11] = (byte)(unixMs >> 32);
        bytes[12] = (byte)(unixMs >> 24);
        bytes[13] = (byte)(unixMs >> 16);
        bytes[14] = (byte)(unixMs >> 8);
        bytes[15] = (byte)unixMs;

        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80); // version 8
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC variant

        return new Guid(bytes, bigEndian: true);
    }
}
