using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Benchmarks.Oracle;

internal readonly record struct OracleBatchMutationItem(int Id, string Value);

internal sealed class OracleBatchMutationStrategyRunner(string connectionString)
{
    private const string InsertSql =
        "INSERT INTO INQUIRYBATCHEVIDENCE (ID, VALUETEXT) VALUES (:id, :value)";
    private const string UpdateSql =
        "UPDATE INQUIRYBATCHEVIDENCE SET VALUETEXT = :value WHERE ID = :id";
    private const string DeleteSql =
        "DELETE FROM INQUIRYBATCHEVIDENCE WHERE ID = :id";

    public async Task InitializeAsync()
    {
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = 'INQUIRYBATCHEVIDENCE'";
        var count = Convert.ToInt32(await exists.ExecuteScalarAsync().ConfigureAwait(false));
        if (count != 0) return;

        await using var create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE INQUIRYBATCHEVIDENCE (
                ID NUMBER(10) NOT NULL PRIMARY KEY,
                VALUETEXT VARCHAR2(100) NOT NULL
            )
            """;
        await create.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task ResetAsync(int rows)
    {
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using (var truncate = connection.CreateCommand())
        {
            truncate.CommandText = "TRUNCATE TABLE INQUIRYBATCHEVIDENCE";
            await truncate.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var insert = connection.CreateCommand();
        insert.Transaction = (OracleTransaction)transaction;
        insert.BindByName = true;
        insert.CommandText = InsertSql;
        var id = insert.Parameters.Add("id", OracleDbType.Int32);
        var value = insert.Parameters.Add("value", OracleDbType.Varchar2, 100);
        await insert.PrepareAsync().ConfigureAwait(false);
        for (var i = 0; i < rows; i++)
        {
            id.Value = i + 1;
            value.Value = $"Seed {i}";
            await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }

    public Task<int> InsertReusedCommandAsync(IReadOnlyList<OracleBatchMutationItem> items)
        => ExecuteReusedCommandAsync(InsertSql, items, includeValue: true);

    public Task<int> UpdateReusedCommandAsync(IReadOnlyList<OracleBatchMutationItem> items)
        => ExecuteReusedCommandAsync(UpdateSql, items, includeValue: true);

    public Task<int> DeleteReusedCommandAsync(IReadOnlyList<OracleBatchMutationItem> items)
        => ExecuteReusedCommandAsync(DeleteSql, items, includeValue: false);

    public Task<int> InsertArrayBindingAsync(IReadOnlyList<OracleBatchMutationItem> items)
        => ExecuteArrayBindingAsync(InsertSql, items, includeValue: true);

    public Task<int> UpdateArrayBindingAsync(IReadOnlyList<OracleBatchMutationItem> items)
        => ExecuteArrayBindingAsync(UpdateSql, items, includeValue: true);

    public Task<int> DeleteArrayBindingAsync(IReadOnlyList<OracleBatchMutationItem> items)
        => ExecuteArrayBindingAsync(DeleteSql, items, includeValue: false);

    private async Task<int> ExecuteReusedCommandAsync(
        string commandText,
        IReadOnlyList<OracleBatchMutationItem> items,
        bool includeValue)
    {
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (OracleTransaction)transaction;
        command.BindByName = true;
        command.CommandText = commandText;
        var id = command.Parameters.Add("id", OracleDbType.Int32);
        OracleParameter? value = null;
        if (includeValue)
        {
            value = command.Parameters.Add("value", OracleDbType.Varchar2, 100);
        }

        await command.PrepareAsync().ConfigureAwait(false);
        var affected = 0;
        for (var i = 0; i < items.Count; i++)
        {
            id.Value = items[i].Id;
            if (value is not null) value.Value = items[i].Value;
            affected += await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteArrayBindingAsync(
        string commandText,
        IReadOnlyList<OracleBatchMutationItem> items,
        bool includeValue)
    {
        var ids = new int[items.Count];
        for (var i = 0; i < items.Count; i++) ids[i] = items[i].Id;

        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (OracleTransaction)transaction;
        command.BindByName = true;
        command.ArrayBindCount = items.Count;
        command.CommandText = commandText;
        command.Parameters.Add("id", OracleDbType.Int32).Value = ids;
        if (includeValue)
        {
            var values = new string[items.Count];
            for (var i = 0; i < items.Count; i++) values[i] = items[i].Value;
            var value = command.Parameters.Add("value", OracleDbType.Varchar2);
            value.ArrayBindSize = Enumerable.Repeat(100, items.Count).ToArray();
            value.Value = values;
        }

        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }
}
