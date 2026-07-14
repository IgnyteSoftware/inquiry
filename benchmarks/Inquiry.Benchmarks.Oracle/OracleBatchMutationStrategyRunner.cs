using System.Data;
using System.Text;
using Inquiry.Parameters;
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

    public Task<int> DeleteReusedCommandAsync(IReadOnlyList<int> ids)
        => ExecuteReusedDeleteAsync(ids);

    public Task<int> InsertDirectDriverArrayBindingFloorAsync(
        IReadOnlyList<OracleBatchMutationItem> items,
        int[] ids,
        string[] values,
        int[] valueSizes)
        => ExecuteArrayBindingAsync(InsertSql, items.Count, ids, values, valueSizes);

    public Task<int> UpdateDirectDriverArrayBindingFloorAsync(
        IReadOnlyList<OracleBatchMutationItem> items,
        int[] ids,
        string[] values,
        int[] valueSizes)
        => ExecuteArrayBindingAsync(UpdateSql, items.Count, ids, values, valueSizes);

    public Task<int> DeleteDirectDriverArrayBindingFloorAsync(IReadOnlyList<int> ids)
        => ExecuteDirectDriverArrayBindingDeleteFloorAsync(ids);

    public Task<int> InsertGeneratedChunkBinderControlAsync(IReadOnlyList<OracleBatchMutationItem> items)
        => ExecuteGeneratedChunkBinderControlAsync(InsertSql, items);

    public Task<int> UpdateGeneratedChunkBinderControlAsync(IReadOnlyList<OracleBatchMutationItem> items)
        => ExecuteGeneratedChunkBinderControlAsync(UpdateSql, items);

    public Task<int> DeleteGeneratedChunkBinderControlAsync(IReadOnlyList<int> ids)
        => ExecuteGeneratedChunkBinderDeleteControlAsync(ids);

    public Task<int> InsertPreIssue180GeneratedControlAsync(IReadOnlyList<OracleBatchMutationItem> items)
        => ExecutePreIssue180GeneratedInsertSelectControlAsync(items);

    public Task<int> DeleteJsonTableAsync(IReadOnlyList<int> ids)
        => ExecuteJsonTableDeleteAsync(ids);

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
        int count,
        int[] ids,
        string[] values,
        int[] valueSizes)
    {
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (OracleTransaction)transaction;
        command.BindByName = true;
        command.ArrayBindCount = count;
        command.CommandText = commandText;
        command.Parameters.Add("id", OracleDbType.Int32).Value = ids;
        var value = command.Parameters.Add("value", OracleDbType.Varchar2, 100);
        value.ArrayBindSize = valueSizes;
        value.Value = values;

        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteGeneratedChunkBinderControlAsync(
        string commandText,
        IReadOnlyList<OracleBatchMutationItem> items)
    {
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (OracleTransaction)transaction;
        command.BindByName = true;
        command.ArrayBindCount = items.Count;
        command.CommandText = commandText;

        // Match the finalized issue #180 generated chunk binder: allocate object arrays in the
        // measured path, box scalar values, and calculate each variable-width element's actual size.
        // This is a binder-equivalent direct-driver control; it intentionally excludes Inquiry's
        // chunk reader and pipeline dispatch, which require a separate generated-store benchmark.
        var ids = new object?[items.Count];
        var values = new object?[items.Count];
        var valueSizes = new int[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            ids[i] = items[i].Id;
            values[i] = items[i].Value;
            valueSizes[i] = values[i] is string value ? value.Length : 0;
        }

        var id = command.CreateParameter();
        id.ParameterName = "id";
        id.DbType = DbType.Int32;
        id.Value = ids;
        command.Parameters.Add(id);

        var text = command.CreateParameter();
        text.ParameterName = "value";
        text.DbType = DbType.String;
        text.Value = values;
        ((OracleParameter)text).ArrayBindSize = valueSizes;
        command.Parameters.Add(text);

        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteReusedDeleteAsync(IReadOnlyList<int> ids)
    {
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (OracleTransaction)transaction;
        command.BindByName = true;
        command.CommandText = DeleteSql;
        var id = command.Parameters.Add("id", OracleDbType.Int32);
        await command.PrepareAsync().ConfigureAwait(false);
        var affected = 0;
        for (var i = 0; i < ids.Count; i++)
        {
            id.Value = ids[i];
            affected += await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteDirectDriverArrayBindingDeleteFloorAsync(IReadOnlyList<int> ids)
    {
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (OracleTransaction)transaction;
        command.BindByName = true;
        command.ArrayBindCount = ids.Count;
        command.CommandText = DeleteSql;
        command.Parameters.Add("id", OracleDbType.Int32).Value = ids;
        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteGeneratedChunkBinderDeleteControlAsync(IReadOnlyList<int> ids)
    {
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (OracleTransaction)transaction;
        command.BindByName = true;
        command.ArrayBindCount = ids.Count;
        command.CommandText = DeleteSql;

        var values = new object?[ids.Count];
        for (var i = 0; i < ids.Count; i++) values[i] = ids[i];

        var id = command.CreateParameter();
        id.ParameterName = "id";
        id.DbType = DbType.Int32;
        id.Value = values;
        command.Parameters.Add(id);

        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecutePreIssue180GeneratedInsertSelectControlAsync(
        IReadOnlyList<OracleBatchMutationItem> items)
    {
        var sql = new StringBuilder("INSERT INTO INQUIRYBATCHEVIDENCE (ID, VALUETEXT) ");
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (OracleTransaction)transaction;
        command.BindByName = true;
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0) sql.Append(" UNION ALL ");
            sql.Append("SELECT :id").Append(i).Append(", :value").Append(i).Append(" FROM dual");
            command.Parameters.Add($"id{i}", OracleDbType.Int32).Value = items[i].Id;
            command.Parameters.Add($"value{i}", OracleDbType.Varchar2, 100).Value = items[i].Value;
        }

        command.CommandText = sql.ToString();
        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteJsonTableDeleteAsync(IReadOnlyList<int> ids)
    {
        await using var connection = new OracleConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = (OracleTransaction)transaction;
        command.BindByName = true;
        command.CommandText = """
            DELETE FROM INQUIRYBATCHEVIDENCE
            WHERE ID IN (
                SELECT jt.val
                FROM JSON_TABLE(:ids, '$[*]' COLUMNS(val NUMBER(10) PATH '$')) jt
            )
            """;
        InquiryJsonArrayParameter.Bind(command, "ids", ids);
        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }
}
