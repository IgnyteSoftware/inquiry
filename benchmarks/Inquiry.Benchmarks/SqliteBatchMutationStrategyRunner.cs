using Microsoft.Data.Sqlite;

namespace Inquiry.Benchmarks;

internal sealed class SqliteBatchMutationStrategyRunner(string connectionString)
{
    private const string InsertSql = "INSERT INTO InquiryBatchEvidence (Id, ValueText) VALUES ($id, $value);";
    private const string UpdateSql = "UPDATE InquiryBatchEvidence SET ValueText = $value WHERE Id = $id;";
    private const string DeleteSql = "DELETE FROM InquiryBatchEvidence WHERE Id = $id;";

    public async Task ResetAsync(int rows)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM InquiryBatchEvidence;";
            await clear.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = InsertSql;
        var id = insert.Parameters.Add("$id", SqliteType.Integer);
        var value = insert.Parameters.Add("$value", SqliteType.Text);
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
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        for (var i = 0; i < items.Count; i++)
        {
            command.Parameters.Add($"$id{i}", SqliteType.Integer).Value = items[i].Id;
            command.Parameters.Add($"$value{i}", SqliteType.Text).Value = items[i].ValueText;
        }
        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    public async Task<int> DeleteJsonEachAsync(string idsJson)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE FROM InquiryBatchEvidence WHERE Id IN (SELECT CAST(value AS INTEGER) FROM json_each($ids));";
        command.Parameters.Add("$ids", SqliteType.Text).Value = idsJson;
        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteReusedPreparedAsync(
        string commandText,
        IReadOnlyList<BatchMutationBenchmarkItem> items,
        bool includeValue)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        var id = command.Parameters.Add("$id", SqliteType.Integer);
        SqliteParameter? value = includeValue ? command.Parameters.Add("$value", SqliteType.Text) : null;
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
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = DeleteSql;
        var id = command.Parameters.Add("$id", SqliteType.Integer);
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
