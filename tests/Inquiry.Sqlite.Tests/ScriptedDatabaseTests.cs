using System.Data;
using Inquiry.Commands;
using Inquiry.DependencyInjection;
using Inquiry.Sqlite.DependencyInjection;
using Inquiry.Sqlite.Tests.Fixtures;
using Inquiry.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.Sqlite.Tests;

/// <summary>
/// Runs the built-in SQLite runtime over a <see cref="ScriptedDatabase"/> (#608): no database file
/// and no hand-written <see cref="IInquiry"/>.
/// </summary>
public sealed class ScriptedDatabaseTests
{
    [Fact]
    public async Task RepositoryRunsHandwrittenSqlAndGeneratedStoresOverScriptedResults()
    {
        var database = new ScriptedDatabase(SqliteFactory.Instance)
            .Enqueue(ScriptedResult.Rows(new ScriptedResultSet()
                .Column<string>("Label").Column<int>("Second").Column<int>("First")
                .Row("x", 2, 1)))
            .Enqueue(ScriptedResult.Rows(new ScriptedResultSet()
                .Column<int>("Id").Column<string>("Name")
                .Row(7, "seven")))
            .Enqueue(ScriptedResult.Scalar(3L));
        await using var services = CreateServices(database);
        await using var scope = services.CreateAsyncScope();
        var repository = new ScriptedItemRepository(
            scope.ServiceProvider.GetRequiredService<IInquiry>(),
            scope.ServiceProvider.GetRequiredService<GeneratedItemStore>());

        var pair = Assert.Single(await repository.ListPairsAsync("x"));
        var item = await repository.FindAsync(7);
        var count = await repository.CountAsync();

        Assert.Equal((1, 2, "x"), (pair.First, pair.Second, pair.Label));
        Assert.Equal((7, "seven"), (item?.Id, item?.Name));
        Assert.Equal(3L, count);

        var commands = database.Commands;
        Assert.Equal(3, commands.Count);
        var labelParameter = Assert.Single(commands[0].Parameters);
        Assert.Equal("x", labelParameter.Value);
        Assert.Equal(DbType.String, labelParameter.DbType);
        Assert.Equal(7, Assert.Single(commands[1].Parameters).Value);
        Assert.All(commands, command => Assert.Null(command.Transaction));
    }

    [Fact]
    public async Task ColumnNamesThatDoNotMatchTheTargetFailAsTheProductionRuntimeDoes()
    {
        var database = new ScriptedDatabase(SqliteFactory.Instance)
            .Enqueue(ScriptedResult.Rows(new ScriptedResultSet().Column<int>("First").Column<string>("Label")));
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();

#pragma warning disable INQ120 // This test checks the run-time error that INQ120 reports at build time.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => inquiry.QueryListAsync<NameMappedPair>($"SELECT First, Label FROM Pairs"));
#pragma warning restore INQ120

        // The same text the live SQLite test asserts in ResultNameMappingTests.
        Assert.Contains(typeof(NameMappedPair).FullName!, exception.Message, StringComparison.Ordinal);
        Assert.Contains("property 'Second'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Available columns: First, Label", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScriptedFailureInsideExecuteInTransactionRollsTheTransactionBack()
    {
        var violation = new SqliteException("SQLite Error 19: 'UNIQUE constraint failed: TGeneratedItem.Name'.", 19);
        var database = new ScriptedDatabase(SqliteFactory.Instance)
            .When(command => command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal), ScriptedResult.RowCount(1))
            .When(command => command.CommandText.StartsWith("DELETE", StringComparison.Ordinal), ScriptedResult.Throws(violation));
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();

        var thrown = await Assert.ThrowsAsync<SqliteException>(() => inquiry.ExecuteInTransactionAsync(async transaction =>
        {
            await transaction.ExecuteAsync($"UPDATE TGeneratedItem SET Name = {"b"} WHERE Id = {1}");
            await transaction.ExecuteAsync($"DELETE FROM TGeneratedItem WHERE Id = {2}");
        }));

        Assert.Same(violation, thrown);
        var transactionRecord = Assert.Single(database.Transactions);
        Assert.Equal(RecordedTransactionOutcome.RolledBack, transactionRecord.Outcome);
        Assert.Collection(
            database.Commands,
            update =>
            {
                Assert.Same(transactionRecord, update.Transaction);
                Assert.Equal(1, update.RecordsAffected);
                Assert.Null(update.Exception);
            },
            delete =>
            {
                Assert.Same(transactionRecord, delete.Transaction);
                Assert.Same(violation, delete.Exception);
            });
    }

    [Fact]
    public async Task CommittedTransactionIsRecordedAsCommitted()
    {
        var database = new ScriptedDatabase(SqliteFactory.Instance)
            .When(_ => true, ScriptedResult.RowCount(1));
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();

        await inquiry.ExecuteInTransactionAsync(
            transaction => transaction.ExecuteAsync($"UPDATE TGeneratedItem SET Name = {"b"}"),
            IsolationLevel.Serializable);

        var transactionRecord = Assert.Single(database.Transactions);
        Assert.Equal(RecordedTransactionOutcome.Committed, transactionRecord.Outcome);
        Assert.Equal(IsolationLevel.Serializable, transactionRecord.IsolationLevel);
        Assert.Same(transactionRecord, Assert.Single(database.Commands).Transaction);
    }

    [Fact]
    public async Task UnscriptedCommandFailsWithItsTextAndParameters()
    {
        var database = new ScriptedDatabase(SqliteFactory.Instance);
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => inquiry.ExecuteAsync($"DELETE FROM TGeneratedItem WHERE Name = {"gone"}"));

        Assert.Contains("No scripted result matches this command", exception.Message, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM TGeneratedItem WHERE Name = @p0", exception.Message, StringComparison.Ordinal);
        Assert.Contains("@p0 (String) = gone", exception.Message, StringComparison.Ordinal);
        Assert.Same(exception, Assert.Single(database.Commands).Exception);
    }

    [Fact]
    public async Task RuleThatThrowsFailsTheCommandWithItsDescription()
    {
        var database = new ScriptedDatabase(SqliteFactory.Instance)
            .When(command => (string?)command.Parameters[5].Value == "x", ScriptedResult.RowCount(1));
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => inquiry.ExecuteAsync($"DELETE FROM TGeneratedItem"));

        Assert.StartsWith("A When rule threw for this command.", exception.Message, StringComparison.Ordinal);
        Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
        Assert.Same(exception, Assert.Single(database.Commands).Exception);
    }

    [Fact]
    public async Task CancelledCallerTokenStopsTheCommand()
    {
        var database = new ScriptedDatabase(SqliteFactory.Instance)
            .When(_ => true, ScriptedResult.RowCount(1));
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => inquiry.ExecuteAsync($"UPDATE TGeneratedItem SET Name = {"b"}", cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task BatchReusesOneCommandAndRecordsEachItemsValues()
    {
        Assert.False(SqliteFactory.Instance.CanCreateBatch);
        var database = new ScriptedDatabase(SqliteFactory.Instance)
            .When(command => command.CommandText.StartsWith("INSERT", StringComparison.Ordinal), ScriptedResult.RowCount(1));
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();

        var affected = await inquiry.ExecuteBatchAsync(
            "INSERT INTO TGeneratedItem (Name) VALUES (@Name)",
            _names,
            static (target, name) =>
            {
                var parameter = target.CreateParameter();
                parameter.ParameterName = "@Name";
                parameter.DbType = DbType.String;
                parameter.Value = name;
                target.AddParameter(parameter);
            });

        Assert.Equal(3, affected);
        Assert.Equal(RecordedTransactionOutcome.Committed, Assert.Single(database.Transactions).Outcome);
        Assert.Equal(_names, database.Commands.Select(command => (string?)Assert.Single(command.Parameters).Value).ToArray());
    }

    [Fact]
    public async Task ParameterOfAnotherProviderIsRejectedAsTheProviderDoes()
    {
        var database = new ScriptedDatabase(SqliteFactory.Instance).When(_ => true, ScriptedResult.RowCount(1));
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();

        await Assert.ThrowsAsync<InvalidCastException>(() => inquiry.ExecuteAsync(new InquiryCommand(
            "UPDATE TGeneratedItem SET Name = @p",
            command => command.Parameters.Add(new Microsoft.Data.SqlClient.SqlParameter("@p", "x")))));
    }

    [Fact]
    public async Task SavepointNameStaysUsableAfterARollbackToIt()
    {
        var database = new ScriptedDatabase(SqliteFactory.Instance).When(_ => true, ScriptedResult.RowCount(1));
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();
        await using var transaction = await inquiry.BeginTransactionAsync();

        await transaction.Transaction.SaveAsync("a");
        await transaction.ExecuteAsync($"UPDATE TGeneratedItem SET Name = {"first"}");
        await transaction.Transaction.RollbackAsync("a");
        await transaction.ExecuteAsync($"UPDATE TGeneratedItem SET Name = {"second"}");
        await transaction.Transaction.RollbackAsync("a");
        await transaction.CommitAsync();

        var (outer, savepoint) = (database.Transactions[0], database.Transactions[1]);
        Assert.Equal(RecordedTransactionOutcome.RolledBack, savepoint.Outcome);
        Assert.Equal(RecordedTransactionOutcome.Committed, outer.Outcome);
        Assert.Collection(
            database.Commands,
            first => Assert.Same(savepoint, first.Transaction),
            second => Assert.Same(outer, second.Transaction));
    }

    [Fact]
    public async Task SecondRollbackToASavepointUndoesSavepointsBegunAfterTheFirst()
    {
        var database = new ScriptedDatabase(SqliteFactory.Instance).When(_ => true, ScriptedResult.RowCount(1));
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();
        await using var transaction = await inquiry.BeginTransactionAsync();

        await transaction.Transaction.SaveAsync("a");
        await transaction.Transaction.RollbackAsync("A");
        await transaction.Transaction.SaveAsync("b");
        await transaction.ExecuteAsync($"INSERT INTO TGeneratedItem (Name) VALUES ({"x"})");
        await transaction.Transaction.RollbackAsync("a");
        await transaction.ExecuteAsync($"INSERT INTO TGeneratedItem (Name) VALUES ({"y"})");
        await transaction.CommitAsync();

        var (outer, savepointB) = (database.Transactions[0], database.Transactions[2]);
        Assert.Equal(RecordedTransactionOutcome.RolledBack, savepointB.Outcome);
        Assert.Collection(
            database.Commands,
            first => Assert.Same(savepointB, first.Transaction),
            second => Assert.Same(outer, second.Transaction));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var other = await inquiry.BeginTransactionAsync();
            await other.Transaction.ReleaseAsync("unknown");
        });
    }

    [Fact]
    public async Task CommandWithoutATransactionJoinsTheConnectionsTransactionAsOnSqlite()
    {
        var database = new ScriptedDatabase(SqliteFactory.Instance).When(_ => true, ScriptedResult.RowCount(1));
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();
        await using var transaction = await inquiry.BeginTransactionAsync();

        await using (var command = transaction.Connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM TGeneratedItem";
            Assert.Equal(1, await command.ExecuteNonQueryAsync());
        }

        await transaction.CommitAsync();
        Assert.Same(Assert.Single(database.Transactions), Assert.Single(database.Commands).Transaction);
    }

    private static readonly string[] _names = { "a", "b", "c" };

    private static ServiceProvider CreateServices(ScriptedDatabase database)
    {
        var services = new ServiceCollection();
        services.AddInquiry(typeof(GeneratedItemStore).Assembly);
        services.AddInquirySqlite(database.DataSource);
        return services.BuildServiceProvider();
    }

    private sealed class ScriptedItemRepository(IInquiry inquiry, GeneratedItemStore items)
    {
        public Task<IReadOnlyList<NameMappedPair>> ListPairsAsync(string label)
            => inquiry.QueryListAsync<NameMappedPair>($"SELECT Label, Second, First FROM Pairs WHERE Label = {label}");

        public Task<GeneratedItem?> FindAsync(int id) => items.SelectByKeyAsync(id);

        public Task<long> CountAsync() => inquiry.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM TGeneratedItem");
    }
}
