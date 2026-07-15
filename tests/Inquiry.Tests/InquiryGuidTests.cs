using System;
using Inquiry;
using Xunit;

namespace Inquiry.Tests;

/// <summary>
/// <see cref="InquiryGuid.NewVersion7"/>: RFC 9562 v7 shape (version + variant bits, leading
/// 48-bit Unix-millisecond timestamp) on every TFM — .NET 8 exercises the polyfill, .NET 9+
/// delegates to <c>Guid.CreateVersion7()</c>.
/// </summary>
public sealed class InquiryGuidTests
{
    [Fact]
    public void NewVersion7HasVersionAndVariantBits()
    {
        var guid = InquiryGuid.NewVersion7();
        var bytes = guid.ToByteArray(bigEndian: true);

        Assert.Equal(0x70, bytes[6] & 0xF0); // version nibble = 7
        Assert.Equal(0x80, bytes[8] & 0xC0); // RFC variant 10xx
    }

    [Fact]
    public void NewVersion7LeadsWithCurrentUnixMillisecondTimestamp()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = InquiryGuid.NewVersion7();
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.InRange(ExtractTimestamp(guid), before, after);
    }

    [Fact]
    public void NewVersion7TimestampsAreNonDecreasing()
    {
        // Compare the extracted 48-bit timestamps with equality tolerance rather than strict
        // whole-GUID byte ordering: the wall clock is not monotonic (NTP steps), and two calls
        // can land in the same millisecond, where the random tail decides byte order.
        var first = ExtractTimestamp(InquiryGuid.NewVersion7());
        System.Threading.Thread.Sleep(10);
        var second = ExtractTimestamp(InquiryGuid.NewVersion7());

        Assert.True(second >= first, $"v7 timestamps regressed: {first} then {second}.");
    }

    private static long ExtractTimestamp(Guid guid)
    {
        var bytes = guid.ToByteArray(bigEndian: true);
        long timestamp = 0;
        for (var i = 0; i < 6; i++)
        {
            timestamp = (timestamp << 8) | bytes[i];
        }

        return timestamp;
    }

    [Fact]
    public void NewVersion7IsUnique()
    {
        var seen = new System.Collections.Generic.HashSet<Guid>();
        for (var i = 0; i < 1000; i++)
        {
            Assert.True(seen.Add(InquiryGuid.NewVersion7()));
        }
    }

    [Fact]
    public void NewSqlServerSequentialHasVersion8AndVariantBits()
    {
        var guid = InquiryGuid.NewSqlServerSequential();
        var bytes = guid.ToByteArray(bigEndian: true);

        Assert.Equal(0x80, bytes[6] & 0xF0); // version nibble = 8
        Assert.Equal(0x80, bytes[8] & 0xC0); // RFC variant 10xx
    }

    [Fact]
    public void NewSqlServerSequentialTimestampInBytes10To15()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var guid = InquiryGuid.NewSqlServerSequential();
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var bytes = guid.ToByteArray(bigEndian: true);
        long timestamp = 0;
        for (var i = 10; i < 16; i++)
        {
            timestamp = (timestamp << 8) | bytes[i];
        }

        Assert.InRange(timestamp, before, after);
    }

    [Fact]
    public void NewSqlServerSequentialSortsAscendingUnderSqlGuidOrdering()
    {
        var first = new System.Data.SqlTypes.SqlGuid(InquiryGuid.NewSqlServerSequential());
        System.Threading.Thread.Sleep(15);
        var second = new System.Data.SqlTypes.SqlGuid(InquiryGuid.NewSqlServerSequential());

        Assert.True(first.CompareTo(second) < 0,
            $"SQL Server ordering regressed: {first.Value} should sort before {second.Value}.");
    }

    [Fact]
    public void NewSqlServerSequentialIsUnique()
    {
        var seen = new System.Collections.Generic.HashSet<Guid>();
        for (var i = 0; i < 1000; i++)
        {
            Assert.True(seen.Add(InquiryGuid.NewSqlServerSequential()));
        }
    }
}
