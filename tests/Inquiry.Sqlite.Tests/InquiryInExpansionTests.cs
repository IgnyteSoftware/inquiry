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
    public void Expand_TooManyParameters_ThrowsBeforeExecutingCommand()
    {
        using var command = new SqliteCommand { CommandText = "SELECT * FROM t WHERE c IN (@c)" };

        var ex = Assert.Throws<InvalidOperationException>(
            () => InquiryInExpansion.Expand(command, "@c", new List<int> { 1, 2, 3 }, maxParameterCount: 2));

        Assert.Contains("parameter limit", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }
}
