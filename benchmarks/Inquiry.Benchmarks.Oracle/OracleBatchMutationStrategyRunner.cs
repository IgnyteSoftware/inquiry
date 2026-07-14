using Oracle.ManagedDataAccess.Client;

namespace Inquiry.Benchmarks.Oracle;

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

    public Task<int> InsertReusedCommandAsync(int rows)
        => ExecuteReusedCommandAsync(InsertSql, rows, static i => 100_001 + i, "Inserted");

    public Task<int> UpdateReusedCommandAsync(int rows)
        => ExecuteReusedCommandAsync(UpdateSql, rows, static i => i + 1, "Updated");

    public Task<int> DeleteReusedCommandAsync(int rows)
        => ExecuteReusedCommandAsync(DeleteSql, rows, static i => i + 1, valuePrefix: null);

    public Task<int> InsertArrayBindingAsync(int rows)
        => ExecuteArrayBindingAsync(InsertSql, rows, static i => 100_001 + i, "Inserted");

    public Task<int> UpdateArrayBindingAsync(int rows)
        => ExecuteArrayBindingAsync(UpdateSql, rows, static i => i + 1, "Updated");

    public Task<int> DeleteArrayBindingAsync(int rows)
        => ExecuteArrayBindingAsync(DeleteSql, rows, static i => i + 1, valuePrefix: null);

    private async Task<int> ExecuteReusedCommandAsync(
        string commandText,
        int rows,
        Func<int, int> idFactory,
        string? valuePrefix)
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
        if (valuePrefix is not null)
        {
            value = command.Parameters.Add("value", OracleDbType.Varchar2, 100);
        }

        await command.PrepareAsync().ConfigureAwait(false);
        var affected = 0;
        for (var i = 0; i < rows; i++)
        {
            id.Value = idFactory(i);
            if (value is not null) value.Value = $"{valuePrefix} {i}";
            affected += await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteArrayBindingAsync(
        string commandText,
        int rows,
        Func<int, int> idFactory,
        string? valuePrefix)
    {
        var ids = new int[rows];
        for (var i = 0; i < rows; i++) ids[i] = idFactory(i);

        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (OracleTransaction)transaction;
        command.BindByName = true;
        command.ArrayBindCount = rows;
        command.CommandText = commandText;
        command.Parameters.Add("id", OracleDbType.Int32).Value = ids;
        if (valuePrefix is not null)
        {
            var values = new string[rows];
            for (var i = 0; i < rows; i++) values[i] = $"{valuePrefix} {i}";
            var value = command.Parameters.Add("value", OracleDbType.Varchar2);
            value.ArrayBindSize = Enumerable.Repeat(100, rows).ToArray();
            value.Value = values;
        }

        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }
}
