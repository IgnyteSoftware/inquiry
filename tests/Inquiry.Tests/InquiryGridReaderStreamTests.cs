using System.Data.Common;
using Inquiry.Materialization;
using Microsoft.Data.Sqlite;

namespace Inquiry.Tests;

/// <summary>
/// <see cref="InquiryGridReader.ReadStreamAsync{TEntity, TMaterializer}"/> (#70): the pull-based read that
/// lets generated eager-load stores yield parents straight out of the grid's last result set instead of
/// buffering them into a list.
/// </summary>
public sealed class InquiryGridReaderStreamTests
{
    [Fact]
    public async Task StreamsEveryRowThenAdvancesToTheNextResultSet()
    {
        await using var connection = CreateConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3; SELECT 99;";
        await using var reader = await command.ExecuteReaderAsync();
        var grid = new InquiryGridReader(reader, command, ownedConnection: null, lease: null);

        var streamed = new List<int>();
        await foreach (var row in grid.ReadStreamAsync<Box, BoxMaterializer>(default))
        {
            streamed.Add(row.Value);
        }

        // Completing the enumeration must leave the grid positioned on the NEXT set.
        var afterStream = await grid.ReadScalarAsync<int>();

        Assert.Equal(new[] { 1, 2, 3 }, streamed);
        Assert.Equal(99, afterStream);
    }

    [Fact]
    public async Task StreamsEmptyResultSetAndStillAdvances()
    {
        await using var connection = CreateConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 WHERE 0 = 1; SELECT 7;";
        await using var reader = await command.ExecuteReaderAsync();
        var grid = new InquiryGridReader(reader, command, ownedConnection: null, lease: null);

        var streamed = new List<int>();
        await foreach (var row in grid.ReadStreamAsync<Box, BoxMaterializer>(default))
        {
            streamed.Add(row.Value);
        }

        Assert.Empty(streamed);
        Assert.Equal(7, await grid.ReadScalarAsync<int>());
    }

    [Fact]
    public async Task AbandonedEnumerationPoisonsTheGridInsteadOfMisreadingTheNextSet()
    {
        await using var connection = CreateConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3; SELECT 99;";
        await using var reader = await command.ExecuteReaderAsync();
        var grid = new InquiryGridReader(reader, command, ownedConnection: null, lease: null);

        // Stop after the first row — the reader is left positioned mid-set.
        await foreach (var row in grid.ReadStreamAsync<Box, BoxMaterializer>(default))
        {
            Assert.Equal(1, row.Value);
            break;
        }

        // Without the latch this would silently return 2 — the tail of the abandoned set — as though it
        // were the next result set.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => grid.ReadScalarAsync<int>());
        Assert.Contains("abandoned", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NestedReadDuringEnumerationThrows()
    {
        await using var connection = CreateConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 UNION ALL SELECT 2; SELECT 99;";
        await using var reader = await command.ExecuteReaderAsync();
        var grid = new InquiryGridReader(reader, command, ownedConnection: null, lease: null);

        await foreach (var _ in grid.ReadStreamAsync<Box, BoxMaterializer>(default))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => grid.ReadScalarAsync<int>());
            break;
        }
    }

    [Fact]
    public async Task ReEnumeratingTheSameSequenceThrowsInsteadOfEatingTheNextResultSet()
    {
        await using var connection = CreateConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 UNION ALL SELECT 2; SELECT 99;";
        await using var reader = await command.ExecuteReaderAsync();
        var grid = new InquiryGridReader(reader, command, ownedConnection: null, lease: null);

        var sequence = grid.ReadStreamAsync<Box, BoxMaterializer>(default);

        var first = new List<int>();
        await foreach (var row in sequence)
        {
            first.Add(row.Value);
        }

        Assert.Equal(new[] { 1, 2 }, first);

        // An async iterator hands out a fresh state machine per GetAsyncEnumerator, so without the
        // one-shot guard this would materialize result set 2 through BoxMaterializer and skip past it.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in sequence)
            {
            }
        });

        // The following result set is still intact and readable.
        Assert.Equal(99, await grid.ReadScalarAsync<int>());
    }

    [Fact]
    public async Task CreatingASequenceWithoutEnumeratingItLeavesTheGridUsable()
    {
        await using var connection = CreateConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 42; SELECT 99;";
        await using var reader = await command.ExecuteReaderAsync();
        var grid = new InquiryGridReader(reader, command, ownedConnection: null, lease: null);

        // Never enumerated, so the reader has not moved — this must not poison the grid.
        _ = grid.ReadStreamAsync<Box, BoxMaterializer>(default);

        Assert.Equal(42, await grid.ReadScalarAsync<int>());
        Assert.Equal(99, await grid.ReadScalarAsync<int>());
    }

    [Fact]
    public async Task ThrowsObjectDisposedWhenTheGridIsAlreadyDisposed()
    {
        await using var connection = CreateConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        await using var reader = await command.ExecuteReaderAsync();
        var grid = new InquiryGridReader(reader, command, ownedConnection: null, lease: null);
        await grid.DisposeAsync();

        // Validation is eager: the throw happens at the call, not deferred to the first MoveNextAsync.
        Assert.Throws<ObjectDisposedException>(() => grid.ReadStreamAsync<Box, BoxMaterializer>(default));
    }

    [Fact]
    public async Task ThrowsWhenNoResultSetsRemain()
    {
        await using var connection = CreateConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        await using var reader = await command.ExecuteReaderAsync();
        var grid = new InquiryGridReader(reader, command, ownedConnection: null, lease: null);

        await grid.ReadScalarAsync<int>();

        Assert.Throws<InvalidOperationException>(() => grid.ReadStreamAsync<Box, BoxMaterializer>(default));
    }

    [Fact]
    public async Task CancellationMidStreamSurfacesToTheCaller()
    {
        await using var connection = CreateConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3;";
        await using var reader = await command.ExecuteReaderAsync();
        var grid = new InquiryGridReader(reader, command, ownedConnection: null, lease: null);

        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var row in grid.ReadStreamAsync<Box, BoxMaterializer>(default, cts.Token))
            {
                cts.Cancel();
                _ = row;
            }
        });
    }

    private static SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        return connection;
    }

    private sealed class Box
    {
        public int Value { get; init; }
    }

    private readonly struct BoxMaterializer : IInquiryEntityMaterializer<Box>
    {
        public Box Materialize(DbDataReader reader) => new() { Value = reader.GetInt32(0) };
    }
}
