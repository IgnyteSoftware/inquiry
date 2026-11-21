using System.Data;
using Inquiry.Commands;
using Inquiry.Transactions;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Maps every documented <c>sp_getapplock</c> return code (#611). The live suite cannot force
/// -2 or -999 through the helper, so a fake transaction returns each code.
/// </summary>
public sealed class AppLockResultMappingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task GrantedCodesComplete(int returnCode)
    {
        var transaction = new ScalarTransaction(returnCode);
        await transaction.AcquireAppLockAsync("orders", SqlServerAppLockMode.Exclusive, TimeSpan.FromSeconds(5));

        var command = Assert.Single(transaction.Commands);
        Assert.Contains("@LockOwner = N'Transaction'", command.CommandText, StringComparison.Ordinal);
        Assert.Equal(new object?[] { "orders", "Exclusive", 5000 }, command.Parameters.Select(p => p.Value));

        // These types and sizes match sp_getapplock's own nvarchar(255) and varchar(32) parameters.
        Assert.Equal(new DbType?[] { DbType.String, DbType.AnsiString, DbType.Int32 }, command.Parameters.Select(p => p.DbType));
        Assert.Equal(new int?[] { 255, 32, null }, command.Parameters.Select(p => p.Size));
        Assert.Equal(35, command.CommandTimeout);
    }

    [Theory]
    [InlineData(-1, SqlServerAppLockFailure.Timeout)]
    [InlineData(-2, SqlServerAppLockFailure.Canceled)]
    [InlineData(-3, SqlServerAppLockFailure.Deadlock)]
    [InlineData(-999, SqlServerAppLockFailure.InvalidRequest)]
    public async Task RefusedCodesThrowTypedException(int returnCode, SqlServerAppLockFailure expected)
    {
        var error = await Assert.ThrowsAsync<SqlServerAppLockException>(
            () => new ScalarTransaction(returnCode).AcquireAppLockAsync("orders", SqlServerAppLockMode.Shared, TimeSpan.Zero));

        Assert.Equal(expected, error.Failure);
        Assert.Equal(returnCode, error.ReturnCode);
        Assert.Equal("orders", error.Resource);
    }

    [Fact]
    public async Task InfiniteTimeoutWaitsWithoutClientTimeout()
    {
        var transaction = new ScalarTransaction(0);
        await transaction.AcquireAppLockAsync("orders", SqlServerAppLockMode.Update, Timeout.InfiniteTimeSpan);

        var command = Assert.Single(transaction.Commands);
        Assert.Equal(-1, command.Parameters[2].Value);
        Assert.Equal(0, command.CommandTimeout);
    }

    [Fact]
    public async Task InvalidArgumentsThrowBeforeExecuting()
    {
        var transaction = new ScalarTransaction(0);
        await Assert.ThrowsAsync<ArgumentException>(() => transaction.AcquireAppLockAsync("", SqlServerAppLockMode.Shared, TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentException>(() => transaction.AcquireAppLockAsync(new string('x', 256), SqlServerAppLockMode.Shared, TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => transaction.AcquireAppLockAsync("orders", (SqlServerAppLockMode)99, TimeSpan.Zero));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => transaction.AcquireAppLockAsync("orders", SqlServerAppLockMode.Shared, TimeSpan.FromMilliseconds(-2)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => transaction.AcquireAppLockAsync("orders", SqlServerAppLockMode.Shared, TimeSpan.FromDays(25)));
        Assert.Empty(transaction.Commands);
    }

    private sealed class ScalarTransaction(int result) : IInquiryTransaction
    {
        public List<InquiryCommand> Commands { get; } = [];

        public IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;

        public Task<T> ExecuteScalarAsync<T>(InquiryCommand command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult((T)(object)result);
        }

        public Task<T> ExecuteScalarAsync<T>(FormattableString commandText, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task CommitAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RollbackAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void ThrowIfClosed() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(FormattableString commandText, CancellationToken cancellationToken = default) where TEntity : class => throw new NotSupportedException();
        public IAsyncEnumerable<TEntity> QueryAsync<TEntity>(InquiryCommand command, CancellationToken cancellationToken = default) where TEntity : class => throw new NotSupportedException();
        public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(FormattableString commandText, CancellationToken cancellationToken = default) where TEntity : class => throw new NotSupportedException();
        public Task<IReadOnlyList<TEntity>> QueryListAsync<TEntity>(InquiryCommand command, CancellationToken cancellationToken = default) where TEntity : class => throw new NotSupportedException();
        public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(FormattableString commandText, CancellationToken cancellationToken = default) where TEntity : class => throw new NotSupportedException();
        public Task<TEntity?> QuerySingleOrDefaultAsync<TEntity>(InquiryCommand command, CancellationToken cancellationToken = default) where TEntity : class => throw new NotSupportedException();
        public Task<int> ExecuteAsync(FormattableString commandText, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> ExecuteAsync(InquiryCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IInquiryTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
