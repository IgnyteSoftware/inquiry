using System.Buffers;
using System.Data.Common;
using Inquiry.Commands;
using Inquiry.Connections;
using Inquiry.Interceptors;
using Inquiry.Materialization;
using Inquiry.Pipeline;
using Microsoft.Data.Sqlite;

namespace Inquiry.Tests;

/// <summary>
/// Pins how buffered list reads size the list they return (#506). Reads without a capacity hint
/// return a list sized to the row count once the result passes 16 rows. Smaller results and hinted
/// reads keep the list they got before.
/// </summary>
public sealed class BufferedListCapacityTests
{
    public enum ListPath
    {
        GeneratedCommand,
        StructMaterializer,
        InterfaceMaterializer,
        TransactedGeneratedCommand,
        TransactedStructMaterializer,
        TransactedInterfaceMaterializer,
        Grid,
    }

    public static TheoryData<ListPath, int> LargeResults()
    {
        var data = new TheoryData<ListPath, int>();
        foreach (var path in Enum.GetValues<ListPath>())
        {
            data.Add(path, 17);
            data.Add(path, 1000);
        }

        return data;
    }

    public static TheoryData<ListPath, int> SmallResults()
    {
        var data = new TheoryData<ListPath, int>();
        foreach (var path in Enum.GetValues<ListPath>())
        {
            foreach (var rows in new[] { 0, 1, 10, 16 })
            {
                data.Add(path, rows);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(LargeResults))]
    public async Task UnhintedListIsSizedToTheRowCount(ListPath path, int rows)
    {
        var list = await ReadAsync(path, rows);

        AssertRowsInOrder(list, rows);
        Assert.Equal(rows, list.Capacity);
    }

    [Theory]
    [MemberData(nameof(SmallResults))]
    public async Task UnhintedSmallListKeepsTodaysGrowth(ListPath path, int rows)
    {
        var list = await ReadAsync(path, rows);

        AssertRowsInOrder(list, rows);
        var expected = new List<Row>();
        for (var i = 0; i < rows; i++)
        {
            expected.Add(new Row(i + 1));
        }

        Assert.Equal(expected.Capacity, list.Capacity);
    }

    [Theory]
    [InlineData(ListPath.GeneratedCommand)]
    [InlineData(ListPath.StructMaterializer)]
    [InlineData(ListPath.TransactedGeneratedCommand)]
    [InlineData(ListPath.TransactedStructMaterializer)]
    public async Task HintedListKeepsTheHintedCapacity(ListPath path)
    {
        var list = await ReadAsync(path, rows: 3, capacityHint: 50);

        AssertRowsInOrder(list, 3);
        Assert.Equal(50, list.Capacity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MaterializerFailureAfterPoolingPropagatesAndNextReadSucceeds(bool useTransaction)
    {
        await using var connection = await OpenAsync();
        await using var transaction = useTransaction ? await connection.BeginTransactionAsync() : null;
        IInquiryRequestPipeline pipeline = useTransaction
            ? new TransactedInquiryRequestPipeline(connection, transaction!, Array.Empty<IInquiryCommandInterceptor>(), new SqliteFactory(), options: null)
            : new InquiryRequestPipeline(new SqliteFactory(), Array.Empty<IInquiryCommandInterceptor>());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            pipeline.QueryListAsync<Row, int, ThrowingRowMaterializer>(
                Generated(100),
                new ThrowingRowMaterializer()));
        Assert.Equal("Row 40 failed.", exception.Message);

        var list = await pipeline.QueryListAsync<Row, int, RowMaterializer>(Generated(100), new RowMaterializer());
        AssertRowsInOrder((List<Row>)list, 100);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PooledRowsAreClearedAndReturnedOnce(bool complete)
    {
        // PoolProbe is used only here, so no other test rents from ArrayPool<PoolProbe>.Shared. A
        // rent on the same thread gets the array returned last, and a second rent gets the same
        // array again only if it was returned twice.
        object? rows = InquiryBufferedList<PoolProbe>.Start(-1);
        for (var i = 0; i < 20; i++)
        {
            InquiryBufferedList<PoolProbe>.Add(ref rows, new PoolProbe(i));
        }

        if (complete)
        {
            Assert.Equal(20, InquiryBufferedList<PoolProbe>.Complete(rows).Capacity);
        }

        InquiryBufferedList<PoolProbe>.Release(rows);
        InquiryBufferedList<PoolProbe>.Release(rows);

        var first = ArrayPool<PoolProbe>.Shared.Rent(32);
        var second = ArrayPool<PoolProbe>.Shared.Rent(32);
        try
        {
            Assert.NotSame(first, second);
            Assert.All(first, Assert.Null);
        }
        finally
        {
            ArrayPool<PoolProbe>.Shared.Return(first);
            ArrayPool<PoolProbe>.Shared.Return(second);
        }
    }

    private static async Task<List<Row>> ReadAsync(ListPath path, int rows, int capacityHint = -1)
    {
        switch (path)
        {
            case ListPath.GeneratedCommand:
            case ListPath.StructMaterializer:
            case ListPath.InterfaceMaterializer:
                {
                    var pipeline = new InquiryRequestPipeline(new SqliteFactory(), Array.Empty<IInquiryCommandInterceptor>());
                    return await ReadAsync(pipeline, path, rows, capacityHint);
                }

            case ListPath.TransactedGeneratedCommand:
            case ListPath.TransactedStructMaterializer:
            case ListPath.TransactedInterfaceMaterializer:
                {
                    await using var connection = await OpenAsync();
                    await using var transaction = await connection.BeginTransactionAsync();
                    var pipeline = new TransactedInquiryRequestPipeline(
                        connection,
                        transaction,
                        Array.Empty<IInquiryCommandInterceptor>(),
                        new SqliteFactory(),
                        options: null);
                    return await ReadAsync(pipeline, path, rows, capacityHint);
                }

            case ListPath.Grid:
                {
                    await using var connection = await OpenAsync();
                    await using var command = connection.CreateCommand();
                    command.CommandText = SelectRowsSql + "; SELECT 1;";
                    BindRowCount(command, rows);
                    var reader = await command.ExecuteReaderAsync();
                    await using var grid = new InquiryGridReader(reader, command, ownedConnection: null, lease: null);
                    var list = await grid.ReadListAsync<Row, RowMaterializer>(new RowMaterializer());
                    Assert.Equal(1, await grid.ReadScalarAsync<int>());
                    return list;
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(path), path, null);
        }
    }

    private static async Task<List<Row>> ReadAsync(
        IInquiryRequestPipeline pipeline,
        ListPath path,
        int rows,
        int capacityHint)
    {
        IReadOnlyList<Row> result = path switch
        {
            ListPath.GeneratedCommand or ListPath.TransactedGeneratedCommand =>
                await pipeline.QueryListAsync<Row, int, RowMaterializer>(
                    Generated(rows), new RowMaterializer(), capacityHint: capacityHint),
            ListPath.StructMaterializer or ListPath.TransactedStructMaterializer =>
                await pipeline.QueryListAsync<Row, RowMaterializer>(
                    Command(rows), new RowMaterializer(), capacityHint: capacityHint),
            _ =>
                await pipeline.QueryListAsync(Command(rows), new RowClassMaterializer()),
        };

        return Assert.IsType<List<Row>>(result);
    }

    // A recursive CTE yields 1..@n without a table, so every connection can use its own
    // in-memory database.
    private const string SelectRowsSql =
        "WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c WHERE x < @n) SELECT x FROM c WHERE x <= @n";

    private static InquiryGeneratedCommand<int> Generated(int rows)
        => new(SelectRowsSql, rows, static (command, n) => BindRowCount(command, n));

    private static InquiryCommand Command(int rows)
        => new(SelectRowsSql, command => BindRowCount(command, rows));

    private static void BindRowCount(DbCommand command, int rows)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@n";
        parameter.Value = rows;
        command.Parameters.Add(parameter);
    }

    private static void AssertRowsInOrder(List<Row> list, int rows)
    {
        Assert.Equal(rows, list.Count);
        for (var i = 0; i < rows; i++)
        {
            Assert.Equal(i + 1, list[i].Id);
        }
    }

    private static async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private sealed record Row(int Id);

    private sealed record PoolProbe(int Value);

    private readonly struct RowMaterializer : IInquiryEntityMaterializer<Row>
    {
        public Row Materialize(DbDataReader reader) => new(reader.GetInt32(0));
    }

    private sealed class RowClassMaterializer : IInquiryEntityMaterializer<Row>
    {
        public Row Materialize(DbDataReader reader) => new(reader.GetInt32(0));
    }

    private readonly struct ThrowingRowMaterializer : IInquiryEntityMaterializer<Row>
    {
        public Row Materialize(DbDataReader reader)
        {
            var id = reader.GetInt32(0);
            return id == 40 ? throw new InvalidOperationException("Row 40 failed.") : new Row(id);
        }
    }

    private sealed class SqliteFactory : IInquiryConnectionFactory
    {
        public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
    }
}
