using System.Data.Common;
using Inquiry.Materialization;
using Microsoft.Data.Sqlite;

namespace Inquiry.Tests;

/// <summary>
/// Generated paged results read their page and total through <see cref="InquiryGridReader"/> (#403). A
/// provider can report cancellation with its own token, or with a native error such as SqlClient's
/// "operation cancelled by user". The list and scalar reads must rethrow either one as a cancellation with
/// the caller's token, as the single-command list and scalar paths do. A read failure must also survive a
/// failing disposal, as it does on those paths.
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

    [Fact]
    public async Task ReadFailureStaysFirstWhenDisposalAlsoFails()
    {
        using var caller = new CancellationTokenSource();
        var readFailure = new InvalidOperationException("Read failed.");
        var disposeFailure = new InvalidOperationException("Dispose failed.");

        // The shape generated paged methods emit: the grid is disposed by await using after the read throws.
        async Task ReadAsync()
        {
            await using var grid = await CreateGridAsync(caller, failOnRead: true, failOnNextResult: false, readFailure, disposeFailure, cancel: false);
            await grid.ReadScalarAsync<long>(caller.Token);
        }

        var exception = await Assert.ThrowsAsync<AggregateException>(ReadAsync);

        Assert.Equal(new Exception[] { readFailure, disposeFailure }, exception.InnerExceptions);
    }

    // The provider sees the caller cancel, then throws with a token of its own, or a native error.
    private static async Task<InquiryGridReader> CreateGridAsync(CancellationTokenSource caller, bool failOnRead, bool failOnNextResult, Exception? failure = null, Exception? disposeFailure = null, bool cancel = true)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync(CancellationToken.None);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT 1; SELECT 2;";
        var inner = await command.ExecuteReaderAsync(CancellationToken.None);
        if (cancel)
        {
            await caller.CancelAsync();
        }

        var reader = new FailingReader(inner, failOnRead, failOnNextResult, failure, disposeFailure);
        return new InquiryGridReader(reader, command, ownedConnection: connection, lease: null);
    }

    private sealed class Box
    {
        public int Value { get; init; }
    }

    private readonly struct BoxMaterializer : IInquiryEntityMaterializer<Box>
    {
        public Box Materialize(DbDataReader reader) => new() { Value = reader.GetInt32(0) };
    }

    private sealed class FailingReader(DbDataReader inner, bool failOnRead, bool failOnNextResult, Exception? failure, Exception? disposeFailure) : DbDataReader
    {
        private static readonly CancellationToken _foreignToken = new(canceled: true);

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
        public override object GetValue(int ordinal) => inner.GetValue(ordinal);
        public override int GetValues(object[] values) => inner.GetValues(values);
        public override bool IsDBNull(int ordinal) => inner.IsDBNull(ordinal);
        public override bool NextResult() => inner.NextResult();
        public override bool Read() => inner.Read();
        public override System.Collections.IEnumerator GetEnumerator() => ((System.Collections.IEnumerable)inner).GetEnumerator();

        public override Task<bool> ReadAsync(CancellationToken cancellationToken)
            => failOnRead ? throw Failure() : inner.ReadAsync(CancellationToken.None);

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
