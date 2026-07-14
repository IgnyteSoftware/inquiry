using System.Data.Common;
using System.Text;
using MySqlConnector;

namespace Inquiry.Benchmarks.MySqlFamily;

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
        var value = insert.Parameters.Add("value", MySqlDbType.VarChar);
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

    public Task<int> InsertDbBatchAsync(int rows)
        => ExecuteDbBatchAsync(InsertSql, rows, static i => 100_001 + i, "Inserted");

    public Task<int> UpdateDbBatchAsync(int rows)
        => ExecuteDbBatchAsync(UpdateSql, rows, static i => i + 1, "Updated");

    public Task<int> DeleteDbBatchAsync(int rows)
        => ExecuteDbBatchAsync(DeleteSql, rows, static i => i + 1, valuePrefix: null);

    public Task<int> InsertSetBasedAsync(int rows)
        => ExecuteSetBasedInsertAsync(rows);

    public Task<int> UpdateSetBasedAsync(int rows)
        => ExecuteSetBasedUpdateAsync(rows);

    public Task<int> DeleteSetBasedAsync(int rows)
        => ExecuteSetBasedDeleteAsync(rows);

    private async Task<int> ExecuteReusedCommandAsync(
        string commandText,
        int rows,
        Func<int, int> idFactory,
        string? valuePrefix)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        var id = command.Parameters.Add("id", MySqlDbType.Int32);
        MySqlParameter? value = null;
        if (valuePrefix is not null)
        {
            value = command.Parameters.Add("value", MySqlDbType.VarChar);
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

    private async Task<int> ExecuteDbBatchAsync(
        string commandText,
        int rows,
        Func<int, int> idFactory,
        string? valuePrefix)
    {
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using DbBatch batch = connection.CreateBatch();
        batch.Transaction = transaction;

        for (var i = 0; i < rows; i++)
        {
            var command = batch.CreateBatchCommand();
            command.CommandText = commandText;
            AddParameter(command, "id", idFactory(i));
            if (valuePrefix is not null) AddParameter(command, "value", $"{valuePrefix} {i}");
            batch.BatchCommands.Add(command);
        }

        var affected = await batch.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteSetBasedInsertAsync(int rows)
    {
        var sql = new StringBuilder(
            "INSERT INTO `InquiryBatchEvidence` (`Id`, `ValueText`) VALUES ");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        for (var i = 0; i < rows; i++)
        {
            if (i > 0) sql.Append(',');
            sql.Append("(@id").Append(i).Append(", @value").Append(i).Append(')');
            command.Parameters.AddWithValue($"id{i}", 100_001 + i);
            command.Parameters.AddWithValue($"value{i}", $"Inserted {i}");
        }

        command.CommandText = sql.Append(';').ToString();
        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteSetBasedUpdateAsync(int rows)
    {
        var sql = new StringBuilder("UPDATE `InquiryBatchEvidence` SET `ValueText` = CASE `Id` ");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        for (var i = 0; i < rows; i++)
        {
            sql.Append("WHEN @id").Append(i).Append(" THEN @value").Append(i).Append(' ');
            command.Parameters.AddWithValue($"id{i}", i + 1);
            command.Parameters.AddWithValue($"value{i}", $"Updated {i}");
        }

        sql.Append("END WHERE `Id` IN (");
        AppendParameterList(sql, "id", rows);
        command.CommandText = sql.Append(");").ToString();
        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteSetBasedDeleteAsync(int rows)
    {
        var sql = new StringBuilder("DELETE FROM `InquiryBatchEvidence` WHERE `Id` IN (");
        await using var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        for (var i = 0; i < rows; i++)
        {
            command.Parameters.AddWithValue($"id{i}", i + 1);
        }

        AppendParameterList(sql, "id", rows);
        command.CommandText = sql.Append(");").ToString();
        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private static void AddParameter(DbBatchCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
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
