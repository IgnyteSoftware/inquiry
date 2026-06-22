using System.Collections.Generic;
using Inquiry.Parameters;
using Microsoft.Data.Sqlite;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// Unit tests for the <c>Compare.In</c> runtime expansion. The enum-coercion case guards the
/// review finding: enum elements must be bound as their underlying integral value (matching the
/// scalar binder), not as a boxed enum, so enum-strict providers (e.g. Npgsql) accept them.
/// </summary>
public class InquiryInExpansionTests
{
    private enum Priority
    {
        Low = 0,
        High = 7,
    }

    [Fact]
    public void Expand_EnumElements_CoercesToUnderlyingIntegralValue()
    {
        using var command = new SqliteCommand { CommandText = "SELECT * FROM t WHERE p IN (@p)" };

        InquiryInExpansion.Expand(command, "@p", new List<Priority> { Priority.Low, Priority.High });

        Assert.Equal("SELECT * FROM t WHERE p IN (@p0, @p1)", command.CommandText);
        Assert.Equal(2, command.Parameters.Count);
        Assert.IsType<int>(command.Parameters[0].Value);
        Assert.Equal(0, command.Parameters[0].Value);
        Assert.IsType<int>(command.Parameters[1].Value);
        Assert.Equal(7, command.Parameters[1].Value);
    }

    [Fact]
    public void Expand_NullableEnumElements_CoercesNonNullToUnderlying()
    {
        using var command = new SqliteCommand { CommandText = "SELECT * FROM t WHERE p IN (@p)" };

        InquiryInExpansion.Expand(command, "@p", new List<Priority?> { Priority.High, null });

        Assert.Equal(2, command.Parameters.Count);
        Assert.IsType<int>(command.Parameters[0].Value);
        Assert.Equal(7, command.Parameters[0].Value);
        Assert.Equal(System.DBNull.Value, command.Parameters[1].Value);
    }

    [Fact]
    public void Expand_ScalarElements_BucketsToNextPowerOfTwoRepeatingLastValue()
    {
        using var command = new SqliteCommand { CommandText = "SELECT * FROM t WHERE c IN (@c)" };

        // Three real elements pad up to the next power-of-two bucket (4); the padding slot repeats the last
        // element value (5), a no-op duplicate that doesn't change which rows match.
        InquiryInExpansion.Expand(command, "@c", new List<int> { 3, 4, 5 });

        Assert.Equal("SELECT * FROM t WHERE c IN (@c0, @c1, @c2, @c3)", command.CommandText);
        Assert.Equal(4, command.Parameters.Count);
        Assert.Equal(3, command.Parameters[0].Value);
        Assert.Equal(4, command.Parameters[1].Value);
        Assert.Equal(5, command.Parameters[2].Value);
        Assert.Equal(5, command.Parameters[3].Value); // padding repeats the last value
    }

    [Fact]
    public void Expand_WithDeclaredSize_StampsEveryElementIncludingPaddedSlots()
    {
        using var command = new SqliteCommand { CommandText = "SELECT * FROM t WHERE c IN (@c)" };

        // Three real elements pad to bucket 4. The declared Size (#102) must land on every element —
        // including the padded 4th slot — so the whole expanded list renders one uniform sp_executesql
        // signature on SQL Server. (Sqlite ignores Size at execution; this asserts the binding metadata.)
        InquiryInExpansion.Expand(command, "@c", new List<string> { "a", "b", "c" }, 2000, System.Data.DbType.String, size: 64);

        Assert.Equal(4, command.Parameters.Count);
        for (var i = 0; i < command.Parameters.Count; i++)
        {
            Assert.Equal(64, command.Parameters[i].Size);
        }
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(5, 8)]
    [InlineData(9, 16)]
    public void Expand_PadsListLengthToNextPowerOfTwoBucket(int elementCount, int expectedBucket)
    {
        using var command = new SqliteCommand { CommandText = "SELECT * FROM t WHERE c IN (@c)" };
        var values = new List<int>();
        for (var i = 0; i < elementCount; i++)
        {
            values.Add(i);
        }

        InquiryInExpansion.Expand(command, "@c", values);

        Assert.Equal(expectedBucket, command.Parameters.Count);
        var names = new string[expectedBucket];
        for (var i = 0; i < expectedBucket; i++)
        {
            names[i] = "@c" + i;
        }

        var expected = "SELECT * FROM t WHERE c IN (" + string.Join(", ", names) + ")";
        Assert.Equal(expected, command.CommandText);
    }

    [Fact]
    public void ExpandNotIn_Buckets_AndRepeatsANonNullPaddingValue()
    {
        using var command = new SqliteCommand { CommandText = "SELECT * FROM t WHERE (c NOT IN (@c))" };

        // 3 -> bucket 4. NOT IN must never pad with NULL (it would make the predicate UNKNOWN); padding
        // repeats the last real value, a no-op for NOT IN (col<>v AND col<>v).
        InquiryInExpansion.ExpandNotIn(command, "@c", new List<int> { 10, 20, 30 });

        Assert.Equal("SELECT * FROM t WHERE (c NOT IN (@c0, @c1, @c2, @c3))", command.CommandText);
        Assert.Equal(4, command.Parameters.Count);
        Assert.Equal(30, command.Parameters[3].Value);
    }

    [Fact]
    public void Expand_AllNullElements_DoesNotBucket()
    {
        using var command = new SqliteCommand { CommandText = "SELECT * FROM t WHERE p IN (@p)" };

        // No non-null element to repeat, so the (degenerate) all-null list is left at its exact length
        // rather than padded with NULL.
        InquiryInExpansion.Expand(command, "@p", new List<Priority?> { null, null, null });

        Assert.Equal("SELECT * FROM t WHERE p IN (@p0, @p1, @p2)", command.CommandText);
        Assert.Equal(3, command.Parameters.Count);
    }

    [Fact]
    public void Expand_BucketAboveOracleInListCeiling_LeavesListAtExactLength()
    {
        using var command = new SqliteCommand { CommandText = "SELECT * FROM t WHERE c IN (@c)" };
        var values = new List<int>();
        for (var i = 0; i < 600; i++) // next power of two is 1024, above the 1000 IN-list ceiling
        {
            values.Add(i);
        }

        InquiryInExpansion.Expand(command, "@c", values);

        // Padding past 1000 would raise ORA-01795 on Oracle, so the exact list is kept instead.
        Assert.Equal(600, command.Parameters.Count);
    }

    [Fact]
    public void Expand_BucketAtOrBelowOracleInListCeiling_StillPads()
    {
        using var command = new SqliteCommand { CommandText = "SELECT * FROM t WHERE c IN (@c)" };
        var values = new List<int>();
        for (var i = 0; i < 500; i++) // next power of two is 512, within the 1000 ceiling
        {
            values.Add(i);
        }

        InquiryInExpansion.Expand(command, "@c", values);

        Assert.Equal(512, command.Parameters.Count);
    }

    [Fact]
    public void Expand_PaddingThatWouldExceedTheCap_LeavesListAtExactLength()
    {
        using var command = new SqliteCommand { CommandText = "SELECT * FROM t WHERE c IN (@c)" };

        // 3 real elements fit a cap of 3, but the bucket (4) would exceed it — padding is skipped rather
        // than throwing, so the exact list is emitted.
        InquiryInExpansion.Expand(command, "@c", new List<int> { 1, 2, 3 }, maxParameterCount: 3);

        Assert.Equal("SELECT * FROM t WHERE c IN (@c0, @c1, @c2)", command.CommandText);
        Assert.Equal(3, command.Parameters.Count);
    }

    [Fact]
    public void Expand_EmptyCollection_RewritesToNoRowsSentinel()
    {
        using var command = new SqliteCommand { CommandText = "SELECT * FROM t WHERE c IN (@c)" };

        InquiryInExpansion.Expand(command, "@c", new List<int>());

        Assert.Equal("SELECT * FROM t WHERE c IN (NULL)", command.CommandText);
        Assert.Equal(0, command.Parameters.Count);
    }

    [Fact]
    public void Expand_WithDbType_StampsDbTypeOnEachParameter()
    {
        using var command = new SqliteCommand { CommandText = "SELECT * FROM t WHERE d IN (@d)" };

        InquiryInExpansion.Expand(
            command,
            "@d",
            new List<System.DateTime> { new(2024, 1, 1), new(2024, 2, 2) },
            InquiryOptions.DefaultMaxParametersPerCommand,
            dbType: System.Data.DbType.DateTime2);

        Assert.Equal(2, command.Parameters.Count);
        Assert.Equal(System.Data.DbType.DateTime2, command.Parameters[0].DbType);
        Assert.Equal(System.Data.DbType.DateTime2, command.Parameters[1].DbType);
    }

    [Fact]
    public void ExpandNotIn_WithDbType_StampsDbTypeOnEachParameter()
    {
        using var command = new SqliteCommand { CommandText = "SELECT * FROM t WHERE (d NOT IN (@d))" };

        InquiryInExpansion.ExpandNotIn(
            command,
            "@d",
            new List<System.DateTime> { new(2024, 1, 1) },
            InquiryOptions.DefaultMaxParametersPerCommand,
            dbType: System.Data.DbType.DateTime2);

        Assert.Single(command.Parameters);
        Assert.Equal(System.Data.DbType.DateTime2, command.Parameters[0].DbType);
    }

    [Fact]
    public void Expand_NoDbType_LeavesParameterDbTypeAtProviderDefault()
    {
        using var command = new SqliteCommand { CommandText = "SELECT * FROM t WHERE c IN (@c)" };

        // The default SqliteParameter.DbType is String; passing no dbType must not stamp one.
        InquiryInExpansion.Expand(command, "@c", new List<int> { 1 });

        Assert.Single(command.Parameters);
        Assert.Equal(System.Data.DbType.String, command.Parameters[0].DbType);
    }

    [Fact]
    public void Expand_UnsignedElements_ReinterpretToSameWidthSignedStorage()
    {
        using var command = new SqliteCommand { CommandText = "SELECT * FROM t WHERE c IN (@c)" };

        // uint > int.MaxValue must persist as the same bit pattern via its signed partner so it matches
        // the value the scalar binder stored for that column (uint 3_000_000_000 -> int -1_294_967_296).
        InquiryInExpansion.Expand(command, "@c", new List<uint> { 7u, 3_000_000_000u });

        Assert.Equal(2, command.Parameters.Count);
        Assert.IsType<int>(command.Parameters[0].Value);
        Assert.Equal(7, command.Parameters[0].Value);
        Assert.IsType<int>(command.Parameters[1].Value);
        Assert.Equal(unchecked((int)3_000_000_000u), command.Parameters[1].Value);
    }

    [Fact]
    public void Expand_TooManyParameters_ThrowsBeforeExecutingCommand()
    {
        using var command = new SqliteCommand { CommandText = "SELECT * FROM t WHERE c IN (@c)" };

        var ex = Assert.Throws<InvalidOperationException>(
            () => InquiryInExpansion.Expand(command, "@c", new List<int> { 1, 2, 3 }, maxParameterCount: 2));

        Assert.Contains("parameter limit", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }
}
