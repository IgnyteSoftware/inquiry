using System;
using System.Threading.Tasks;
using Inquiry;
using Inquiry.Sqlite.Tests.Fixtures;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// Transactional-outbox interop: <see cref="Transactions.IInquiryTransaction.Connection"/> /
/// <see cref="Transactions.IInquiryTransaction.Transaction"/> let external libraries (outbox
/// patterns) enlist their own <c>DbCommand</c>s in the active Inquiry transaction, committing and
/// rolling back atomically with Inquiry's own work.
/// </summary>
public sealed class TransactionInteropTests
{
    private const string Ddl = "CREATE TABLE Outbox (Id INTEGER PRIMARY KEY AUTOINCREMENT, Payload TEXT NOT NULL);";

    [Fact]
    public async Task RawCommandOnExposedConnectionCommitsWithTransaction()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "TxInterop");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            // The external-library path: a raw DbCommand enlisted via the exposed pair.
            await using (var cmd = tx.Connection.CreateCommand())
            {
                cmd.Transaction = tx.Transaction;
                cmd.CommandText = "INSERT INTO Outbox (Payload) VALUES ('external')";
                await cmd.ExecuteNonQueryAsync();
            }

            // Inquiry's own work in the same transaction.
            await tx.ExecuteAsync($"INSERT INTO Outbox (Payload) VALUES ({"inquiry"})");

            await tx.CommitAsync();
        }

        var count = await inquiry.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM Outbox");
        Assert.Equal(2L, count);
    }

    [Fact]
    public async Task RawCommandRollsBackWithTransaction()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "TxInterop");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using (var tx = await inquiry.BeginTransactionAsync())
        {
            await using var cmd = tx.Connection.CreateCommand();
            cmd.Transaction = tx.Transaction;
            cmd.CommandText = "INSERT INTO Outbox (Payload) VALUES ('doomed')";
            await cmd.ExecuteNonQueryAsync();
            // Disposed without commit → rolls back, taking the external write with it.
        }

        var count = await inquiry.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM Outbox");
        Assert.Equal(0L, count);
    }

    [Fact]
    public async Task ExposedHandlesThrowAfterClose()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "TxInterop");
        var inquiry = harness.GetRequiredService<IInquiry>();

        var tx = await inquiry.BeginTransactionAsync();
        await tx.CommitAsync();

        Assert.Throws<ObjectDisposedException>(() => tx.Connection);
        Assert.Throws<ObjectDisposedException>(() => tx.Transaction);
        await tx.DisposeAsync();
    }

    [Fact]
    public async Task SavepointExposesOuterConnectionAndTransaction()
    {
        await using var harness = await SqliteTestHarness.CreateAsync(Ddl, "TxInterop");
        var inquiry = harness.GetRequiredService<IInquiry>();

        await using var outer = await inquiry.BeginTransactionAsync();
        await using (var inner = await outer.BeginTransactionAsync())
        {
            // A savepoint is the same physical transaction — interop writes enlist identically.
            Assert.Same(outer.Connection, inner.Connection);
            Assert.Same(outer.Transaction, inner.Transaction);
            await inner.CommitAsync();
        }

        await outer.CommitAsync();
    }
}
