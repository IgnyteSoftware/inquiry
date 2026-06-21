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
    public void Expand_ScalarElements_PassThroughUnchanged()
    {
        using var command = new SqliteCommand { CommandText = "SELECT * FROM t WHERE c IN (@c)" };

        InquiryInExpansion.Expand(command, "@c", new List<int> { 3, 4, 5 });

        Assert.Equal("SELECT * FROM t WHERE c IN (@c0, @c1, @c2)", command.CommandText);
        Assert.Equal(3, command.Parameters.Count);
        Assert.Equal(3, command.Parameters[0].Value);
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
