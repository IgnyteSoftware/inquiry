using Npgsql;
using NpgsqlTypes;

namespace Inquiry.Benchmarks.PostgreSql;

internal sealed class PostgreSqlBatchMutationStrategyRunner(string connectionString)
{
    private const string InsertSql = "INSERT INTO \"InquiryBatchEvidence\" (\"Id\", \"ValueText\") VALUES (@id, @value);";
    private const string UpdateSql = "UPDATE \"InquiryBatchEvidence\" SET \"ValueText\" = @value WHERE \"Id\" = @id;";
    private const string DeleteSql = "DELETE FROM \"InquiryBatchEvidence\" WHERE \"Id\" = @id;";

    public async Task ResetAsync(int rows)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using (var clear = connection.CreateCommand())
        {
            clear.CommandText = "TRUNCATE TABLE \"InquiryBatchEvidence\";";
            await clear.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = InsertSql;
        var id = insert.Parameters.Add("id", NpgsqlDbType.Integer);
        var value = insert.Parameters.Add("value", NpgsqlDbType.Varchar, 100);
        await insert.PrepareAsync().ConfigureAwait(false);
        for (var i = 0; i < rows; i++)
        {
            id.Value = i + 1;
            value.Value = $"Seed {i}";
            await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        await transaction.CommitAsync().ConfigureAwait(false);
    }

    public Task<int> InsertReusedPreparedAsync(IReadOnlyList<BatchMutationBenchmarkItem> items)
        => ExecuteReusedPreparedAsync(InsertSql, items, includeValue: true);
    public Task<int> UpdateReusedPreparedAsync(IReadOnlyList<BatchMutationBenchmarkItem> items)
        => ExecuteReusedPreparedAsync(UpdateSql, items, includeValue: true);
    public Task<int> DeleteReusedPreparedAsync(IReadOnlyList<int> ids)
        => ExecuteDeleteReusedPreparedAsync(ids);

    public async Task<int> InsertMultiRowAsync(string commandText, IReadOnlyList<BatchMutationBenchmarkItem> items)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        for (var i = 0; i < items.Count; i++)
        {
            command.Parameters.Add($"id{i}", NpgsqlDbType.Integer).Value = items[i].Id;
            command.Parameters.Add($"value{i}", NpgsqlDbType.Varchar, 100).Value = items[i].ValueText;
        }
        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    public async Task<int> UpdateNpgsqlBatchAsync(IReadOnlyList<BatchMutationBenchmarkItem> items)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var batch = new NpgsqlBatch(connection, transaction);
        for (var i = 0; i < items.Count; i++)
        {
            var command = new NpgsqlBatchCommand(UpdateSql);
            command.Parameters.Add(new NpgsqlParameter<int>("id", NpgsqlDbType.Integer) { TypedValue = items[i].Id });
            command.Parameters.Add(new NpgsqlParameter<string>("value", NpgsqlDbType.Varchar)
            {
                Size = 100,
                TypedValue = items[i].ValueText,
            });
            batch.BatchCommands.Add(command);
        }
        var affected = await batch.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    public async Task<int> DeleteAnyAsync(int[] ids)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM \"InquiryBatchEvidence\" WHERE \"Id\" = ANY (@ids);";
        command.Parameters.Add(new NpgsqlParameter<int[]>("ids", NpgsqlDbType.Array | NpgsqlDbType.Integer) { TypedValue = ids });
        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteReusedPreparedAsync(string commandText, IReadOnlyList<BatchMutationBenchmarkItem> items, bool includeValue)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        var id = command.Parameters.Add("id", NpgsqlDbType.Integer);
        NpgsqlParameter? value = includeValue ? command.Parameters.Add("value", NpgsqlDbType.Varchar, 100) : null;
        await command.PrepareAsync().ConfigureAwait(false);
        var affected = 0;
        for (var i = 0; i < items.Count; i++)
        {
            id.Value = items[i].Id;
            if (value is not null) value.Value = items[i].ValueText;
            affected += await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteDeleteReusedPreparedAsync(IReadOnlyList<int> ids)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = DeleteSql;
        var id = command.Parameters.Add("id", NpgsqlDbType.Integer);
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
}
