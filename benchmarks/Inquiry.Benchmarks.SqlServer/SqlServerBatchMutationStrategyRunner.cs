using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;

namespace Inquiry.Benchmarks.SqlServer;

internal sealed class SqlServerBatchMutationStrategyRunner(string connectionString)
{
    private const string InsertSql = "INSERT INTO InquiryBatchEvidence (Id, ValueText) VALUES (@id, @value);";
    private const string UpdateSql = "UPDATE InquiryBatchEvidence SET ValueText = @value WHERE Id = @id;";
    private const string DeleteSql = "DELETE FROM InquiryBatchEvidence WHERE Id = @id;";

    public bool CanCreateBatch { get; private set; }

    public async Task ProbeAsync()
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        CanCreateBatch = connection.CanCreateBatch;
    }

    public async Task ResetAsync(int rows)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using (var clear = connection.CreateCommand())
        {
            clear.CommandText = "TRUNCATE TABLE InquiryBatchEvidence;";
            await clear.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = InsertSql;
        var id = insert.Parameters.Add("@id", SqlDbType.Int);
        var value = insert.Parameters.Add("@value", SqlDbType.NVarChar, 100);
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

    public Task<int> InsertDbBatchAsync(IReadOnlyList<BatchMutationBenchmarkItem> items)
        => ExecuteDbBatchAsync(InsertSql, items, includeValue: true);
    public Task<int> UpdateDbBatchAsync(IReadOnlyList<BatchMutationBenchmarkItem> items)
        => ExecuteDbBatchAsync(UpdateSql, items, includeValue: true);
    public Task<int> DeleteDbBatchAsync(IReadOnlyList<int> ids)
        => ExecuteDeleteDbBatchAsync(ids);

    public async Task<int> InsertMultiRowAsync(string commandText, IReadOnlyList<BatchMutationBenchmarkItem> items)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        for (var i = 0; i < items.Count; i++)
        {
            command.Parameters.Add($"@id{i}", SqlDbType.Int).Value = items[i].Id;
            command.Parameters.Add($"@value{i}", SqlDbType.NVarChar, 100).Value = items[i].ValueText;
        }
        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    public async Task<int> DeleteTvpAsync(DataTable ids)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DELETE target FROM InquiryBatchEvidence target INNER JOIN @ids ids ON ids.Id = target.Id;";
        var parameter = command.Parameters.Add("@ids", SqlDbType.Structured);
        parameter.TypeName = "dbo.InquiryBatchEvidenceIdList";
        parameter.Value = ids;
        var affected = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteReusedPreparedAsync(string commandText, IReadOnlyList<BatchMutationBenchmarkItem> items, bool includeValue)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        var id = command.Parameters.Add("@id", SqlDbType.Int);
        SqlParameter? value = includeValue ? command.Parameters.Add("@value", SqlDbType.NVarChar, 100) : null;
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
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = DeleteSql;
        var id = command.Parameters.Add("@id", SqlDbType.Int);
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

    private async Task<int> ExecuteDbBatchAsync(string commandText, IReadOnlyList<BatchMutationBenchmarkItem> items, bool includeValue)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using DbBatch batch = connection.CreateBatch();
        batch.Transaction = transaction;
        for (var i = 0; i < items.Count; i++)
        {
            var command = batch.CreateBatchCommand();
            command.CommandText = commandText;
            AddParameter(command, "@id", SqlDbType.Int, 0, items[i].Id);
            if (includeValue) AddParameter(command, "@value", SqlDbType.NVarChar, 100, items[i].ValueText);
            batch.BatchCommands.Add(command);
        }
        var affected = await batch.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private async Task<int> ExecuteDeleteDbBatchAsync(IReadOnlyList<int> ids)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using DbBatch batch = connection.CreateBatch();
        batch.Transaction = transaction;
        for (var i = 0; i < ids.Count; i++)
        {
            var command = batch.CreateBatchCommand();
            command.CommandText = DeleteSql;
            AddParameter(command, "@id", SqlDbType.Int, 0, ids[i]);
            batch.BatchCommands.Add(command);
        }
        var affected = await batch.ExecuteNonQueryAsync().ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private static void AddParameter(DbBatchCommand command, string name, SqlDbType type, int size, object value)
    {
        var parameter = (SqlParameter)command.CreateParameter();
        parameter.ParameterName = name;
        parameter.SqlDbType = type;
        if (size > 0) parameter.Size = size;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
