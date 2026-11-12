using System.Data.Common;
using Inquiry.Materialization;
using Microsoft.Data.Sqlite;

namespace Inquiry.Tests;

/// <summary>
/// Generated paged results read their page and total through <see cref="InquiryGridReader"/> (#403). A
/// provider can report cancellation with its own token, or with a native error such as SqlClient's
/// "operation cancelled by user". Every grid read must rethrow either one as a cancellation with the
/// caller's token, as the single-command paths do (#542). A read failure must also survive a failing
/// disposal, as it does on those paths.
/// </summary>
public sealed class InquiryGridReaderCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadListAsyncRethrowsForeignCancellationWithTheCallerToken(bool failOnNextResult)
    {
        using var caller = new CancellationTokenSource();
        await using var grid = await CreateGridAsync(caller, failOnRead: !failOnNextResult, failOnNextResult);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => grid.ReadListAsync<Box, BoxMaterializer>(default, caller.Token));

        Assert.Equal(caller.Token, exception.CancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadScalarAsyncRethrowsForeignCancellationWithTheCallerToken(bool failOnNextResult)
    {
        using var caller = new CancellationTokenSource();
        await using var grid = await CreateGridAsync(caller, failOnRead: !failOnNextResult, failOnNextResult);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => grid.ReadScalarAsync<long>(caller.Token));

        Assert.Equal(caller.Token, exception.CancellationToken);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task NativeErrorAfterCancellationIsRethrownAsCallerCancellation(bool scalar, bool failOnNextResult)
    {
        using var caller = new CancellationTokenSource();
        var native = new InvalidOperationException("Operation cancelled by user.");
        await using var grid = await CreateGridAsync(caller, failOnRead: !failOnNextResult, failOnNextResult, native);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => scalar ? grid.ReadScalarAsync<long>(caller.Token) : grid.ReadListAsync<Box, BoxMaterializer>(default, caller.Token));

        Assert.Equal(caller.Token, exception.CancellationToken);
        Assert.Same(native, exception.InnerException);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReadFailureStaysFirstWhenDisposalAlsoFails(bool scalar, bool failOnNextResult)
    {
        using var caller = new CancellationTokenSource();
        var readFailure = new InvalidOperationException("Read failed.");
        var disposeFailure = new InvalidOperationException("Dispose failed.");

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => ReadThenDisposeAsync(caller, scalar, failOnNextResult, readFailure, disposeFailure, cancel: false));

        Assert.Equal(new Exception[] { readFailure, disposeFailure }, exception.InnerExceptions);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task CancellationStaysFirstWithTheCallerTokenWhenDisposalAlsoFails(bool scalar, bool failOnNextResult, bool nativeError)
    {
        using var caller = new CancellationTokenSource();
        Exception? failure = nativeError ? new InvalidOperationException("Operation cancelled by user.") : null;
        var disposeFailure = new InvalidOperationException("Dispose failed.");

        var exception = await Assert.ThrowsAsync<AggregateException>(
            () => ReadThenDisposeAsync(caller, scalar, failOnNextResult, failure, disposeFailure, cancel: true));

        Assert.Equal(2, exception.InnerExceptions.Count);
        var canceled = Assert.IsAssignableFrom<OperationCanceledException>(exception.InnerExceptions[0]);
        Assert.Equal(caller.Token, canceled.CancellationToken);
        Assert.Same(disposeFailure, exception.InnerExceptions[1]);
    }

    public enum GridRead
    {
        SingleOrDefault,
        GeneratedSingleOrDefault,
        ForEach,
        Rows,
        Stream,
        List,
    }

    public static TheoryData<GridRead, bool> EveryReadAtBothFailurePoints()
    {
        var data = new TheoryData<GridRead, bool>();
        foreach (var read in Enum.GetValues<GridRead>().Where(static read => read != GridRead.List))
        {
            data.Add(read, false);
            data.Add(read, true);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryReadAtBothFailurePoints))]
    public async Task EveryReadRethrowsForeignCancellationWithTheCallerToken(GridRead read, bool failOnNextResult)
    {
        using var caller = new CancellationTokenSource();
        await using var grid = await CreateGridAsync(caller, failOnRead: !failOnNextResult, failOnNextResult);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadGridAsync(grid, read, caller.Token));

        Assert.Equal(caller.Token, exception.CancellationToken);
    }

    [Theory]
    [MemberData(nameof(EveryReadAtBothFailurePoints))]
    public async Task EveryReadRethrowsANativeErrorAfterCancellationAsCallerCancellation(GridRead read, bool failOnNextResult)
    {
        using var caller = new CancellationTokenSource();
        var native = new InvalidOperationException("Operation cancelled by user.");
        await using var grid = await CreateGridAsync(caller, failOnRead: !failOnNextResult, failOnNextResult, native);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ReadGridAsync(grid, read, caller.Token));

        Assert.Equal(caller.Token, exception.CancellationToken);
        Assert.Same(native, exception.InnerException);
    }

    [Theory]
    [MemberData(nameof(EveryReadAtBothFailurePoints))]
    public async Task EveryReadLeavesADriverErrorUnchangedWithoutCancellation(GridRead read, bool failOnNextResult)
    {
        using var caller = new CancellationTokenSource();
        var driverError = new InvalidOperationException("Connection lost.");
        await using var grid = await CreateGridAsync(caller, failOnRead: !failOnNextResult, failOnNextResult, driverError, cancel: false);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => ReadGridAsync(grid, read, caller.Token));

        Assert.Same(driverError, exception);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadScalarAsyncNormalizesAFailureFromReadingTheValue(bool nativeError)
    {
        using var caller = new CancellationTokenSource();
        Exception? native = nativeError ? new InvalidOperationException("Operation cancelled by user.") : null;
        await using var grid = await CreateGridAsync(caller, failOnRead: false, failOnNextResult: false, native, failOnGetValue: true);

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => grid.ReadScalarAsync<long>(caller.Token));

        Assert.Equal(caller.Token, exception.CancellationToken);
        if (nativeError)
        {
            Assert.Same(native, exception.InnerException);
        }
    }

    // A materializer or callback failure is not a provider error, so it is never relabelled as a
    // cancellation, even when the token is already cancelled.
    [Theory]
    [InlineData(GridRead.SingleOrDefault)]
    [InlineData(GridRead.GeneratedSingleOrDefault)]
    [InlineData(GridRead.ForEach)]
    [InlineData(GridRead.Rows)]
    [InlineData(GridRead.Stream)]
    [InlineData(GridRead.List)]
    public async Task ApplicationFailureAfterCancellationSurfacesUnchanged(GridRead read)
    {
        using var caller = new CancellationTokenSource();
        await using var grid = await CreateGridAsync(caller, failOnRead: false, failOnNextResult: false);

        var exception = await Assert.ThrowsAsync<FormatException>(() => ReadGridAsync(grid, read, caller.Token, failInApplicationCode: true));

        Assert.Equal("bad row", exception.Message);
    }

    [Fact]
    public async Task StreamCancelledBetweenRowsRethrowsWithTheCallerToken()
    {
        using var caller = new CancellationTokenSource();
        await using var grid = await CreateGridAsync(
            caller, failOnRead: true, failOnNextResult: false, cancel: false,
            commandText: "SELECT 1 UNION ALL SELECT 2; SELECT 3;", rowsBeforeReadFailure: 1);
        var rows = new List<int>();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var box in grid.ReadStreamAsync<Box, BoxMaterializer>(default, caller.Token))
            {
                rows.Add(box.Value);
                await caller.CancelAsync();
            }
        });

        Assert.Equal([1], rows);
        Assert.Equal(caller.Token, exception.CancellationToken);
    }

    private static async Task ReadGridAsync(InquiryGridReader grid, GridRead read, CancellationToken cancellationToken, bool failInApplicationCode = false)
    {
        var materializer = new BoxMaterializer(failInApplicationCode);
        switch (read)
        {
            case GridRead.SingleOrDefault:
                await grid.ReadSingleOrDefaultAsync<Box, BoxMaterializer>(materializer, cancellationToken);
                break;
            case GridRead.GeneratedSingleOrDefault:
                await grid.ReadGeneratedSingleOrDefaultAsync<Box, BoxMaterializer>(materializer, cancellationToken);
                break;
            case GridRead.ForEach:
                await grid.ReadForEachAsync<Box, BoxMaterializer>(default, _ => ThrowIf(failInApplicationCode), cancellationToken);
                break;
            case GridRead.Rows:
                await grid.ReadRowsAsync(_ => ThrowIf(failInApplicationCode), cancellationToken);
                break;
            case GridRead.List:
                await grid.ReadListAsync<Box, BoxMaterializer>(materializer, cancellationToken);
                break;
            default:
                await foreach (var _ in grid.ReadStreamAsync<Box, BoxMaterializer>(materializer, cancellationToken))
                {
                    // Enumerate to the end.
                }

                break;
        }
    }

    private static void ThrowIf(bool fail)
    {
        if (fail)
        {
            throw new FormatException("bad row");
        }
    }

    // The shape generated paged methods emit: the grid is disposed by await using after the read throws.
    private static async Task ReadThenDisposeAsync(CancellationTokenSource caller, bool scalar, bool failOnNextResult, Exception? failure, Exception disposeFailure, bool cancel)
    {
        await using var grid = await CreateGridAsync(caller, failOnRead: !failOnNextResult, failOnNextResult, failure, disposeFailure, cancel);
        if (scalar)
        {
            await grid.ReadScalarAsync<long>(caller.Token);
        }
        else
        {
            await grid.ReadListAsync<Box, BoxMaterializer>(default, caller.Token);
        }
    }

    // The provider sees the caller cancel, then throws with a token of its own, or a native error.
    private static async Task<InquiryGridReader> CreateGridAsync(CancellationTokenSource caller, bool failOnRead, bool failOnNextResult, Exception? failure = null, Exception? disposeFailure = null, bool cancel = true, string commandText = "SELECT 1; SELECT 2;", int rowsBeforeReadFailure = 0, bool failOnGetValue = false)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        var inner = await command.ExecuteReaderAsync(CancellationToken.None);
        if (cancel)
        {
            await caller.CancelAsync();
        }

        var reader = new FailingReader(inner, failOnRead, failOnNextResult, failure, disposeFailure, rowsBeforeReadFailure, failOnGetValue);
        return new InquiryGridReader(reader, command, ownedConnection: connection, lease: null);
    }

    private sealed class Box
    {
        public int Value { get; init; }
    }

    private readonly struct BoxMaterializer(bool fail) : IInquiryEntityMaterializer<Box>
    {
        public Box Materialize(DbDataReader reader)
        {
            ThrowIf(fail);
            return new() { Value = reader.GetInt32(0) };
        }
    }

    private sealed class FailingReader(DbDataReader inner, bool failOnRead, bool failOnNextResult, Exception? failure, Exception? disposeFailure, int rowsBeforeReadFailure, bool failOnGetValue) : DbDataReader
    {
        private static readonly CancellationToken _foreignToken = new(canceled: true);
        private int _rowsRead;

        public override object this[int ordinal] => inner[ordinal];
        public override object this[string name] => inner[name];
        public override int Depth => inner.Depth;
        public override int FieldCount => inner.FieldCount;
        public override bool HasRows => inner.HasRows;
        public override bool IsClosed => inner.IsClosed;
        public override int RecordsAffected => inner.RecordsAffected;
        public override bool GetBoolean(int ordinal) => inner.GetBoolean(ordinal);
        public override byte GetByte(int ordinal) => inner.GetByte(ordinal);
        public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) => inner.GetBytes(ordinal, dataOffset, buffer, bufferOffset, length);
        public override char GetChar(int ordinal) => inner.GetChar(ordinal);
        public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) => inner.GetChars(ordinal, dataOffset, buffer, bufferOffset, length);
        public override string GetDataTypeName(int ordinal) => inner.GetDataTypeName(ordinal);
        public override DateTime GetDateTime(int ordinal) => inner.GetDateTime(ordinal);
        public override decimal GetDecimal(int ordinal) => inner.GetDecimal(ordinal);
        public override double GetDouble(int ordinal) => inner.GetDouble(ordinal);
        public override Type GetFieldType(int ordinal) => inner.GetFieldType(ordinal);
        public override float GetFloat(int ordinal) => inner.GetFloat(ordinal);
        public override Guid GetGuid(int ordinal) => inner.GetGuid(ordinal);
        public override short GetInt16(int ordinal) => inner.GetInt16(ordinal);
        public override int GetInt32(int ordinal) => inner.GetInt32(ordinal);
        public override long GetInt64(int ordinal) => inner.GetInt64(ordinal);
        public override string GetName(int ordinal) => inner.GetName(ordinal);
        public override int GetOrdinal(string name) => inner.GetOrdinal(name);
        public override string GetString(int ordinal) => inner.GetString(ordinal);
        public override object GetValue(int ordinal) => failOnGetValue ? throw Failure() : inner.GetValue(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override bool NextResult() => inner.NextResult();
        public override bool Read() => inner.Read();
        public override System.Collections.IEnumerator GetEnumerator() => ((System.Collections.IEnumerable)inner).GetEnumerator();

        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
            => failOnRead && _rowsRead++ >= rowsBeforeReadFailure ? throw Failure() : inner.ReadAsync(CancellationToken.None);

        public override Task<bool> NextResultAsync(CancellationToken cancellationToken)
            => failOnNextResult ? throw Failure() : inner.NextResultAsync(CancellationToken.None);

        private Exception Failure() => failure ?? new OperationCanceledException(_foreignToken);

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            await base.DisposeAsync();
            if (disposeFailure is not null)
            {
                throw disposeFailure;
            }
        }
    }
}
