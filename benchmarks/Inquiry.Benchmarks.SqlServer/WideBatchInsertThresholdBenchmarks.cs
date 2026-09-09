using System.Data;
using BenchmarkDotNet.Attributes;
using Inquiry.Entities;
using Inquiry.Stores;
using Microsoft.Data.SqlClient;

namespace Inquiry.Benchmarks.SqlServer;

[MemoryDiagnoser]
[InvocationCount(1)]
public class WideBatchInsertThresholdBenchmarks
{
    private const string InsertSql = "INSERT INTO InquiryWideBatchEvidence (Id,C1,C2,C3,C4,C5,C6,C7,C8,C9) VALUES (@p0,@p1,@p2,@p3,@p4,@p5,@p6,@p7,@p8,@p9)";
    private SqlServerBenchmarkDatabase _database = null!;
    private WideBatchEvidenceItem[] _items = null!;

    [Params(199, 200, 201, 209, 210, 211, 249, 250, 251)]
    public int Rows;

    [GlobalSetup]
    public void Setup()
    {
        _database = SqlServerBenchmarkDatabase.CreateAsync(1).GetAwaiter().GetResult();
        _items = Enumerable.Range(1, Rows).Select(id => new WideBatchEvidenceItem
        {
            Id = id,
            C1 = id + 1,
            C2 = id + 2,
            C3 = id + 3,
            C4 = id + 4,
            C5 = id + 5,
            C6 = id + 6,
            C7 = id + 7,
            C8 = id + 8,
            C9 = id + 9
        }).ToArray();
    }

    [IterationSetup]
    public void Reset()
    {
        using var connection = new SqlConnection(_database.ConnectionString);
        connection.Open();
        using var command = new SqlCommand("TRUNCATE TABLE InquiryWideBatchEvidence", connection);
        command.ExecuteNonQuery();
    }

    [IterationCleanup]
    public void VerifyPersistedRows()
    {
        using var connection = new SqlConnection(_database.ConnectionString);
        connection.Open();
        using var command = new SqlCommand("""
            SELECT COUNT(*) FROM InquiryWideBatchEvidence
            WHERE Id BETWEEN 1 AND @rows AND C1=Id+1 AND C2=Id+2 AND C3=Id+3 AND C4=Id+4 AND C5=Id+5
                AND C6=Id+6 AND C7=Id+7 AND C8=Id+8 AND C9=Id+9
            """, connection);
        command.Parameters.Add("@rows", SqlDbType.Int).Value = Rows;
        if ((int)command.ExecuteScalar()! != Rows)
            throw new InvalidOperationException("Wide insert persisted rows or values differ from the requested input.");
    }

    [GlobalCleanup]
    public void Cleanup() => _database.DisposeAsync().AsTask().GetAwaiter().GetResult();

    [Benchmark(Baseline = true)]
    public Task<int> Inquiry_SelectedInsertAll() => RequireAsync(_database.WideBatchMutations.InsertAllAsync(_items));

    [Benchmark]
    public Task<int> Direct_ReusedPreparedInsert() => RequireAsync(InsertDirectAsync(useBatch: false));

    [Benchmark]
    public Task<int> Native_DbBatchInsert() => RequireAsync(InsertDirectAsync(useBatch: true));

    private async Task<int> RequireAsync(Task<int> execution)
    {
        var affected = await execution.ConfigureAwait(false);
        return affected == Rows ? affected : throw new InvalidOperationException($"Expected {Rows} affected rows, received {affected}.");
    }

    private async Task<int> InsertDirectAsync(bool useBatch)
    {
        await using var connection = new SqlConnection(_database.ConnectionString);
        await connection.OpenAsync().ConfigureAwait(false);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);
        int affected;
        if (useBatch)
        {
            if (!connection.CanCreateBatch)
                throw new NotSupportedException("SqlConnection.CanCreateBatch is false; the native comparison is unavailable.");
            await using var batch = connection.CreateBatch();
            batch.Transaction = transaction;
            foreach (var item in _items)
            {
                var command = batch.CreateBatchCommand();
                command.CommandText = InsertSql;
                for (var column = 0; column < 10; column++)
                {
                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@p" + column;
                    parameter.DbType = DbType.Int32;
                    parameter.Value = ValueAt(item, column);
                    command.Parameters.Add(parameter);
                }
                batch.BatchCommands.Add(command);
            }
            affected = await batch.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        else
        {
            await using var command = new SqlCommand(InsertSql, connection, transaction);
            for (var column = 0; column < 10; column++)
                command.Parameters.Add("@p" + column, SqlDbType.Int).Value = column;
            await command.PrepareAsync().ConfigureAwait(false);
            affected = 0;
            foreach (var item in _items)
            {
                for (var column = 0; column < 10; column++)
                    command.Parameters[column].Value = ValueAt(item, column);
                affected += await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
        await transaction.CommitAsync().ConfigureAwait(false);
        return affected;
    }

    private static int ValueAt(WideBatchEvidenceItem item, int column) => column switch
    {
        0 => item.Id,
        1 => item.C1,
        2 => item.C2,
        3 => item.C3,
        4 => item.C4,
        5 => item.C5,
        6 => item.C6,
        7 => item.C7,
        8 => item.C8,
        _ => item.C9
    };
}

[InquiryTable("InquiryWideBatchEvidence")]
public sealed class WideBatchEvidenceItem
{
    [InquiryKey] public int Id { get; set; }
    [InquiryColumn] public int C1 { get; set; }
    [InquiryColumn] public int C2 { get; set; }
    [InquiryColumn] public int C3 { get; set; }
    [InquiryColumn] public int C4 { get; set; }
    [InquiryColumn] public int C5 { get; set; }
    [InquiryColumn] public int C6 { get; set; }
    [InquiryColumn] public int C7 { get; set; }
    [InquiryColumn] public int C8 { get; set; }
    [InquiryColumn] public int C9 { get; set; }
}

public partial class WideBatchEvidenceStore : InquiryStore<WideBatchEvidenceItem>
{
    [InquiryInsert]
    public partial Task<int> InsertAllAsync(IEnumerable<WideBatchEvidenceItem> items, CancellationToken ct = default);
}
