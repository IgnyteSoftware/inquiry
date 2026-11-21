using System.Data;
using Inquiry.Commands;
using Inquiry.DependencyInjection;
using Inquiry.Northwind.Models;
using Inquiry.Northwind.Stores;
using Inquiry.SqlServer.DependencyInjection;
using Inquiry.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace Inquiry.SqlServer.Tests;

/// <summary>
/// Runs the built-in SQL Server runtime over a <see cref="ScriptedDatabase"/> (#608). These tests
/// need no container.
/// </summary>
public sealed class ScriptedDatabaseTests
{
    private static readonly string[] _regionNames = { "North", "South", "East" };
    private static readonly int[] _groupIds = { 4, 8 };

    [Fact]
    public async Task GeneratedStoreParametersCarryTheSqlDbTypeTheProductionPathBinds()
    {
        var database = new ScriptedDatabase(SqlClientFactory.Instance)
            .When(command => command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal), ScriptedResult.RowCount(1));
        await using var services = CreateServices(database);
        await using var scope = services.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrderStore>();

        var updated = await orders.UpdateAsync(new Order
        {
            OrderID = 10248,
            CustomerID = "VINET",
            OrderDate = new DateTime(1996, 7, 4, 10, 30, 0, 123, DateTimeKind.Unspecified),
            Freight = 32.38m,
        });

        Assert.True(updated);
        var command = Assert.Single(database.Commands);
        var orderDate = Assert.Single(command.Parameters, parameter => parameter.Name == "@OrderDate");
        Assert.Equal(DbType.DateTime2, orderDate.DbType);
        Assert.Equal(SqlDbType.DateTime2, Assert.IsType<SqlParameter>(orderDate.ProviderParameter).SqlDbType);
        var freight = Assert.Single(command.Parameters, parameter => parameter.Name == "@Freight");
        Assert.Equal(SqlDbType.Decimal, Assert.IsType<SqlParameter>(freight.ProviderParameter).SqlDbType);
        Assert.Equal(32.38m, freight.Value);
    }

    [Fact]
    public async Task HandwrittenSqlMapsScriptedColumnsByName()
    {
        var database = new ScriptedDatabase(SqlClientFactory.Instance)
            .Enqueue(ScriptedResult.Rows(new ScriptedResultSet()
                .Column<string>("CompanyName").Column<string>("CustomerID")
                .Row("Vins et alcools Chevalier", "VINET")));
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();
        var since = new DateTime(1996, 7, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var customer = Assert.Single(await inquiry.QueryListAsync<ScriptedCustomerRow>(
            $"SELECT c.CompanyName, c.CustomerID FROM Customers c WHERE EXISTS (SELECT 1 FROM Orders o WHERE o.CustomerID = c.CustomerID AND o.OrderDate >= {since})"));

        Assert.Equal(("VINET", "Vins et alcools Chevalier"), (customer.CustomerID, customer.CompanyName));
        var parameter = Assert.Single(Assert.Single(database.Commands).Parameters);
        Assert.Equal(since, parameter.Value);
    }

    [Fact]
    public async Task EntityResultMissingAMappedColumnFailsAsTheProductionRuntimeDoes()
    {
        var database = new ScriptedDatabase(SqlClientFactory.Instance)
            .Enqueue(ScriptedResult.Rows(new ScriptedResultSet()
                .Column<string>("CompanyName").Column<string>("CustomerID")));
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();

#pragma warning disable INQ120 // This test checks the run-time error that INQ120 reports at build time.
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => inquiry.QueryListAsync<Customer>($"SELECT CompanyName, CustomerID FROM Customers"));
#pragma warning restore INQ120

        Assert.Contains("property 'ContactName' needs a column named 'ContactName'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Available columns: CompanyName, CustomerID", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScriptedFailureInsideExecuteInTransactionRollsBack()
    {
        var violation = new InvalidOperationException("Duplicate order.");
        var database = new ScriptedDatabase(SqlClientFactory.Instance)
            .Enqueue(ScriptedResult.RowCount(1))
            .Enqueue(ScriptedResult.Throws(violation));
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => inquiry.ExecuteInTransactionAsync(async transaction =>
        {
            await transaction.ExecuteAsync($"UPDATE Orders SET ShipName = {"a"} WHERE OrderID = {1}");
            await transaction.ExecuteAsync($"INSERT INTO Orders (ShipName) VALUES ({"a"})");
        }));

        Assert.Same(violation, thrown);
        var transactionRecord = Assert.Single(database.Transactions);
        Assert.Equal(RecordedTransactionOutcome.RolledBack, transactionRecord.Outcome);
        Assert.All(database.Commands, command => Assert.Same(transactionRecord, command.Transaction));
        Assert.Equal(2, database.Commands.Count);
    }

    [Fact]
    public async Task BatchRunsThroughDbBatchAndRecordsEachItem()
    {
        Assert.True(SqlClientFactory.Instance.CanCreateBatch);
        var database = new ScriptedDatabase(SqlClientFactory.Instance)
            .When(command => command.CommandText.StartsWith("INSERT", StringComparison.Ordinal), ScriptedResult.RowCount(1));
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();

        var affected = await inquiry.ExecuteBatchAsync(
            "INSERT INTO Region (RegionDescription) VALUES (@Name)",
            _regionNames,
            BindName);

        Assert.Equal(3, affected);
        var transactionRecord = Assert.Single(database.Transactions);
        Assert.Equal(RecordedTransactionOutcome.Committed, transactionRecord.Outcome);
        Assert.Equal(
            _regionNames,
            database.Commands.Select(command => (string?)Assert.Single(command.Parameters).Value).ToArray());
        Assert.All(database.Commands, command => Assert.Same(transactionRecord, command.Transaction));
    }

    [Fact]
    public async Task UnscriptedCommandFailsWithItsTextAndParameters()
    {
        var database = new ScriptedDatabase(SqlClientFactory.Instance);
        await using var services = CreateServices(database);
        await using var scope = services.CreateAsyncScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrderStore>();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => orders.SelectByKeyAsync(42));

        Assert.Contains("No scripted result matches this command", exception.Message, StringComparison.Ordinal);
        Assert.Contains("FROM [Orders]", exception.Message, StringComparison.Ordinal);
        Assert.Contains("(Int32) = 42", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoredProcedureReadsScriptedOutputAndReturnValues()
    {
        var database = new ScriptedDatabase(SqlClientFactory.Instance)
            .When(command => command.CommandText == "usp_SumByCategory", ScriptedResult.RowCount(-1).WithOutput("@Total", 19.75m))
            .When(command => command.CommandText == "usp_CountByCategory", ScriptedResult.RowCount(-1).WithReturnValue(3))
            .When(command => command.CommandText == "usp_P478_TouchQuiet", ScriptedResult.RowCount(-1));
        await using var services = CreateServices(database);
        await using var scope = services.CreateAsyncScope();
        var procedures = scope.ServiceProvider.GetRequiredService<ProcItemStore>();

        Assert.Equal(19.75m, await procedures.SumByCategoryAsync("coffee"));
        Assert.Equal(3, await procedures.CountByCategoryAsync("coffee"));
        Assert.Equal(-1, await scope.ServiceProvider.GetRequiredService<P478ItemStore>().TouchQuietAsync(2));
        Assert.Equal(-1, database.Commands[2].RecordsAffected);
        Assert.All(database.Commands, command => Assert.Equal(CommandType.StoredProcedure, command.CommandType));
    }

    [Fact]
    public async Task OutputParameterWithoutAScriptedValueFails()
    {
        var database = new ScriptedDatabase(SqlClientFactory.Instance).Enqueue(ScriptedResult.RowCount(-1));
        await using var services = CreateServices(database);
        await using var scope = services.CreateAsyncScope();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<ProcItemStore>().SumByCategoryAsync("coffee"));

        Assert.Contains("Output parameter '@Total'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("WithOutput", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReusingOneSqlParameterAcrossBatchItemsFailsAsSqlClientDoes()
    {
        var database = new ScriptedDatabase(SqlClientFactory.Instance).When(_ => true, ScriptedResult.RowCount(1));
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();
        var shared = new SqlParameter("@Name", SqlDbType.NVarChar);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => inquiry.ExecuteBatchAsync(
            "INSERT INTO Region (RegionDescription) VALUES (@Name)",
            _regionNames,
            (target, name) =>
            {
                shared.Value = name;
                target.AddParameter(shared);
            }));

        Assert.Contains("already contained by another SqlParameterCollection", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BatchWithoutAnyCountReportsMinusOne()
    {
        var database = new ScriptedDatabase(SqlClientFactory.Instance).When(_ => true, ScriptedResult.RowCount(-1));
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();

        var affected = await inquiry.ExecuteBatchAsync(
            "INSERT INTO Region (RegionDescription) VALUES (@Name)", _regionNames, BindName);

        Assert.Equal(-1, affected);
    }

    [Fact]
    public async Task BatchFailureKeepsTheOutcomesOfEarlierItems()
    {
        var violation = new InvalidOperationException("Violation of PRIMARY KEY constraint.");
        var database = new ScriptedDatabase(SqlClientFactory.Instance)
            .Enqueue(ScriptedResult.RowCount(1))
            .Enqueue(ScriptedResult.Throws(violation));
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => inquiry.ExecuteBatchAsync(
            "INSERT INTO Region (RegionDescription) VALUES (@Name)", _regionNames, BindName));

        Assert.Equal(RecordedTransactionOutcome.RolledBack, Assert.Single(database.Transactions).Outcome);
        Assert.Collection(
            database.Commands,
            first => Assert.Equal(1, first.RecordsAffected),
            second => Assert.Same(violation, second.Exception));
    }

    [Fact]
    public async Task TableValuedParameterRowsAreReadAndRecorded()
    {
        var database = new ScriptedDatabase(SqlClientFactory.Instance)
            .Enqueue(ScriptedResult.RowCount(-1).WithOutput("@Total", 12));
        await using var services = CreateServices(database);
        await using var scope = services.CreateAsyncScope();

        var total = await scope.ServiceProvider.GetRequiredService<TvpProcItemStore>().SumGroupIdsAsync(_groupIds);

        Assert.Equal(12, total);
        var ids = Assert.Single(Assert.Single(database.Commands).Parameters, parameter => parameter.Name == "@ids");
        Assert.Equal(SqlDbType.Structured, Assert.IsType<SqlParameter>(ids.ProviderParameter).SqlDbType);
        var rows = Assert.IsAssignableFrom<IReadOnlyList<object?[]>>(ids.Value);
        Assert.Same(rows, ((SqlParameter)ids.ProviderParameter).Value);
        Assert.Equal(_groupIds, rows.Select(row => (int)Assert.Single(row)!).ToArray());
    }

    [Fact]
    public async Task FailingTableValuedParameterSourceFailsTheCommand()
    {
        var database = new ScriptedDatabase(SqlClientFactory.Instance)
            .Enqueue(ScriptedResult.RowCount(-1).WithOutput("@Total", 0));
        await using var services = CreateServices(database);
        await using var scope = services.CreateAsyncScope();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => scope.ServiceProvider.GetRequiredService<TvpProcItemStore>().SumGroupIdsAsync(FailingIds()));

        Assert.Equal("source failed", exception.Message);
        Assert.Same(exception, Assert.Single(database.Commands).Exception);

        static IEnumerable<int> FailingIds()
        {
            yield return 1;
            yield return 2;
            throw new InvalidOperationException("source failed");
        }
    }

    [Fact]
    public async Task NestedTransactionFailureRollsBackOnlyItsSavepoint()
    {
        var database = new ScriptedDatabase(SqlClientFactory.Instance)
            .When(command => command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal), ScriptedResult.RowCount(1))
            .When(command => command.CommandText.StartsWith("INSERT", StringComparison.Ordinal), ScriptedResult.Throws(new InvalidOperationException("Duplicate order.")));
        await using var services = CreateServices(database);
        var inquiry = services.GetRequiredService<IInquiry>();

        await inquiry.ExecuteInTransactionAsync(async outer =>
        {
            await outer.ExecuteAsync($"UPDATE Orders SET ShipName = {"outer"} WHERE OrderID = {1}");
            await Assert.ThrowsAsync<InvalidOperationException>(() => inquiry.ExecuteInTransactionAsync(async inner =>
            {
                await inner.ExecuteAsync($"UPDATE Orders SET ShipName = {"inner"} WHERE OrderID = {2}");
                await inner.ExecuteAsync($"INSERT INTO Orders (ShipName) VALUES ({"inner"})");
            }));
        });

        var transactions = database.Transactions;
        Assert.Equal(2, transactions.Count);
        var (transaction, savepoint) = (transactions[0], transactions[1]);
        Assert.Null(transaction.Parent);
        Assert.Same(transaction, savepoint.Parent);
        Assert.Equal(RecordedTransactionOutcome.Committed, transaction.Outcome);
        Assert.Equal(RecordedTransactionOutcome.RolledBack, savepoint.Outcome);
        Assert.Collection(
            database.Commands,
            outerUpdate => Assert.Same(transaction, outerUpdate.Transaction),
            innerUpdate => Assert.Same(savepoint, innerUpdate.Transaction),
            innerInsert => Assert.Same(savepoint, innerInsert.Transaction));
    }

    private static void BindName(InquiryParameterTarget target, string name)
    {
        var parameter = target.CreateParameter();
        parameter.ParameterName = "@Name";
        parameter.DbType = DbType.String;
        parameter.Value = name;
        target.AddParameter(parameter);
    }

    private static ServiceProvider CreateServices(ScriptedDatabase database)
    {
        var services = new ServiceCollection();
        services.AddInquiry(typeof(CustomerStore).Assembly, typeof(ProcItemStore).Assembly);
        services.AddInquirySqlServer(database.DataSource);
        return services.BuildServiceProvider();
    }
}

[Inquiry.Entities.InquiryAdHoc]
public sealed class ScriptedCustomerRow
{
    public string CustomerID { get; set; } = string.Empty;

    public string CompanyName { get; set; } = string.Empty;
}
