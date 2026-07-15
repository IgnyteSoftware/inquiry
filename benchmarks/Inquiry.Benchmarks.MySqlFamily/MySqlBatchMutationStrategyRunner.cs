using System.Data.Common;
using System.Text;
using Inquiry.Parameters;
using MySqlConnector;

namespace Inquiry.Benchmarks.MySqlFamily;

internal readonly record struct MySqlBatchMutationItem(int Id, string Value);

/// <summary>
/// Direct-driver floor used by the MySQL and MariaDB benchmark projects. Each operation opens one
/// connection, commits one transaction, and returns the provider's affected-row count. Database
/// reset and seed are intentionally exposed separately so BenchmarkDotNet setup keeps them outside
/// the measured path.
/// </summary>
internal sealed class MySqlBatchMutationStrategyRunner(string connectionString)
{
    private const string TableName = "InquiryBatchEvidence";
    private const string InsertSql =
        "INSERT INTO `InquiryBatchEvidence` (`Id`, `ValueText`) VALUES (@id, @value);";
    private const string UpdateSql =
        "UPDATE `InquiryBatchEvidence` SET `ValueText` = @value WHERE `Id` = @id;";
    private const string DeleteSql =
        "DELETE FROM `InquiryBatchEvidence` WHERE `Id` = @id;";

    public async Task InitializeAsync()
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            CREATE TABLE IF NOT EXISTS `{TableName}` (
                `Id` INT NOT NULL PRIMARY KEY,
                `ValueText` VARCHAR(100) NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    public async Task ResetAsync(int rows)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);

        await using (var truncate = connection.CreateCommand())
        {
            truncate.CommandText = $"TRUNCATE TABLE `{TableName}`;";
            await truncate.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = InsertSql;
        var id = insert.Parameters.Add("id", MySqlDbType.Int32);
        var value = insert.Parameters.Add("value", MySqlDbType.VarChar, 100);
        await insert.PrepareAsync().ConfigureAwait(false);
        for (var i = 0; i < rows; i++)
        {
            id.Value = i + 1;
            value.Value = $"Seed {i}";
            await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
    }

    public Task<int> InsertReusedCommandAsync(IReadOnlyList<MySqlBatchMutationItem> items)
        => ExecuteReusedCommandAsync(InsertSql, items, includeValue: true);

    public Task<int> UpdateReusedCommandAsync(IReadOnlyList<MySqlBatchMutationItem> items)
        => ExecuteReusedCommandAsync(UpdateSql, items, includeValue: true);

    public Task<int> DeleteReusedCommandAsync(IReadOnlyList<int> ids)
        => ExecuteReusedDeleteAsync(ids);

    public Task<int> InsertDbBatchAsync(IReadOnlyList<MySqlBatchMutationItem> items)
        => ExecuteDbBatchAsync(InsertSql, items, includeValue: true);

    public Task<int> UpdateDbBatchAsync(IReadOnlyList<MySqlBatchMutationItem> items)
        => ExecuteDbBatchAsync(UpdateSql, items, includeValue: true);

    public Task<int> DeleteDbBatchAsync(IReadOnlyList<int> ids)
        => ExecuteDbBatchDeleteAsync(ids);

    public Task<int> InsertSetBasedAsync(IReadOnlyList<MySqlBatchMutationItem> items)
        => ExecuteSetBasedInsertAsync(items);

    public Task<int> UpdateSetBasedAsync(IReadOnlyList<MySqlBatchMutationItem> items)
        => ExecuteSetBasedUpdateAsync(items);

    public Task<int> UpdateDerivedTableJoinAsync(IReadOnlyList<MySqlBatchMutationItem> items)
        => ExecuteDerivedTableJoinUpdateAsync(items);

    public Task<int> DeleteSetBasedAsync(IReadOnlyList<int> ids)
        => ExecuteSetBasedDeleteAsync(ids);

    public Task<int> DeleteJsonTableAsync(IReadOnlyList<int> ids)
        => ExecuteJsonTableDeleteAsync(ids);

    private async Task<int> ExecuteReusedCommandAsync(
        string commandText,
        IReadOnlyList<MySqlBatchMutationItem> items,
        bool includeValue)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        var id = command.Parameters.Add("id", MySqlDbType.Int32);
        MySqlParameter? value = null;
        if (includeValue)
        {
            value = command.Parameters.Add("value", MySqlDbType.VarChar, 100);
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

    private async Task<int> ExecuteDbBatchAsync(
        string commandText,
        IReadOnlyList<MySqlBatchMutationItem> items,
        bool includeValue)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using DbBatch batch = connection.CreateBatch();
        batch.Transaction = transaction;

        for (var i = 0; i < items.Count; i++)
        {
            var command = batch.CreateBatchCommand();
            command.CommandText = commandText;
            AddParameter(command, "id", MySqlDbType.Int32, size: 0, items[i].Id);
            if (includeValue) AddParameter(command, "value", MySqlDbType.VarChar, size: 100, items[i].Value);
            batch.BatchCommands.Add(command);
        }

        var affected = await batch.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteReusedDeleteAsync(IReadOnlyList<int> ids)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = DeleteSql;
        var id = command.Parameters.Add("id", MySqlDbType.Int32);
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

    private async Task<int> ExecuteDbBatchDeleteAsync(IReadOnlyList<int> ids)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using DbBatch batch = connection.CreateBatch();
        batch.Transaction = transaction;
        for (var i = 0; i < ids.Count; i++)
        {
            var command = batch.CreateBatchCommand();
            command.CommandText = DeleteSql;
            AddParameter(command, "id", MySqlDbType.Int32, size: 0, ids[i]);
            batch.BatchCommands.Add(command);
        }

        var affected = await batch.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteSetBasedInsertAsync(IReadOnlyList<MySqlBatchMutationItem> items)
    {
        var sql = new StringBuilder(
            "INSERT INTO `InquiryBatchEvidence` (`Id`, `ValueText`) VALUES ");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0) sql.Append(',');
            sql.Append("(@id").Append(i).Append(", @value").Append(i).Append(')');
            command.Parameters.Add($"id{i}", MySqlDbType.Int32).Value = items[i].Id;
            command.Parameters.Add($"value{i}", MySqlDbType.VarChar, 100).Value = items[i].Value;
        }

        command.CommandText = sql.Append(';').ToString();
        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteSetBasedUpdateAsync(IReadOnlyList<MySqlBatchMutationItem> items)
    {
        var sql = new StringBuilder("UPDATE `InquiryBatchEvidence` SET `ValueText` = CASE `Id` ");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        for (var i = 0; i < items.Count; i++)
        {
            sql.Append("WHEN @id").Append(i).Append(" THEN @value").Append(i).Append(' ');
            command.Parameters.Add($"id{i}", MySqlDbType.Int32).Value = items[i].Id;
            command.Parameters.Add($"value{i}", MySqlDbType.VarChar, 100).Value = items[i].Value;
        }

        sql.Append("END WHERE `Id` IN (");
        AppendParameterList(sql, "id", items.Count);
        command.CommandText = sql.Append(");").ToString();
        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteDerivedTableJoinUpdateAsync(IReadOnlyList<MySqlBatchMutationItem> items)
    {
        var sql = new StringBuilder("UPDATE `InquiryBatchEvidence` AS `_t` INNER JOIN (");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        for (var i = 0; i < items.Count; i++)
        {
            sql.Append(i == 0 ? "SELECT " : " UNION ALL SELECT ");
            sql.Append("@_u").Append(i).Append("_0");
            if (i == 0) sql.Append(" AS `Id`");
            sql.Append(", @_u").Append(i).Append("_1");
            if (i == 0) sql.Append(" AS `ValueText`");
            command.Parameters.Add($"_u{i}_0", MySqlDbType.Int32).Value = items[i].Id;
            command.Parameters.Add($"_u{i}_1", MySqlDbType.VarChar, 100).Value = items[i].Value;
        }

        sql.Append(") AS `_v` ON `_t`.`Id` = `_v`.`Id` ")
            .Append("SET `_t`.`ValueText` = `_v`.`ValueText`;");
        command.CommandText = sql.ToString();
        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteSetBasedDeleteAsync(IReadOnlyList<int> ids)
    {
        var sql = new StringBuilder("DELETE FROM `InquiryBatchEvidence` WHERE `Id` IN (");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        for (var i = 0; i < ids.Count; i++)
        {
            command.Parameters.Add($"id{i}", MySqlDbType.Int32).Value = ids[i];
        }

        AppendParameterList(sql, "id", ids.Count);
        command.CommandText = sql.Append(");").ToString();
        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteJsonTableDeleteAsync(IReadOnlyList<int> ids)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            DELETE FROM `InquiryBatchEvidence`
            WHERE `Id` IN (
                SELECT jt.val
                FROM JSON_TABLE(@ids, '$[*]' COLUMNS(val INT PATH '$')) jt
            );
            """;
        InquiryJsonArrayParameter.Bind(command, "ids", ids);
        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private static void AddParameter(
        DbBatchCommand command,
        string name,
        MySqlDbType type,
        int size,
        object value)
    {
        var parameter = (MySqlParameter)command.CreateParameter();
        parameter.ParameterName = name;
        parameter.MySqlDbType = type;
        if (size > 0) parameter.Size = size;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static void AppendParameterList(StringBuilder sql, string prefix, int count)
    {
        for (var i = 0; i < count; i++)
        {
            if (i > 0) sql.Append(',');
            sql.Append('@').Append(prefix).Append(i);
        }
    }
}
